import { useEffect, useRef, useState } from "react"
import { useTranslation } from "react-i18next"
import { RefreshCw } from "@/components/icons"
import { pickAudioMime } from "@/lib/audio"
import { useObjectUrl } from "@/lib/media"
import { cn } from "@/lib/utils"

const MAX_SECONDS = 30

/**
 * One big record button, a running timer that stops itself at 30 s, then playback with
 * "Record again". Works offline; nothing is sent until sync. If the microphone is refused, says
 * so and lets the form fall back to a witness.
 */
export function AudioRecorder({
  value,
  onChange,
  onUnavailable,
}: {
  value: Blob | undefined
  onChange: (clip: Blob | undefined) => void
  /** the phone can't record (no microphone, or permission refused) */
  onUnavailable: () => void
}) {
  const { t } = useTranslation()
  const recorder = useRef<MediaRecorder | null>(null)
  const [seconds, setSeconds] = useState(0)
  const [recording, setRecording] = useState(false)
  const url = useObjectUrl(value)

  useEffect(() => {
    if (!recording) return
    const timer = window.setInterval(() => setSeconds((s) => s + 1), 1000)
    return () => window.clearInterval(timer)
  }, [recording])

  useEffect(() => {
    if (recording && seconds >= MAX_SECONDS) recorder.current?.stop()
  }, [recording, seconds])

  // stop the microphone if the person leaves the page mid-recording
  useEffect(
    () => () => {
      if (recorder.current?.state === "recording") recorder.current.stop()
    },
    []
  )

  async function start() {
    const mime = pickAudioMime()
    let stream: MediaStream
    try {
      stream = await navigator.mediaDevices.getUserMedia({ audio: true })
    } catch {
      onUnavailable()
      return
    }
    const rec = new MediaRecorder(stream, {
      ...(mime ? { mimeType: mime } : {}),
      audioBitsPerSecond: 16_000,
    })
    const chunks: Blob[] = []
    rec.ondataavailable = (e) => chunks.push(e.data)
    rec.onstop = () => {
      stream.getTracks().forEach((track) => track.stop())
      setRecording(false)
      onChange(new Blob(chunks, { type: rec.mimeType || mime || "audio/webm" }))
    }
    recorder.current = rec
    setSeconds(0)
    onChange(undefined)
    rec.start()
    setRecording(true)
  }

  if (value && url && !recording)
    return (
      <div className="space-y-3 rounded-xl bg-card p-4 shadow-card">
        {/* Chrome shows 0:00 for a fresh WebM clip, so say how long it is */}
        {seconds > 0 ? (
          <p className="text-sm font-semibold text-heading">
            {t("capture.recorded", { seconds })}
          </p>
        ) : null}
        <audio controls src={url} className="w-full" />
        <button
          type="button"
          onClick={() => void start()}
          className="inline-flex h-11 items-center gap-2 rounded-lg px-3 text-sm font-semibold text-primary hover:bg-secondary"
        >
          <RefreshCw className="size-5" aria-hidden />
          {t("capture.again")}
        </button>
      </div>
    )

  return (
    <div className="flex items-center gap-4 rounded-xl bg-card p-4 shadow-card">
      <button
        type="button"
        onClick={() => (recording ? recorder.current?.stop() : void start())}
        aria-pressed={recording}
        className={cn(
          "grid size-16 shrink-0 place-items-center rounded-full text-primary-foreground shadow-card transition-colors",
          recording ? "bg-destructive" : "bg-primary"
        )}
      >
        <span className="sr-only">
          {recording ? t("capture.stop") : t("capture.record")}
        </span>
        <span
          aria-hidden
          className={cn(
            "bg-current",
            recording ? "size-5 rounded-sm" : "size-6 rounded-full"
          )}
        />
      </button>
      <div className="min-w-0 text-sm" aria-live="polite">
        {recording ? (
          <p className="flex items-center gap-2 font-semibold text-destructive">
            <span className="size-2.5 animate-pulse rounded-full bg-destructive" />
            {t("capture.recording", {
              seconds: MAX_SECONDS - seconds,
            })}
          </p>
        ) : (
          <p className="font-semibold text-heading">{t("capture.record")}</p>
        )}
        <p className="text-muted-foreground">{t("capture.spokenHint")}</p>
      </div>
    </div>
  )
}
