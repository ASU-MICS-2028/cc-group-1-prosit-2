import { Menu } from "@base-ui/react/menu"
import { useLiveQuery } from "dexie-react-hooks"
import { useState } from "react"
import { useTranslation } from "react-i18next"
import { Link } from "react-router-dom"
import { Segmented } from "@/components/form/Segmented"
import { Check, Clock, TriangleAlert } from "@/components/icons"
import { EmptyState, PageHeader } from "@/components/PageHeader"
import { db } from "@/db/local"
import type { Appointment, Patient } from "@/db/types"
import { fullName } from "@/features/patients/usePatients"
import { formatDateTime } from "@/lib/dates"
import { cn } from "@/lib/utils"
import { setAppointmentStatus } from "./appointmentActions"

type Tab = "upcoming" | "overdue" | "past"
type Row = Appointment & { patient?: Patient }

/**
 * The health worker's visits: upcoming (soonest first), overdue (booked but the time has
 * passed, so the CHV can follow up or mark them), and past (done, missed or cancelled). Each
 * open visit can be marked done, rescheduled, marked missed or cancelled from its card.
 */
export function Component() {
  const { t } = useTranslation()
  const [tab, setTab] = useState<Tab>("upcoming")
  const data = useLiveQuery(async () => {
    const all = await db.appointments.orderBy("scheduledAt").toArray()
    const patients = await db.patients.bulkGet(all.map((a) => a.patientId))
    const rows: Row[] = all.map((a, i) => ({ ...a, patient: patients[i] }))
    const now = new Date().toISOString()
    const open = rows.filter((a) => a.status === "scheduled")
    return {
      upcoming: open.filter((a) => a.scheduledAt >= now),
      overdue: open.filter((a) => a.scheduledAt < now).reverse(),
      past: rows.filter((a) => a.status !== "scheduled").reverse(),
    }
  }, [])
  const rows = data?.[tab]

  return (
    <>
      <PageHeader title={t("appointments.title")} />
      <div className="mb-5">
        <Segmented
          name="visitTab"
          legend={t("appointments.title")}
          hideLegend
          value={tab}
          onChange={setTab}
          tone={(v) => (v === "overdue" ? "warning" : "primary")}
          options={(["upcoming", "overdue", "past"] as const).map((v) => ({
            value: v,
            label:
              data && v === "overdue" && data.overdue.length
                ? `${t(`appointments.${v}`)} (${data.overdue.length})`
                : t(`appointments.${v}`),
          }))}
        />
      </div>
      {rows?.length === 0 ? (
        <EmptyState illustration="empty-visits">
          {t(`appointments.empty_${tab}`)}
        </EmptyState>
      ) : (
        <ul className="space-y-3">
          {rows?.map((a) => (
            <VisitCard key={a.id} a={a} overdue={tab === "overdue"} />
          ))}
        </ul>
      )}
    </>
  )
}

function VisitCard({ a, overdue }: { a: Row; overdue: boolean }) {
  const { t } = useTranslation()
  const open = a.status === "scheduled"
  return (
    <li className="rounded-xl bg-card p-4 text-sm shadow-card">
      <Link to={`/patients/${a.patientId}`} className="block">
        <p className="flex items-center justify-between gap-2">
          <span className="font-semibold text-heading">
            {a.patient ? fullName(a.patient) : "—"}
          </span>
          {!open ? (
            <span
              className={cn(
                "rounded-full px-2.5 py-0.5 text-xs font-semibold",
                a.status === "completed"
                  ? "bg-secondary text-primary"
                  : "bg-muted text-muted-foreground"
              )}
            >
              {t(`status.${a.status}`)}
            </span>
          ) : a.priority !== "routine" ? (
            <span
              className={cn(
                "rounded-full px-2.5 py-0.5 text-xs font-semibold",
                a.priority === "emergency"
                  ? "bg-destructive-soft text-destructive"
                  : "bg-warning-soft text-warning"
              )}
            >
              {t(`priority.${a.priority}`)}
            </span>
          ) : null}
        </p>
        <p className="text-muted-foreground">
          {t(`appointmentType.${a.type}`)} · {a.facility}
        </p>
        <p
          className={cn(
            "mt-1 flex items-center gap-1.5",
            overdue ? "font-medium text-warning" : "text-muted-foreground"
          )}
        >
          {overdue ? (
            <TriangleAlert className="size-4" aria-hidden />
          ) : (
            <Clock className="size-4" aria-hidden />
          )}
          {formatDateTime(a.scheduledAt)}
          {overdue ? ` · ${t("appointments.overdueTag")}` : null}
        </p>
      </Link>
      {open ? (
        <div className="mt-3 flex gap-2">
          <button
            type="button"
            onClick={() => void setAppointmentStatus(a, "completed")}
            className="inline-flex h-11 flex-1 items-center justify-center gap-2 rounded-lg bg-primary px-3 text-sm font-semibold text-primary-foreground"
          >
            <Check className="size-5" aria-hidden />
            {t("appointments.markDone")}
          </button>
          <Link
            to={`/patients/${a.patientId}/appointments/${a.id}/edit`}
            className="inline-flex h-11 flex-1 items-center justify-center rounded-lg bg-secondary px-3 text-sm font-semibold text-primary"
          >
            {t("appointments.reschedule")}
          </Link>
          <MoreMenu a={a} />
        </div>
      ) : null}
    </li>
  )
}

/** The less common outcomes, behind ⋯ so they aren't tapped by mistake. */
function MoreMenu({ a }: { a: Appointment }) {
  const { t } = useTranslation()
  const item =
    "flex min-h-12 cursor-pointer items-center rounded-lg px-3 text-base text-foreground outline-none select-none data-[highlighted]:bg-secondary"
  return (
    <Menu.Root>
      <Menu.Trigger
        aria-label={t("appointments.more")}
        className="grid size-11 shrink-0 place-items-center rounded-lg bg-secondary text-primary data-[popup-open]:bg-primary data-[popup-open]:text-primary-foreground"
      >
        <svg
          viewBox="0 0 24 24"
          className="size-6"
          fill="currentColor"
          aria-hidden
        >
          <path d="M6 10c-1.1 0-2 .9-2 2s.9 2 2 2 2-.9 2-2-.9-2-2-2zm12 0c-1.1 0-2 .9-2 2s.9 2 2 2 2-.9 2-2-.9-2-2-2zm-6 0c-1.1 0-2 .9-2 2s.9 2 2 2 2-.9 2-2-.9-2-2-2z" />
        </svg>
      </Menu.Trigger>
      <Menu.Portal>
        <Menu.Positioner
          sideOffset={6}
          align="end"
          className="z-50 outline-none"
        >
          <Menu.Popup className="min-w-52 origin-[var(--transform-origin)] rounded-xl bg-card p-2 shadow-card ring-1 ring-black/5 transition-[transform,opacity] duration-150 outline-none data-[ending-style]:scale-95 data-[ending-style]:opacity-0 data-[starting-style]:scale-95 data-[starting-style]:opacity-0">
            <Menu.Item
              className={item}
              onClick={() => void setAppointmentStatus(a, "missed")}
            >
              {t("appointments.markMissed")}
            </Menu.Item>
            <Menu.Item
              className={cn(item, "text-destructive")}
              onClick={() => void setAppointmentStatus(a, "cancelled")}
            >
              {t("appointments.cancel")}
            </Menu.Item>
          </Menu.Popup>
        </Menu.Positioner>
      </Menu.Portal>
    </Menu.Root>
  )
}
