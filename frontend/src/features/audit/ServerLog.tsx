import { useEffect, useState } from "react"
import { useTranslation } from "react-i18next"
import { ShieldCheck } from "@/components/icons"
import { Card } from "@/components/PageHeader"
import { api } from "@/api/client"
import type { AuditEvent } from "@/db/types"
import { AuditRow } from "./AuditRow"

/** One entry of the server's audit log (GET /api/audit-events). */
export interface ServerEntry {
  id: number
  actorId: string
  role: string | null
  action: "read" | "write" | "delete"
  outcome: "success" | "denied" | "not-found" | "error"
  purpose: "TREAT" | "PUBHLTH" | "HOPERAT"
  entity: string | null
  entityId: string | null
  patientId: string | null
  detail: string | null
  recordedAt: string
}

interface Verification {
  intact: boolean
  entriesChecked: number
  firstBadId: number | null
}

/** The server's entry in the shape the phone's own log uses, so the same row draws both. */
function toEvent(e: ServerEntry): AuditEvent {
  const denied = e.outcome !== "success"
  const entity =
    e.entity === "patient" ||
    e.entity === "vitals" ||
    e.entity === "appointment" ||
    e.entity === "sign-in"
      ? e.entity
      : "page"
  return {
    id: `server-${e.id}`,
    at: e.recordedAt,
    actorId: e.actorId,
    actorName: e.actorId,
    role: e.role ?? "unknown",
    action: denied ? "denied" : e.action === "read" ? "view" : "update",
    entity,
    entityId: e.entityId ?? undefined,
    patientId: e.patientId ?? undefined,
    outcome: denied ? "denied" : "success",
    purpose: e.purpose === "TREAT" ? "TREAT" : "HOPERAT",
    detail: e.detail ?? e.entityId ?? undefined,
    syncedAt: e.recordedAt,
  }
}

/**
 * The server's own copy of the activity log: what every phone has uploaded plus every sign-in,
 * newest first, with a check that its hash chain is still intact (nothing edited or removed).
 * Needs a connection; offline it says so instead of showing nothing.
 */
export function ServerLog() {
  const { t } = useTranslation()
  const [entries, setEntries] = useState<ServerEntry[] | null>(null)
  const [check, setCheck] = useState<Verification | null>(null)
  const [failed, setFailed] = useState(false)

  useEffect(() => {
    let live = true
    Promise.all([
      api<ServerEntry[]>("/audit-events?limit=100"),
      api<Verification>("/audit-events/verify"),
    ])
      .then(([list, verification]) => {
        if (!live) return
        setEntries(list)
        setCheck(verification)
      })
      .catch(() => live && setFailed(true))
    return () => {
      live = false
    }
  }, [])

  return (
    <section className="mt-8" aria-labelledby="server-log-title">
      <h2
        id="server-log-title"
        className="mb-2 px-1 text-base font-semibold text-heading"
      >
        {t("audit.serverTitle")}
      </h2>
      {failed ? (
        <p role="status" className="px-1 text-sm text-muted-foreground">
          {t("audit.serverOffline")}
        </p>
      ) : null}
      {check ? (
        <p
          role="status"
          className={
            check.intact
              ? "mb-3 flex gap-2 px-1 text-sm text-muted-foreground"
              : "mb-3 flex gap-2 rounded-xl bg-destructive-soft p-3 text-sm font-semibold text-destructive"
          }
        >
          {check.intact ? (
            <ShieldCheck className="size-4 shrink-0 text-primary" aria-hidden />
          ) : null}
          {check.intact
            ? t("audit.serverIntact", { count: check.entriesChecked })
            : t("audit.serverBroken", { id: check.firstBadId })}
        </p>
      ) : null}
      {entries?.length === 0 ? (
        <p className="px-1 text-sm text-muted-foreground">
          {t("audit.serverEmpty")}
        </p>
      ) : entries ? (
        <Card className="px-4 py-1">
          <ul className="divide-y divide-border/60">
            {entries.map((e) => (
              <AuditRow key={e.id} e={toEvent(e)} showPatient={false} />
            ))}
          </ul>
        </Card>
      ) : null}
    </section>
  )
}
