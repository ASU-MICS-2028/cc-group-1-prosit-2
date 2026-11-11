import { useCallback, useEffect, useRef, useState } from "react"
import { api } from "@/api/client"
import { type CallStats, type Previous, readStats } from "./callStats"

/** What the API hands out to join a teleconsult's call (POST /api/calls/{id}/ticket). */
interface CallTicket {
  ticket: string
  expiresAt: string
  socketPath: string
  iceServers: RTCIceServer[]
}

export type CallPhase =
  | "starting" // asking for the camera and a ticket
  | "waiting" // in the call, the other person has not joined
  | "connecting" // both in, finding a path between the phones
  | "connected"
  | "reconnecting" // the path dropped; WebRTC is trying again
  | "ended"
  | "full"
  | "failed"

export type CallError =
  "camera" | "offline" | "not_allowed" | "closed" | "no_path" | "server"

type Signal =
  | { type: "waiting" | "peer-left" | "full" }
  | { type: "ready"; initiator: boolean; peer: Peer }
  | { type: "peer-joined"; peer: Peer }
  | { type: "offer" | "answer"; sdp: RTCSessionDescriptionInit }
  | { type: "candidate"; candidate: RTCIceCandidateInit | null }
  | { type: "hangup" }

export interface Peer {
  name: string
  role: string
}

/** Low-bandwidth defaults for 2G/3G: a small, slow picture; speech matters more than video. */
const VIDEO: MediaTrackConstraints = {
  width: { ideal: 320 },
  height: { ideal: 240 },
  frameRate: { ideal: 15, max: 15 },
  facingMode: "user",
}
const AUDIO: MediaTrackConstraints = {
  echoCancellation: true,
  noiseSuppression: true,
  autoGainControl: true,
}
/** Upper limit for the video we send; WebRTC goes lower by itself when the network is worse. */
export const MAX_VIDEO_BPS = 250_000

function socketUrl(path: string, ticket: string) {
  const base = import.meta.env.VITE_API_BASE_URL || window.location.origin
  const url = new URL(path, base)
  url.protocol = url.protocol === "https:" ? "wss:" : "ws:"
  url.searchParams.set("ticket", ticket)
  return url.toString()
}

/**
 * A two-person WebRTC call for one teleconsultation (Lab 7 Part C). The API relays the
 * offer, answer and network candidates over a WebSocket; the audio and video then go
 * phone to phone (or through a TURN relay when the networks need one). Logs the real
 * connection numbers to the console every 5 seconds, as the lab asks.
 */
