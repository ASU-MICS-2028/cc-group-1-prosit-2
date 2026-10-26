import { useTranslation } from "react-i18next"
import { EmptyState, PageHeader } from "@/components/PageHeader"
import { useSession } from "@/auth/session"

/**
 * The district officer's anonymised summary (counts only, never names). Online only: it reads
 * Liza's aggregation endpoint (GET /api/surveillance/summary), which is wired up on Sun 1 Nov.
 */
export function Component() {
  const { t } = useTranslation()
  const session = useSession()
  return (
    <>
      <PageHeader title={t("surveillance.title")} />
      <p className="mb-4 text-sm text-muted-foreground">
        {t("surveillance.district", { district: session?.user.district })}
      </p>
      <EmptyState>{t("surveillance.comingSoon")}</EmptyState>
    </>
  )
}
