/**
 * Real numbers about a call (Lab 7 C.3), read from RTCPeerConnection.getStats(): how the
 * two phones are connected and how well the video and audio are getting through, so a
 * "it looked fine" can be checked.
 */
export type Path = "direct" | "stun" | "relay" | "unknown"

export interface CallStats {
  /** host = same network, srflx/prflx = through NAT with STUN, relay = through a TURN server */
  path: Path
  localCandidate?: string
  remoteCandidate?: string
  protocol?: string
  rttMs?: number
  /** What arrives from the other side */
  videoKbps?: number
  audioKbps?: number
  packetsLost?: number
  packetLossPct?: number
  jitterMs?: number
  framesPerSecond?: number
  width?: number
  height?: number
  framesDecoded?: number
  /** What we send (lowered by WebRTC itself when the network is slow) */
  sendKbps?: number
  availableOutgoingKbps?: number
}

/** Byte counters from the previous reading, to turn totals into a rate. */
export interface Previous {
  at: number
  videoBytes?: number
  audioBytes?: number
  sentBytes?: number
}

type Report = Record<string, unknown> & { type: string; id: string }

const num = (v: unknown) => (typeof v === "number" ? v : undefined)
const str = (v: unknown) => (typeof v === "string" ? v : undefined)

function pathOf(candidateType?: string): Path {
  switch (candidateType) {
    case "host":
      return "direct"
    case "srflx":
    case "prflx":
      return "stun"
    case "relay":
      return "relay"
    default:
      return "unknown"
  }
}

const kbps = (
  bytes: number | undefined,
  before: number | undefined,
  seconds: number
) =>
  bytes !== undefined && before !== undefined && seconds > 0
    ? Math.max(0, Math.round(((bytes - before) * 8) / 1000 / seconds))
    : undefined

/** Reads one getStats() result; `previous` (if any) gives the bitrates. */
export function readStats(
  reports: Iterable<Report>,
  now: number,
  previous?: Previous
): { stats: CallStats; next: Previous } {
  const byId = new Map<string, Report>()
  for (const r of reports) byId.set(r.id, r)
  const all = [...byId.values()]

  // The pair in use: the transport names it; older browsers only mark it "selected"/"nominated".
  const transport = all.find((r) => r.type === "transport")
  const pair =
    (transport && byId.get(str(transport.selectedCandidatePairId) ?? "")) ??
    all.find(
      (r) =>
        r.type === "candidate-pair" &&
        r.state === "succeeded" &&
        (r.selected === true || r.nominated === true)
    )
  const local = pair && byId.get(str(pair.localCandidateId) ?? "")
  const remote = pair && byId.get(str(pair.remoteCandidateId) ?? "")
  const localType = str(local?.candidateType)
  const remoteType = str(remote?.candidateType)
  // Relayed if either side goes through TURN; direct only if both are on the same network.
  const path: Path =
    localType === "relay" || remoteType === "relay"
      ? "relay"
      : localType === "host" && remoteType === "host"
        ? "direct"
        : pathOf(localType === "host" ? remoteType : localType)

  const video = all.find((r) => r.type === "inbound-rtp" && r.kind === "video")
  const audio = all.find((r) => r.type === "inbound-rtp" && r.kind === "audio")
  const outbound = all.filter((r) => r.type === "outbound-rtp")
  const sentBytes = outbound.length
    ? outbound.reduce((s, r) => s + (num(r.bytesSent) ?? 0), 0)
    : undefined
  const seconds = previous ? (now - previous.at) / 1000 : 0

  const lost = num(video?.packetsLost)
  const received = num(video?.packetsReceived)
  const rtt = num(pair?.currentRoundTripTime)
  const jitter = num(video?.jitter) ?? num(audio?.jitter)
  const outgoing = num(pair?.availableOutgoingBitrate)

  const stats: CallStats = {
    path,
    localCandidate: localType,
    remoteCandidate: remoteType,
    protocol: str(local?.protocol),
    rttMs: rtt !== undefined ? Math.round(rtt * 1000) : undefined,
    videoKbps: kbps(num(video?.bytesReceived), previous?.videoBytes, seconds),
    audioKbps: kbps(num(audio?.bytesReceived), previous?.audioBytes, seconds),
    packetsLost: lost,
    packetLossPct:
      lost !== undefined && received !== undefined && lost + received > 0
        ? Math.round((lost / (lost + received)) * 1000) / 10
        : undefined,
    jitterMs: jitter !== undefined ? Math.round(jitter * 1000) : undefined,
    framesPerSecond: num(video?.framesPerSecond),
    width: num(video?.frameWidth),
    height: num(video?.frameHeight),
    framesDecoded: num(video?.framesDecoded),
    sendKbps: kbps(sentBytes, previous?.sentBytes, seconds),
    availableOutgoingKbps:
      outgoing !== undefined ? Math.round(outgoing / 1000) : undefined,
  }
  return {
    stats,
    next: {
      at: now,
      videoBytes: num(video?.bytesReceived),
      audioBytes: num(audio?.bytesReceived),
      sentBytes,
    },
  }
}
