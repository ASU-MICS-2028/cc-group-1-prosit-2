import { CloudOff } from "lucide-react"
import { useTranslation } from "react-i18next"
import { Button } from "@/components/ui/button"

/**
 * Shown instead of React Router's developer error page, e.g. when a screen's code can't be
 * downloaded (a new version was deployed while offline). Saved records are never affected.
 */
export function RouteError() {
  const { t } = useTranslation()
  return (
    <main className="mx-auto flex min-h-svh max-w-md flex-col items-center justify-center gap-4 p-6 text-center">
      <CloudOff className="size-10 text-primary" aria-hidden />
      <h1 className="text-xl font-bold text-heading">
        {t("routeError.title")}
      </h1>
      <p className="text-sm text-muted-foreground">{t("routeError.text")}</p>
      <Button size="xl" className="w-full" onClick={() => location.reload()}>
        {t("routeError.retry")}
      </Button>
    </main>
  )
}
