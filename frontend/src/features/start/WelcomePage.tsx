import { Check } from "@/components/icons"
import { useState } from "react"
import { useTranslation } from "react-i18next"
import { useNavigate } from "react-router-dom"
import { Button } from "@/components/ui/button"
import { LANGUAGES, setLanguage, type LanguageCode } from "@/i18n"
import { cn } from "@/lib/utils"

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
    <main className="mx-auto flex min-h-svh max-w-md flex-col items-center gap-6 bg-background px-6 py-10">
      <h1 className="text-[2.75rem] leading-none font-bold tracking-[0.01em] text-heading">
        HealthConnect
      </h1>
      <img
        src="/illustrations/welcome.svg"
        alt=""
        width={313}
        height={324}
        className="h-auto w-full max-w-64"
      />
      <fieldset className="w-full">
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
                  "flex min-h-14 items-center gap-2 rounded-xl bg-card px-3 py-2 shadow-card has-[:focus-visible]:ring-3 has-[:focus-visible]:ring-ring/50",
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
                  <Check className="size-5 text-primary" aria-hidden />
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
        className="mt-auto h-15 w-full"
        onClick={() => void start()}
      >
        {t("welcome.start")}
      </Button>
    </main>
  )
}
