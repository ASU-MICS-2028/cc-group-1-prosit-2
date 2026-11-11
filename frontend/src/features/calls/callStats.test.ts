import { describe, expect, it } from "vitest"
import { readStats } from "./callStats"

const pair = (local: string, remote: string) => [
  { id: "T", type: "transport", selectedCandidatePairId: "P" },
  {
    id: "P",
    type: "candidate-pair",
    state: "succeeded",
    localCandidateId: "L",
    remoteCandidateId: "R",
    currentRoundTripTime: 0.084,
    availableOutgoingBitrate: 310000,
  },
  { id: "L", type: "local-candidate", candidateType: local, protocol: "udp" },
  { id: "R", type: "remote-candidate", candidateType: remote },
]

const media = (videoBytes: number, audioBytes: number, sent: number) => [
  {
    id: "V",
    type: "inbound-rtp",
    kind: "video",
    bytesReceived: videoBytes,
    packetsLost: 5,
    packetsReceived: 995,
    jitter: 0.012,
    framesPerSecond: 15,
    frameWidth: 320,
    frameHeight: 240,
    framesDecoded: 300,
  },
  {
    id: "A",
    type: "inbound-rtp",
    kind: "audio",
    bytesReceived: audioBytes,
    jitter: 0.004,
  },
  { id: "O", type: "outbound-rtp", kind: "video", bytesSent: sent },
]

describe("readStats", () => {
  it("names the path: same network, through NAT, or relayed", () => {
    expect(readStats(pair("host", "host"), 0).stats.path).toBe("direct")
    expect(readStats(pair("srflx", "host"), 0).stats.path).toBe("stun")
    expect(readStats(pair("host", "prflx"), 0).stats.path).toBe("stun")
    expect(readStats(pair("relay", "srflx"), 0).stats.path).toBe("relay")
    expect(readStats([], 0).stats.path).toBe("unknown")
  })

  it("reads round-trip time, loss, jitter and the picture", () => {
    const { stats } = readStats(
      [...pair("srflx", "srflx"), ...media(0, 0, 0)],
      0
    )
    expect(stats).toMatchObject({
      rttMs: 84,
      packetsLost: 5,
      packetLossPct: 0.5,
      jitterMs: 12,
      framesPerSecond: 15,
      width: 320,
      height: 240,
      protocol: "udp",
      availableOutgoingKbps: 310,
    })
  })

  it("turns byte totals into kilobits per second between two readings", () => {
    const first = readStats([...pair("host", "host"), ...media(0, 0, 0)], 1000)
    const second = readStats(
      [...pair("host", "host"), ...media(50_000, 6_000, 40_000)],
      3000,
      first.next
    )
    expect(second.stats.videoKbps).toBe(200)
    expect(second.stats.audioKbps).toBe(24)
    expect(second.stats.sendKbps).toBe(160)
    expect(first.stats.videoKbps).toBeUndefined()
  })
})
