import { useLiveQuery } from "dexie-react-hooks"
import { useTranslation } from "react-i18next"
import { Link } from "react-router-dom"
import { EmptyState, PageHeader } from "@/components/PageHeader"
import { db } from "@/db/local"
import { formatDateTime } from "@/lib/dates"
import { cn } from "@/lib/utils"
import { fullName } from "@/features/patients/usePatients"

/** Upcoming appointments and referrals on this phone, soonest first. */
export function Component() {
  const { t } = useTranslation()
  const rows = useLiveQuery(async () => {
    const list = await db.appointments
      .where("scheduledAt")
      .aboveOrEqual(new Date().toISOString())
      .filter((a) => a.status === "scheduled")
      .sortBy("scheduledAt")
    const patients = await db.patients.bulkGet(list.map((a) => a.patientId))
    return list.map((a, i) => ({ ...a, patient: patients[i] }))
  }, [])

  return (
    <>
      <PageHeader title={t("appointments.title")} />
      {rows?.length === 0 ? (
        <EmptyState illustration="empty-visits">
          {t("appointments.empty")}
        </EmptyState>
      ) : (
        <ul className="space-y-2">
          {rows?.map((a) => (
            <li key={a.id}>
              <Link
                to={`/patients/${a.patientId}`}
                className="block rounded-xl bg-card shadow-card p-4 text-sm"
              >
                <p className="flex items-center justify-between gap-2">
                  <span className="font-medium text-foreground">
                    {a.patient ? fullName(a.patient) : "—"}
                  </span>
                  {a.priority !== "routine" ? (
                    <span
                      className={cn(
                        "rounded-full px-2 py-0.5 text-xs font-medium",
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
                <p className="text-muted-foreground">
                  {formatDateTime(a.scheduledAt)}
                </p>
              </Link>
            </li>
          ))}
        </ul>
      )}
    </>
  )
}
