import { Cloud, CloudOff, RefreshCw } from "@/components/icons"
import { useState } from "react"
import { useTranslation } from "react-i18next"
import { EmptyState, PageHeader } from "@/components/PageHeader"
import { Button } from "@/components/ui/button"
import { useOnline } from "@/lib/useOnline"
import {
  syncNow,
  useOutbox,
  usePendingMedia,
  type SyncResult,
} from "@/sync/sync"

/** What is waiting on this phone, and a button to send it now. */
export function Component() {
  const { t } = useTranslation()
  const online = useOnline()
  const outbox = useOutbox()
  const files = usePendingMedia() ?? 0
  const [busy, setBusy] = useState(false)
  const [result, setResult] = useState<SyncResult | null>(null)

  async function send() {
    setBusy(true)
    try {
      setResult(await syncNow())
    } finally {
      setBusy(false)
    }
  }

  return (
    <>
      <PageHeader title={t("sync.title")} />
      <div className="mb-5 flex items-center gap-3 rounded-xl bg-card shadow-card p-4">
        {online ? (
          <Cloud className="size-6 text-primary" aria-hidden />
        ) : (
          <CloudOff className="size-6 text-warning" aria-hidden />
        )}
        <div className="flex-1">
          <p className="font-medium text-foreground">
            {online ? t("sync.online") : t("sync.offline")}
          </p>
          <p className="text-sm text-muted-foreground">
            {t("sync.waiting", { count: outbox?.length ?? 0 })}
          </p>
          {files ? (
            <p className="text-sm text-muted-foreground">
              {t("sync.files", { count: files })}
            </p>
          ) : null}
        </div>
        <Button
          size="lg"
          onClick={() => void send()}
          disabled={!online || busy || (!outbox?.length && !files)}
        >
          <RefreshCw
            className={busy ? "animate-spin" : undefined}
            aria-hidden
          />
          {t("sync.now")}
        </Button>
      </div>
      {result ? (
        <p role="status" className="mb-4 text-sm text-muted-foreground">
          {t("sync.result", {
            sent: result.sent,
            failed: result.failed + result.conflicts,
          })}
        </p>
      ) : null}
      {outbox?.length === 0 && !files ? (
        <EmptyState illustration="synced">{t("sync.allSent")}</EmptyState>
      ) : outbox?.length === 0 ? null : (
        <ul className="divide-y rounded-xl bg-card shadow-card text-sm">
          {outbox?.map((item) => (
            <li key={item.seq} className="flex justify-between gap-3 p-3">
              <span className="text-foreground">
                {t(`entity.${item.entity}`)}
              </span>
              <span
                className={
                  item.lastError ? "text-destructive" : "text-muted-foreground"
                }
              >
                {item.lastError === "conflict"
                  ? t("sync.conflict")
                  : item.lastError
                    ? t("sync.failed")
                    : t("sync.queued")}
              </span>
            </li>
          ))}
        </ul>
      )}
    </>
  )
}
