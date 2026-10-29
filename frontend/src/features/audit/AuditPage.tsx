import { useLiveQuery } from "dexie-react-hooks"
import { useState } from "react"
import { useTranslation } from "react-i18next"
import { Search, ShieldCheck } from "@/components/icons"
import { Card, EmptyState, PageHeader } from "@/components/PageHeader"
import { db } from "@/db/local"
import type { AuditEvent } from "@/db/types"
import { fullName } from "@/features/patients/usePatients"
import { useAuditEvents } from "@/lib/audit"
import { startOfToday } from "@/lib/caseload"
import { cn } from "@/lib/utils"
import i18n from "@/i18n"
import { AuditRow } from "./AuditRow"

const FILTERS = {
  all: () => true,
  view: (e: AuditEvent) => e.action === "view",
  change: (e: AuditEvent) => e.action === "create" || e.action === "update",
  denied: (e: AuditEvent) => e.outcome === "denied",
} as const
type Filter = keyof typeof FILTERS

/**
 * The auditor's view of the audit trail (workplan: "audit events are visible"): every time
 * patient data was opened, created or changed on this phone, and every refused attempt, newest
 * first and grouped by day. The server keeps the authoritative, hash-chained copy (Lab 6
 * access_log); this page shows what this phone recorded, including events not yet uploaded.
 */
export function Component() {
  const { t } = useTranslation()
  const events = useAuditEvents()
  const names = useLiveQuery(async () => {
    const all = await db.patients.toArray()
    return new Map(all.map((p) => [p.id, fullName(p)]))
  }, [])
  const [filter, setFilter] = useState<Filter>("all")
  const [search, setSearch] = useState("")

  const q = search.trim().toLowerCase()
  const shown = events?.filter(
    (e) =>
      FILTERS[filter](e) &&
      (!q ||
        e.actorName.toLowerCase().includes(q) ||
        (e.patientId && names?.get(e.patientId)?.toLowerCase().includes(q)))
  )
  const today = startOfToday().toISOString()
  const todays = events?.filter((e) => e.at >= today) ?? []

  // Group by calendar day, newest day first.
  const days = new Map<string, AuditEvent[]>()
  for (const e of shown ?? []) {
    const key = e.at.slice(0, 10)
    days.set(key, [...(days.get(key) ?? []), e])
  }

  return (
    <>
      <PageHeader title={t("audit.title")} subtitle={t("audit.subtitle")} />

      <Card className="mb-4 grid grid-cols-3 gap-2 p-4 text-center">
        <Count
          label={t("audit.todayViews")}
          value={todays.filter(FILTERS.view).length}
        />
        <Count
          label={t("audit.todayChanges")}
          value={todays.filter(FILTERS.change).length}
        />
        <Count
          label={t("audit.todayDenied")}
          value={todays.filter(FILTERS.denied).length}
          danger
        />
      </Card>

      <label className="mb-3 flex h-12 items-center gap-2 rounded-xl bg-card px-4 shadow-card focus-within:ring-2 focus-within:ring-primary">
        <Search className="size-6 text-foreground" aria-hidden />
        <input
          type="search"
          aria-label={t("audit.search")}
          placeholder={t("audit.search")}
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          className="h-full min-w-0 flex-1 bg-transparent text-base outline-none placeholder:text-muted-foreground"
        />
      </label>

      <div
        role="radiogroup"
        aria-label={t("patients.filterLabel")}
        className="mb-4 flex flex-wrap gap-2"
      >
        {(Object.keys(FILTERS) as Filter[]).map((f) => (
          <button
            key={f}
            type="button"
            role="radio"
            aria-checked={filter === f}
            onClick={() => setFilter(f)}
            className={cn(
              "inline-flex h-10 shrink-0 items-center rounded-full px-4 text-sm font-semibold whitespace-nowrap",
              filter === f
                ? f === "denied"
                  ? "bg-destructive text-background"
                  : "bg-primary text-primary-foreground"
                : "bg-card text-foreground shadow-card"
            )}
          >
            {t(`audit.filter_${f}`)}
          </button>
        ))}
      </div>

      {shown?.length === 0 ? (
        <EmptyState illustration="no-data">{t("audit.empty")}</EmptyState>
      ) : (
        <div className="space-y-4">
          {[...days].map(([day, list]) => (
            <section key={day}>
              <h2 className="mb-1 px-1 text-sm font-semibold text-muted-foreground first-letter:uppercase">
                {new Intl.DateTimeFormat(i18n.language, {
                  weekday: "long",
                  day: "numeric",
                  month: "long",
                }).format(new Date(`${day}T12:00:00`))}
              </h2>
              <Card className="px-4 py-1">
                <ul className="divide-y divide-border/60">
                  {list.map((e) => (
                    <AuditRow
                      key={e.id}
                      e={e}
                      patientName={
                        e.patientId ? names?.get(e.patientId) : undefined
                      }
                    />
                  ))}
                </ul>
              </Card>
            </section>
          ))}
        </div>
      )}

      <p className="mt-5 flex gap-2 text-xs text-muted-foreground">
        <ShieldCheck className="size-4 shrink-0 text-primary" aria-hidden />
        {t("audit.serverNote")}
      </p>
    </>
  )
}

function Count({
  label,
  value,
  danger,
}: {
  label: string
  value: number
  danger?: boolean
}) {
  return (
    <div className="flex flex-col-reverse">
      <span className="text-xs leading-tight text-muted-foreground">
        {label}
      </span>
      <span
        className={cn(
          "text-xl font-semibold tabular-nums",
          danger && value ? "text-destructive" : "text-heading"
        )}
      >
        {value}
      </span>
    </div>
  )
}
