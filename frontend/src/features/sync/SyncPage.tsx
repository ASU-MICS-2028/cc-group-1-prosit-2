import { Cloud, CloudOff, RefreshCw } from "@/components/icons"
import { useState } from "react"
import { useTranslation } from "react-i18next"
import { toast } from "@/lib/toast"
import { EmptyState, PageHeader } from "@/components/PageHeader"
import { Button } from "@/components/ui/button"
import { useOnline } from "@/lib/useOnline"
import type { OutboxItem, Patient } from "@/db/types"
import {
  keepMine,
  takeServerCopy,
  syncNow,
  useOutbox,
  usePendingMedia,
  type SyncResult,
} from "@/sync/sync"

const PATIENT_FIELDS = [
  "givenName",
  "familyName",
  "sex",
  "dateOfBirth",
  "phone",
  "nationalId",
  "community",
  "district",
  "preferredLanguage",
] as const

/** A patient change that met someone else's edit: shows what differs and lets the worker choose. */
function ConflictReview({ item }: { item: OutboxItem }) {
  const { t } = useTranslation()
  const mine = item.payload as Patient
  const theirs = item.serverCopy as Patient
  const differing = PATIENT_FIELDS.filter(
    (f) => (mine[f] ?? "") !== (theirs[f] ?? "")
  )
  return (
    <li className="space-y-3 p-3" data-testid="conflict-review">
      <div>
        <p className="font-medium text-foreground">
          {t("sync.review_title")}: {mine.givenName} {mine.familyName}
        </p>
        <p className="text-sm text-muted-foreground">{t("sync.review_text")}</p>
      </div>
      <dl className="grid grid-cols-[auto_1fr_1fr] gap-x-3 gap-y-1 text-sm">
        <dt />
        <dd className="font-semibold text-foreground">{t("sync.yours")}</dd>
        <dd className="font-semibold text-foreground">{t("sync.theirs")}</dd>
        {differing.map((f) => (
          <div key={f} className="contents">
            <dt className="text-muted-foreground">{t(`sync.field_${f}`)}</dt>
            <dd>{mine[f] || t("sync.empty")}</dd>
            <dd>{theirs[f] || t("sync.empty")}</dd>
          </div>
        ))}
      </dl>
      <div className="flex gap-2">
        <Button size="lg" onClick={() => void keepMine(item.seq!)}>
          {t("sync.keepMine")}
        </Button>
        <Button
          size="lg"
          variant="outline"
          onClick={() => void takeServerCopy(item.seq!)}
        >
          {t("sync.useServer")}
        </Button>
      </div>
    </li>
  )
}

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
      const result = await syncNow()
      setResult(result)
      const text = t("sync.result", {
        sent: result.sent,
        failed: result.failed + result.conflicts,
      })
      if (result.failed + result.conflicts > 0)
        toast.error(t("toast.syncPartial"), text)
      else toast.success(t("toast.synced"), text)
    } catch {
      toast.error(t("login.network"))
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
          {outbox?.map((item) =>
            item.serverCopy ? (
              <ConflictReview key={item.seq} item={item} />
            ) : (
              <li key={item.seq} className="flex justify-between gap-3 p-3">
                <span className="text-foreground">
                  {t(`entity.${item.entity}`)}
                </span>
                <span
                  className={
                    item.lastError
                      ? "text-destructive"
                      : "text-muted-foreground"
                  }
                >
                  {item.lastError === "conflict"
                    ? t("sync.conflict")
                    : item.lastError
                      ? t("sync.failed")
                      : t("sync.queued")}
                </span>
              </li>
            )
          )}
        </ul>
      )}
    </>
  )
}
