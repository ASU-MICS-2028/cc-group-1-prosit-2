import { useRef, useState } from "react"
import { useTranslation } from "react-i18next"
import { X } from "@/components/icons"
import { compressPhoto, useObjectUrl } from "@/lib/media"

/**
 * Up to `max` photos as square thumbnails, with an "add" tile that opens the back camera. Each
 * photo is shrunk on the phone first. Used for wound and skin photos on a vitals record.
 */
export function PhotoStrip({
  photos,
  onChange,
  max = 3,
}: {
  photos: Blob[]
  onChange: (photos: Blob[]) => void
  max?: number
}) {
  const { t } = useTranslation()
  const input = useRef<HTMLInputElement>(null)
  const [busy, setBusy] = useState(false)

  async function onPick(e: React.ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0]
    e.target.value = ""
    if (!file) return
    setBusy(true)
    try {
      onChange([...photos, await compressPhoto(file)])
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="grid grid-cols-3 gap-3">
      {photos.map((photo, i) => (
        <Thumb
          key={i}
          photo={photo}
          label={t("capture.photoN", { n: i + 1 })}
          onRemove={() => onChange(photos.filter((_, j) => j !== i))}
        />
      ))}
      {photos.length < max ? (
        <button
          type="button"
          disabled={busy}
          onClick={() => input.current?.click()}
          className="flex aspect-square flex-col items-center justify-center gap-1.5 rounded-xl bg-card text-xs font-semibold text-primary shadow-card disabled:opacity-60"
        >
          <svg
            viewBox="0 0 24 24"
            className="size-7"
            fill="currentColor"
            aria-hidden
          >
            <path d="M12 15.2a3.2 3.2 0 1 0 0-6.4 3.2 3.2 0 0 0 0 6.4z" />
            <path d="M9 2 7.17 4H4c-1.1 0-2 .9-2 2v12c0 1.1.9 2 2 2h16c1.1 0 2-.9 2-2V6c0-1.1-.9-2-2-2h-3.17L15 2H9zm3 15c-2.76 0-5-2.24-5-5s2.24-5 5-5 5 2.24 5 5-2.24 5-5 5z" />
          </svg>
          {busy ? t("common.loading") : t("capture.addPhoto")}
        </button>
      ) : null}
      <input
        ref={input}
        type="file"
        accept="image/*"
        capture="environment"
        className="sr-only"
        aria-label={t("capture.addPhoto")}
        onChange={(e) => void onPick(e)}
      />
    </div>
  )
}

function Thumb({
  photo,
  label,
  onRemove,
}: {
  photo: Blob
  label: string
  onRemove: () => void
}) {
  const { t } = useTranslation()
  const url = useObjectUrl(photo)
  return (
    <div className="relative">
      {url ? (
        <img
          src={url}
          alt={label}
          className="aspect-square w-full rounded-xl object-cover shadow-card"
        />
      ) : null}
      <button
        type="button"
        onClick={onRemove}
        aria-label={t("capture.removePhoto", { label })}
        className="absolute -top-2 -right-2 grid size-8 place-items-center rounded-full bg-foreground text-background shadow-card"
      >
        <X className="size-4" aria-hidden />
      </button>
    </div>
  )
}
