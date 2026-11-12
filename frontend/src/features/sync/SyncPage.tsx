import { Cloud, CloudOff, RefreshCw } from "@/components/icons"
import { useState } from "react"
import { useTranslation } from "react-i18next"
import { toast } from "@/lib/toast"
import { EmptyState, PageHeader } from "@/components/PageHeader"
import { Button } from "@/components/ui/button"
import { db, recordTable } from "@/db/local"
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
              <QueuedItem
                key={item.seq}
                item={item}
                orphan={isOrphan(item, outbox)}
              />
            )
          )}
        </ul>
      )}
    </>
  )
}

/** A refusal in words: the raw error is the server's reply or the browser's network error. */
function whyFailed(error: string) {
  if (error.includes("wrong_district")) return "failed_district"
  if (error.includes("not on the server yet")) return "failed_patient"
  if (/Failed to fetch|NetworkError|Load failed/i.test(error))
    return "failed_network"
  return "failed"
}

/**
 * One change waiting to be sent. When the server refuses it for good (its own reason, e.g. "The top
 * number must be higher than the bottom number"), that reason is shown, with a way to drop it so
 * the rest of the queue isn't held up by it for ever.
 */
function QueuedItem({
  item,
  orphan,
}: {
  item: OutboxItem
  /** Waiting for a patient that is neither on the server nor queued here: it can never be sent. */
  orphan: boolean
}) {
  const { t } = useTranslation()
  const [confirming, setConfirming] = useState(false)
  const reason = orphan
    ? t("sync.orphan")
    : item.lastError
      ? serverReason(item.lastError)
      : null
  // 422: the server read it and said no for a reason of its own; retrying will not change that.
  const refused =
    orphan || (reason !== null && !!item.lastError?.startsWith("422"))
  return (
    <li className="space-y-2 p-3">
      <div className="flex justify-between gap-3">
        <span className="text-foreground">{t(`entity.${item.entity}`)}</span>
        <span
          className={
            item.lastError ? "text-destructive" : "text-muted-foreground"
          }
        >
          {item.lastError === "conflict"
            ? t("sync.conflict")
            : item.lastError
              ? t(`sync.${whyFailed(item.lastError)}` as const)
              : t("sync.queued")}
        </span>
      </div>
      {reason && refused ? (
        <div className="space-y-2 rounded-lg bg-destructive-soft p-3">
          <p className="text-sm text-destructive">
            {orphan ? reason : t("sync.serverSays", { reason })}
          </p>
          {confirming ? (
            <div className="flex gap-2">
              <Button
                size="lg"
                variant="destructive"
                onClick={() => void discard(item)}
              >
                {t("sync.discardYes")}
              </Button>
              <Button
                size="lg"
                variant="ghost"
                onClick={() => setConfirming(false)}
              >
                {t("sync.discardNo")}
              </Button>
            </div>
          ) : (
            <Button
              size="lg"
              variant="secondary"
              onClick={() => setConfirming(true)}
            >
              {t(`sync.discard_${item.entity}` as const)}
            </Button>
          )}
        </div>
      ) : null}
    </li>
  )
}

/** The server's own words for why it refused a change (the first field it named), if it gave any. */
function serverReason(error: string): string | null {
  const start = error.indexOf("{")
  if (start < 0) return null
  try {
    const body = JSON.parse(error.slice(start)) as {
      errors?: Record<string, string>
    }
    const first = body.errors ? Object.values(body.errors)[0] : undefined
    // "Not on the server yet" fixes itself once its patient is sent: not a refusal.
    return first && !first.includes("not on the server yet") ? first : null
  } catch {
    return null
  }
}

/** Drops a change the server will never accept: from the queue, and the record from this phone if it never reached the server. */
async function discard(item: OutboxItem) {
  await db.outbox.delete(item.seq!)
  if (item.op === "create") await recordTable[item.entity].delete(item.entityId)
}

/** A reading or visit whose patient the server doesn't have and that isn't waiting to be sent either. */
function isOrphan(item: OutboxItem, outbox: OutboxItem[]): boolean {
  if (
    item.entity === "patient" ||
    !item.lastError?.includes("not on the server yet")
  )
    return false
  const patientId = (item.payload as { patientId?: string }).patientId
  return !outbox.some((o) => o.entity === "patient" && o.entityId === patientId)
}
