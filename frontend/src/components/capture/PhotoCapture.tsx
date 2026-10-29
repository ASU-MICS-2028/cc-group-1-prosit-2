import { useRef, useState } from "react"
import { useTranslation } from "react-i18next"
import { RefreshCw } from "@/components/icons"
import { compressPhoto, useObjectUrl } from "@/lib/media"

/**
 * Take a photo with the back camera (or pick one), shrunk on the phone before it is kept.
 * Shows the photo with "Take again". Used for the thumbprint on the paper consent form and for
 * wound and skin photos.
 */
export function PhotoCapture({
  value,
  onChange,
  label,
  hint,
  alt,
}: {
  value: Blob | undefined
  onChange: (photo: Blob | undefined) => void
  label: string
  hint: string
  alt: string
}) {
  const { t } = useTranslation()
  const input = useRef<HTMLInputElement>(null)
  const [busy, setBusy] = useState(false)
  const [failed, setFailed] = useState(false)
  const url = useObjectUrl(value)

  async function onPick(e: React.ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0]
    e.target.value = ""
    if (!file) return
    setBusy(true)
    setFailed(false)
    try {
      onChange(await compressPhoto(file))
    } catch {
      setFailed(true)
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="rounded-xl bg-card p-4 shadow-card">
      <input
        ref={input}
        type="file"
        accept="image/*"
        capture="environment"
        className="sr-only"
        aria-label={label}
        onChange={(e) => void onPick(e)}
      />
      {url ? (
        <img
          src={url}
          alt={alt}
          className="mb-3 max-h-64 w-full rounded-lg bg-secondary object-contain"
        />
      ) : null}
      <div className="flex items-center gap-3">
        <button
          type="button"
          disabled={busy}
          onClick={() => input.current?.click()}
          className={
            url
              ? "inline-flex h-11 items-center gap-2 rounded-lg px-3 text-sm font-semibold text-primary hover:bg-secondary"
              : "inline-flex h-12 items-center gap-2 rounded-lg bg-primary px-4 text-sm font-semibold text-primary-foreground disabled:opacity-60"
          }
        >
          {url ? <RefreshCw className="size-5" aria-hidden /> : <CameraIcon />}
          {busy ? t("common.loading") : url ? t("capture.retake") : label}
        </button>
        {!url ? <p className="text-xs text-muted-foreground">{hint}</p> : null}
      </div>
      {failed ? (
        <p role="alert" className="mt-2 text-sm text-destructive">
          {t("profile.photoError")}
        </p>
      ) : null}
    </div>
  )
}

// Material "photo_camera" (Apache-2.0), like the rest of the app's icons.
function CameraIcon() {
  return (
    <svg viewBox="0 0 24 24" className="size-5" fill="currentColor" aria-hidden>
      <path d="M12 15.2a3.2 3.2 0 1 0 0-6.4 3.2 3.2 0 0 0 0 6.4z" />
      <path d="M9 2 7.17 4H4c-1.1 0-2 .9-2 2v12c0 1.1.9 2 2 2h16c1.1 0 2-.9 2-2V6c0-1.1-.9-2-2-2h-3.17L15 2H9zm3 15c-2.76 0-5-2.24-5-5s2.24-5 5-5 5 2.24 5 5-2.24 5-5 5z" />
    </svg>
  )
}
