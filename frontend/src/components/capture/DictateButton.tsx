import { useEffect, useRef, useState } from "react"
import { useTranslation } from "react-i18next"
import { dictate, dictationState } from "@/lib/dictation"
import { cn } from "@/lib/utils"

/**
 * A microphone button that types what the health worker says into a notes field, recognised on
 * the phone itself. Hidden when the phone has no on-device speech model for the app's language.
 */
export function DictateButton({ onText }: { onText: (text: string) => void }) {
  const { t, i18n } = useTranslation()
  const [usable, setUsable] = useState(false)
  const [listening, setListening] = useState(false)
  const stop = useRef<(() => void) | null>(null)

  useEffect(() => {
    let live = true
    void dictationState(i18n.language).then(
      (s) => live && setUsable(s !== "unavailable")
    )
    return () => {
      live = false
      stop.current?.()
    }
  }, [i18n.language])

  if (!usable) return null

  async function toggle() {
    if (listening) {
      stop.current?.()
      return
    }
    setListening(true)
    try {
      stop.current = await dictate(i18n.language, onText, () =>
        setListening(false)
      )
    } catch {
      setListening(false)
    }
  }

  return (
    <button
      type="button"
      onClick={() => void toggle()}
      aria-pressed={listening}
      className={cn(
        "inline-flex h-10 items-center gap-1.5 rounded-full px-3 text-sm font-semibold",
        listening
          ? "bg-destructive text-background"
          : "bg-secondary text-secondary-foreground"
      )}
    >
      <svg
        viewBox="0 0 24 24"
        className="size-5"
        fill="currentColor"
        aria-hidden
      >
        <path d="M12 14c1.66 0 2.99-1.34 2.99-3L15 5c0-1.66-1.34-3-3-3S9 3.34 9 5v6c0 1.66 1.34 3 3 3zm5.3-3c0 3-2.54 5.1-5.3 5.1S6.7 14 6.7 11H5c0 3.41 2.72 6.23 6 6.72V21h2v-3.28c3.28-.48 6-3.3 6-6.72h-1.7z" />
      </svg>
      {listening ? t("capture.listening") : t("capture.dictate")}
    </button>
  )
}
