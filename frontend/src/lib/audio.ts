/** Opus in WebM (Android, desktop) or AAC in MP4 (iPhone), at a voice bitrate: ~60 KB for 30 s. */
export function pickAudioMime() {
  if (typeof MediaRecorder === "undefined") return null
  return (
    ["audio/webm;codecs=opus", "audio/mp4", "audio/webm"].find((m) =>
      MediaRecorder.isTypeSupported(m)
    ) ?? ""
  )
}

export const canRecordAudio = () =>
  pickAudioMime() !== null && !!navigator.mediaDevices?.getUserMedia
