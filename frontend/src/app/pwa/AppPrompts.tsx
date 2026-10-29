import { useTranslation } from "react-i18next"
import { useRegisterSW } from "virtual:pwa-register/react"
import { Button } from "@/components/ui/button"
import { useInstallPrompt } from "./useInstallPrompt"

/** Screens where a pop-up would get in the way: signing in and the forms. */
const QUIET =
  /^\/(login|patients\/new|patients\/[^/]+\/(vitals|appointments)\/new)/

/**
 * The app's three messages about itself, one at a time:
 * 1. "A new version is ready" (any screen; the person chooses when to reload),
 * 2. "HealthConnect now works offline" (once, after the first install of the files),
 * 3. "Install HealthConnect" (not while signing in or filling a form).
 */
export function AppPrompts({ pathname }: { pathname: string }) {
  const { t } = useTranslation()
  const {
    needRefresh: [needRefresh, setNeedRefresh],
    offlineReady: [offlineReady, setOfflineReady],
    updateServiceWorker,
    // Register now: React mounts after the page's "load" event, which the default waits for.
  } = useRegisterSW({ immediate: true })
  const { canInstall, install, dismiss } = useInstallPrompt()
  const quiet = QUIET.test(pathname)

  if (needRefresh) {
    return (
      <Prompt
        title={t("pwa.updateTitle")}
        text={t("pwa.updateText")}
        primary={{
          label: t("pwa.reload"),
          onClick: () => void updateServiceWorker(true),
        }}
        secondary={{
          label: t("pwa.later"),
          onClick: () => setNeedRefresh(false),
        }}
      />
    )
  }
  if (offlineReady && !quiet) {
    return (
      <Prompt
        title={t("pwa.offlineTitle")}
        text={t("pwa.offlineText")}
        primary={{ label: t("pwa.ok"), onClick: () => setOfflineReady(false) }}
      />
    )
  }
  if (canInstall && !quiet) {
    return (
      <Prompt
        title={t("pwa.installTitle")}
        text={t("pwa.installText")}
        primary={{ label: t("pwa.install"), onClick: () => void install() }}
        secondary={{ label: t("pwa.notNow"), onClick: dismiss }}
      />
    )
  }
  return null
}

function Prompt({
  title,
  text,
  primary,
  secondary,
}: {
  title: string
  text: string
  primary: { label: string; onClick: () => void }
  secondary?: { label: string; onClick: () => void }
}) {
  return (
    // Above the phone's bottom bar; bottom right on computers.
    <section
      role="status"
      aria-label={title}
      className="fixed inset-x-4 bottom-24 z-30 mx-auto flex max-w-md gap-3 rounded-3xl bg-card p-4 shadow-floating md:inset-x-auto md:right-6 md:bottom-6 md:w-96"
    >
      <img
        src="/pwa-64x64.png"
        alt=""
        className="size-11 shrink-0 rounded-xl"
      />
      <div className="min-w-0 flex-1 space-y-3">
        <div>
          <p className="font-semibold text-foreground">{title}</p>
          <p className="text-sm text-muted-foreground">{text}</p>
        </div>
        <div className="flex gap-2">
          <Button
            size="lg"
            onClick={primary.onClick}
            className="rounded-full px-4"
          >
            {primary.label}
          </Button>
          {secondary ? (
            <Button
              size="lg"
              variant="ghost"
              onClick={secondary.onClick}
              className="rounded-full px-4"
            >
              {secondary.label}
            </Button>
          ) : null}
        </div>
      </div>
    </section>
  )
}