export function useCall(appointmentId: string) {
  const [phase, setPhase] = useState<CallPhase>("starting")
  const [error, setError] = useState<CallError | null>(null)
  const [peer, setPeer] = useState<Peer | null>(null)
  const [stats, setStats] = useState<CallStats | null>(null)
  const [local, setLocal] = useState<MediaStream | null>(null)
  const [remote, setRemote] = useState<MediaStream | null>(null)
  const [micOn, setMicOn] = useState(true)
  const [cameraOn, setCameraOn] = useState(true)

  const pc = useRef<RTCPeerConnection | null>(null)
  const ws = useRef<WebSocket | null>(null)
  const stream = useRef<MediaStream | null>(null)
  const pending = useRef<RTCIceCandidateInit[]>([])
  const initiator = useRef(false)
  const ended = useRef(false)

  const send = useCallback((message: Signal) => {
    if (ws.current?.readyState === WebSocket.OPEN)
      ws.current.send(JSON.stringify(message))
  }, [])

  const hangUp = useCallback(() => {
    ended.current = true
    send({ type: "hangup" })
    ws.current?.close()
    pc.current?.close()
    stream.current?.getTracks().forEach((t) => t.stop())
    setPhase("ended")
  }, [send])

  useEffect(() => {
    ended.current = false
    let cancelled = false
    let timer: ReturnType<typeof setInterval> | undefined
    let ping: ReturnType<typeof setInterval> | undefined

    async function makeOffer(restart = false) {
      const conn = pc.current
      if (!conn) return
      const offer = await conn.createOffer({ iceRestart: restart })
      await conn.setLocalDescription(offer)
      send({ type: "offer", sdp: conn.localDescription!.toJSON() })
    }

    function newConnection(iceServers: RTCIceServer[]) {
      pc.current?.close()
      const conn = new RTCPeerConnection({ iceServers })
      pc.current = conn
      pending.current = []
      const media = stream.current!
      for (const track of media.getTracks()) {
        const sender = conn.addTrack(track, media)
        if (track.kind === "video") {
          const params = sender.getParameters()
          params.encodings = [
            { ...(params.encodings?.[0] ?? {}), maxBitrate: MAX_VIDEO_BPS },
          ]
          // Keep the picture smooth and small rather than sharp and frozen.
          params.degradationPreference = "maintain-framerate"
          void sender.setParameters(params).catch(() => undefined)
        }
      }
      conn.ontrack = (e) =>
        setRemote(e.streams[0] ?? new MediaStream([e.track]))
      conn.onicecandidate = (e) =>
        send({ type: "candidate", candidate: e.candidate?.toJSON() ?? null })
      conn.onconnectionstatechange = () => {
        if (ended.current) return
        switch (conn.connectionState) {
          case "connected":
            setPhase("connected")
            setError(null)
            break
          case "disconnected":
            setPhase("reconnecting")
            break
          case "failed":
            // No path at all: try once more with fresh candidates, then say so.
            if (initiator.current) {
              setPhase("reconnecting")
              void makeOffer(true)
            } else {
              setPhase("failed")
              setError("no_path")
            }
            break
        }
      }
      return conn
    }

    async function start() {
      if (!navigator.onLine) {
        setPhase("failed")
        setError("offline")
        return
      }
      try {
        stream.current = await navigator.mediaDevices.getUserMedia({
          video: VIDEO,
          audio: AUDIO,
        })
      } catch {
        setPhase("failed")
        setError("camera")
        return
      }
      if (cancelled) return
      setLocal(stream.current)

      let ticket: CallTicket
      try {
        ticket = await api<CallTicket>(`/calls/${appointmentId}/ticket`, {
          method: "POST",
        })
      } catch (e) {
        const status = (e as { status?: number }).status
        setPhase("failed")
        setError(
          status === 422
            ? "closed"
            : status === 403 || status === 404
              ? "not_allowed"
              : "server"
        )
        return
      }
      if (cancelled) return

      const socket = new WebSocket(socketUrl(ticket.socketPath, ticket.ticket))
      ws.current = socket
      // Keep the socket busy so no proxy closes it as idle while waiting for the other side.
      ping = setInterval(
        () => send({ type: "ping" } as unknown as Signal),
        20_000
      )
      socket.onclose = () => {
        if (
          !ended.current &&
          !cancelled &&
          pc.current?.connectionState !== "connected"
        ) {
          setPhase((p) => (p === "full" ? p : "failed"))
          setError((e) => e ?? "server")
        }
      }
      socket.onmessage = async (event) => {
        const msg = JSON.parse(String(event.data)) as Signal
        switch (msg.type) {
          case "waiting":
            setPhase("waiting")
            newConnection(ticket.iceServers)
            break
          case "full":
            setPhase("full")
            break
          case "peer-joined":
            setPeer(msg.peer)
            setPhase("connecting")
            initiator.current = false
            newConnection(ticket.iceServers)
            break
          case "ready":
            setPeer(msg.peer)
            setPhase("connecting")
            initiator.current = true
            newConnection(ticket.iceServers)
            await makeOffer()
            break
          case "offer": {
            const conn = pc.current ?? newConnection(ticket.iceServers)
            await conn.setRemoteDescription(msg.sdp)
            for (const c of pending.current.splice(0))
              await conn.addIceCandidate(c)
            const answer = await conn.createAnswer()
            await conn.setLocalDescription(answer)
            send({ type: "answer", sdp: conn.localDescription!.toJSON() })
            break
          }
          case "answer":
            await pc.current?.setRemoteDescription(msg.sdp)
            for (const c of pending.current.splice(0))
              await pc.current?.addIceCandidate(c)
            break
          case "candidate":
            if (!msg.candidate) break
            if (pc.current?.remoteDescription)
              await pc.current.addIceCandidate(msg.candidate)
            else pending.current.push(msg.candidate)
            break
          case "hangup":
          case "peer-left":
            setRemote(null)
            setPeer(null)
            setStats(null)
            setPhase("waiting")
            newConnection(ticket.iceServers) // ready for them to come back
            break
        }
      }

      let previous: Previous | undefined
      timer = setInterval(async () => {
        const conn = pc.current
        if (!conn || conn.connectionState === "closed") return
        const reports = await conn.getStats()
        const { stats: now, next } = readStats(
          reports.values() as Iterable<
            Record<string, unknown> & { type: string; id: string }
          >,
          performance.now(),
          previous
        )
        previous = next
        setStats(now)
        if (conn.connectionState === "connected")
          console.info(
            `[call] path=${now.path} (${now.localCandidate}/${now.remoteCandidate} ${now.protocol ?? ""}) ` +
              `rtt=${now.rttMs ?? "-"}ms lost=${now.packetsLost ?? "-"} (${now.packetLossPct ?? "-"}%) ` +
              `jitter=${now.jitterMs ?? "-"}ms video=${now.videoKbps ?? "-"}kbps ${now.width ?? "-"}x${now.height ?? "-"}@${now.framesPerSecond ?? "-"}fps ` +
              `audio=${now.audioKbps ?? "-"}kbps send=${now.sendKbps ?? "-"}kbps frames=${now.framesDecoded ?? "-"}`
          )
      }, 5000)
    }

    void start()
    return () => {
      cancelled = true
      ended.current = true
      clearInterval(timer)
      clearInterval(ping)
      ws.current?.close()
      pc.current?.close()
      stream.current?.getTracks().forEach((t) => t.stop())
    }
  }, [appointmentId, send])

  const toggleMic = useCallback(() => {
    const on = !micOn
    stream.current?.getAudioTracks().forEach((t) => (t.enabled = on))
    setMicOn(on)
  }, [micOn])

  /** Camera off is also the "audio only" mode for a weak 2G signal: no video is sent at all. */
  const toggleCamera = useCallback(() => {
    const on = !cameraOn
    stream.current?.getVideoTracks().forEach((t) => (t.enabled = on))
    setCameraOn(on)
  }, [cameraOn])

  return {
    phase,
    error,
    peer,
    stats,
    local,
    remote,
    micOn,
    cameraOn,
    toggleMic,
    toggleCamera,
    hangUp,
  }
}
