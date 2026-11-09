import { Check } from "@/components/icons"
import { useState } from "react"
import { useTranslation } from "react-i18next"
import { useNavigate } from "react-router-dom"
import { Button } from "@/components/ui/button"
import { LANGUAGES, setLanguage, type LanguageCode } from "@/i18n"
import { cn } from "@/lib/utils"
import { BrandLogo } from "@/components/Brand"

/**
 * First open on a new phone (Figma "Welcome"): the app's name, its illustration, and the
 * language, chosen before anything else so every later screen is in it. Languages not yet
 * translated by native speakers are listed but can't be chosen.
 */
export function Component() {
  const { t, i18n } = useTranslation()
  const navigate = useNavigate()
  const [chosen, setChosen] = useState<LanguageCode>(
    i18n.language as LanguageCode
  )

  async function start() {
    await setLanguage(chosen)
    navigate("/login", { replace: true })
  }

  return (
    // Exactly one screen tall, so "Get started" is always in view without scrolling. Only on a
    // phone held sideways, with no room even without the picture, does the page scroll.
    <main className="mx-auto flex h-svh max-w-md flex-col items-center gap-4 overflow-y-auto bg-background px-6 pt-[max(1.5rem,env(safe-area-inset-top))] pb-[max(1.25rem,env(safe-area-inset-bottom))]">
      <h1 className="w-full max-w-36 shrink-0 animate-in fade-in-0 slide-in-from-top-2 duration-500 motion-reduce:animate-none">
        <BrandLogo />
      </h1>
      {/* The illustration takes whatever height is left and is dropped when that is too little
          to read (under 5rem), instead of pushing the button off the screen. */}
      <div className="relative min-h-0 w-full flex-1 [container-type:size]">
        <img
          src="/illustrations/welcome.svg"
          alt=""
          width={313}
          height={324}
          className="absolute inset-0 m-auto hidden size-full max-h-80 animate-in object-contain fade-in-0 zoom-in-95 duration-500 motion-reduce:animate-none [@container(min-height:5rem)]:block"
        />
      </div>
      <fieldset className="w-full shrink-0">
        <legend className="mb-3 text-center text-base font-semibold text-foreground">
          {t("welcome.language")}
        </legend>
        <div className="grid grid-cols-2 gap-2">
          {LANGUAGES.map((l) => {
            const selected = chosen === l.code
            return (
              <label
                key={l.code}
                className={cn(
                  "flex min-h-14 items-center gap-2 rounded-xl bg-card px-3 py-2 shadow-card transition-[box-shadow,transform] duration-150 active:scale-[0.98] has-[:focus-visible]:ring-3 has-[:focus-visible]:ring-ring/50 motion-reduce:transition-none",
                  !l.ready && "opacity-60",
                  selected && "ring-2 ring-primary",
                  l.ready ? "cursor-pointer" : "cursor-not-allowed"
                )}
              >
                <input
                  type="radio"
                  name="language"
                  value={l.code}
                  checked={selected}
                  disabled={!l.ready}
                  onChange={() => setChosen(l.code)}
                  className="sr-only"
                />
                <span className="flex-1">
                  <span className="block text-sm font-semibold text-foreground">
                    {l.label}
                  </span>
                  <span className="block text-xs text-muted-foreground">
                    {!l.ready
                      ? t("welcome.soon")
                      : l.draft
                        ? `${l.sub} · ${t("common.draft")}`
                        : l.sub}
                  </span>
                </span>
                {selected ? (
                  <Check
                    className="size-5 shrink-0 animate-pop text-primary motion-reduce:animate-none"
                    aria-hidden
                  />
                ) : null}
              </label>
            )
          })}
        </div>
        <p className="mt-3 text-center text-xs text-muted-foreground">
          {t("welcome.draftNote")}
        </p>
      </fieldset>
      <Button
        size="xl"
        className="h-15 w-full shrink-0"
        onClick={() => void start()}
      >
        {t("welcome.start")}
      </Button>
    </main>
  )
}
