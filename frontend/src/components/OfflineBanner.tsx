import { CloudOff } from "lucide-react"
import { useTranslation } from "react-i18next"
import { Link } from "react-router-dom"
import { useOnline } from "@/lib/useOnline"
import { useOutbox } from "@/sync/sync"

/**
 * The offline indicator: a strip at the top while there is no network, with how many changes are
 * waiting on this phone. Never colour alone: an icon and words say what is going on.
 */
export function OfflineBanner() {
  const { t } = useTranslation()
  const online = useOnline()
  const waiting = useOutbox()?.length ?? 0
  if (online) return null
  return (
    <Link
      to="/sync"
      role="status"
      className="flex items-center justify-center gap-2 bg-warning-soft px-4 py-2 text-sm font-medium text-warning"
    >
      <CloudOff className="size-4" aria-hidden />
      {t("offline.banner", { count: waiting })}
    </Link>
  )
}
