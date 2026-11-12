import { useState } from "react"
import { useTranslation } from "react-i18next"
import type { Media } from "@/db/types"
import { usePhotoUrl } from "@/lib/media"
import { cn } from "@/lib/utils"

/**
 * One saved photo as a thumbnail that opens full size: this phone's own copy, or one another
 * worker took, fetched from the server when there is signal.
 */
export function PhotoThumb({
  media,
  n,
  className,
}: {
  media: Media
  n: number
  className?: string
}) {
  const { t } = useTranslation()
  const url = usePhotoUrl(media)
  // The server knows of the photo but the phone that took it has not finished sending it.
  const [broken, setBroken] = useState<string | null>(null)
  const box = cn("size-16 rounded-lg", className)
  if (!url || broken === url)
    return (
      <span
        className={cn(
          box,
          "grid place-items-center bg-secondary p-1 text-center text-[10px] leading-tight text-muted-foreground"
        )}
      >
        {t("capture.photoOffline")}
      </span>
    )
  return (
    <a href={url} target="_blank" rel="noreferrer">
      <img
        src={url}
        alt={t("capture.photoN", { n })}
        onError={() => setBroken(url)}
        className={cn(box, "object-cover")}
      />
    </a>
  )
}
