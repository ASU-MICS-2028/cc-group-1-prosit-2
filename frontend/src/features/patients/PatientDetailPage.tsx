import { useLiveQuery } from "dexie-react-hooks"
import { CalendarPlus, HeartPulse, TriangleAlert } from "lucide-react"
import { useTranslation } from "react-i18next"
import { Link, useParams } from "react-router-dom"
import { EmptyState, PageHeader } from "@/components/PageHeader"
import { buttonVariants } from "@/components/ui/button"
import { db } from "@/db/local"
import type { Vitals } from "@/db/types"
import { formatDateTime } from "@/lib/dates"
import { cn } from "@/lib/utils"
import { ageFrom, bmi, URGENT_FLAGS, vitalsFlags } from "@/lib/vitals"
import { fullName, usePatient } from "./usePatients"

export function Component() {
  const { t } = useTranslation()
  const { id } = useParams()
  const patient = usePatient(id)
  const vitals = useLiveQuery(
    () =>
      db.vitals
        .where("patientId")
        .equals(id ?? "")
        .reverse()
        .sortBy("takenAt"),
    [id]
  )
  const appointments = useLiveQuery(
    () =>
      db.appointments
        .where("patientId")
        .equals(id ?? "")
        .sortBy("scheduledAt"),
    [id]
  )

  if (patient === undefined) return null
  if (patient === null) return <EmptyState>{t("patients.notFound")}</EmptyState>

  return (
    <div className="space-y-6">
      <PageHeader title={fullName(patient)} back="/patients" />
      <dl className="grid grid-cols-2 gap-3 rounded-xl border border-border bg-card p-4 text-sm">
        <Item label={t("patient.sex")} value={t(`sex.${patient.sex}`)} />
        <Item
          label={t("patient.age")}
          value={
            t("patients.age", { count: ageFrom(patient.dateOfBirth) }) +
            (patient.birthDateEstimated ? ` (${t("patient.estimated")})` : "")
          }
        />
        <Item label={t("patient.community")} value={patient.community} />
        <Item label={t("patient.district")} value={patient.district} />
        {patient.phone ? (
          <Item label={t("patient.phone")} value={patient.phone} />
        ) : null}
        {patient.nationalId ? (
          <Item label={t("patient.nationalId")} value={patient.nationalId} />
        ) : null}
      </dl>

      <div className="grid gap-3 sm:grid-cols-2">
        <Link
          to={`/patients/${patient.id}/vitals/new`}
          className={cn(buttonVariants({ size: "xl" }), "w-full")}
        >
          <HeartPulse aria-hidden />
          {t("patient.recordVitals")}
        </Link>
        <Link
          to={`/patients/${patient.id}/appointments/new`}
          className={cn(
            buttonVariants({ size: "xl", variant: "secondary" }),
            "w-full"
          )}
        >
          <CalendarPlus aria-hidden />
          {t("patient.book")}
        </Link>
      </div>

      <section className="space-y-3">
        <h2 className="text-base font-semibold text-heading">
          {t("patient.vitals")}
        </h2>
        {vitals?.length === 0 ? (
          <EmptyState>{t("patient.noVitals")}</EmptyState>
        ) : (
          <ul className="space-y-2">
            {vitals?.map((v) => (
              <VitalsRow key={v.id} v={v} />
            ))}
          </ul>
        )}
      </section>

      <section className="space-y-3">
        <h2 className="text-base font-semibold text-heading">
          {t("patient.appointments")}
        </h2>
        {appointments?.length === 0 ? (
          <EmptyState>{t("appointments.empty")}</EmptyState>
        ) : (
          <ul className="space-y-2">
            {appointments?.map((a) => (
              <li
                key={a.id}
                className="rounded-xl border border-border bg-card p-4 text-sm"
              >
                <p className="font-medium text-foreground">
                  {t(`appointmentType.${a.type}`)} · {a.facility}
                </p>
                <p className="text-muted-foreground">
                  {formatDateTime(a.scheduledAt)} · {t(`status.${a.status}`)}
                </p>
              </li>
            ))}
          </ul>
        )}
      </section>
    </div>
  )
}

function Item({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <dt className="text-muted-foreground">{label}</dt>
      <dd className="font-medium text-foreground">{value}</dd>
    </div>
  )
}

function VitalsRow({ v }: { v: Vitals }) {
  const { t } = useTranslation()
  const flags = vitalsFlags(v)
  const urgent = flags.some((f) => URGENT_FLAGS.includes(f))
  const index = bmi(v.weightKg, v.heightCm)
  const parts = [
    v.systolic && v.diastolic
      ? `${t("vitals.bp")} ${v.systolic}/${v.diastolic}`
      : null,
    v.temperatureC ? `${v.temperatureC} °C` : null,
    v.pulse ? `${t("vitals.pulse")} ${v.pulse}` : null,
    v.weightKg ? `${v.weightKg} kg` : null,
    index ? `${t("vitals.bmi")} ${index}` : null,
  ].filter(Boolean)
  return (
    <li className="rounded-xl border border-border bg-card p-4 text-sm">
      <p className="text-muted-foreground">{formatDateTime(v.takenAt)}</p>
      <p className="font-medium text-foreground">{parts.join(" · ")}</p>
      {flags.length > 0 ? (
        <p
          className={cn(
            "mt-2 flex items-center gap-1.5 font-medium",
            urgent ? "text-destructive" : "text-warning"
          )}
        >
          <TriangleAlert className="size-4" aria-hidden />
          {flags.map((f) => t(`flags.${f}`)).join(", ")}
        </p>
      ) : null}
    </li>
  )
}
