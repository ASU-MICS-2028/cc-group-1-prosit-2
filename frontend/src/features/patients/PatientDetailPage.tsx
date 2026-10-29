import { useLiveQuery } from "dexie-react-hooks"
import {
  CalendarPlus,
  HeartPulse,
  Thermometer,
  TriangleAlert,
  Weight,
} from "lucide-react"
import type { ReactNode } from "react"
import { useTranslation } from "react-i18next"
import { Link, useParams } from "react-router-dom"
import { BpTrend } from "@/components/BpTrend"
import { Card, EmptyState, PageHeader } from "@/components/PageHeader"
import { buttonVariants } from "@/components/ui/button"
import { db } from "@/db/local"
import type { Vitals } from "@/db/types"
import { ReferralStatusChip } from "@/features/referrals/ReferralStatusChip"
import { formatDateTime } from "@/lib/dates"
import { cn } from "@/lib/utils"
import {
  ageFrom,
  bmi,
  type Flag,
  URGENT_FLAGS,
  vitalsFlags,
} from "@/lib/vitals"
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

      {vitals?.length ? <Latest readings={vitals} /> : null}
      {vitals ? <BpTrend readings={vitals} /> : null}

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
                <p className="flex items-start justify-between gap-2 font-medium text-foreground">
                  <span>
                    {t(`appointmentType.${a.type}`)} · {a.facility}
                  </span>
                  {a.referralStatus ? (
                    <ReferralStatusChip status={a.referralStatus} />
                  ) : null}
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

/**
 * The latest value of each measure, as number cards (Figma "Metrics"): the value in text ink,
 * and a flag with an icon and words when it needs attention, never colour alone.
 */
function Latest({ readings }: { readings: Vitals[] }) {
  const { t } = useTranslation()
  const latest = <K extends keyof Vitals>(key: K) =>
    readings.find((r) => r[key] != null)
  const bp = latest("systolic")
  const temp = latest("temperatureC")
  const weight = latest("weightKg")
  const flagged = (v: Vitals | undefined, own: Flag[]) =>
    v ? vitalsFlags(v).filter((f) => own.includes(f)) : []
  return (
    <section className="space-y-3">
      <h2 className="text-base font-semibold text-heading">
        {t("patient.latest")}
      </h2>
      <div className="grid grid-cols-2 gap-3 sm:grid-cols-3">
        <Metric
          icon={<HeartPulse />}
          label={t("vitals.bp")}
          value={bp ? `${bp.systolic}/${bp.diastolic}` : "–"}
          unit="mmHg"
          flags={flagged(bp, ["bpCrisis", "bpHigh", "bpLow"])}
        />
        <Metric
          icon={<Thermometer />}
          label={t("vitals.temperature")}
          value={temp ? String(temp.temperatureC) : "–"}
          unit="°C"
          flags={flagged(temp, ["highFever", "fever", "lowTemp"])}
        />
        <Metric
          icon={<Weight />}
          label={t("vitals.weight")}
          value={weight ? String(weight.weightKg) : "–"}
          unit="kg"
          flags={[]}
        />
      </div>
    </section>
  )
}

function Metric({
  icon,
  label,
  value,
  unit,
  flags,
}: {
  icon: ReactNode
  label: string
  value: string
  unit: string
  flags: Flag[]
}) {
  const { t } = useTranslation()
  const urgent = flags.some((f) => URGENT_FLAGS.includes(f))
  return (
    <Card className="space-y-1 p-4 [&>svg]:size-5 [&>svg]:text-primary">
      {icon}
      <p className="text-xs font-medium text-muted-foreground">{label}</p>
      <p className="text-2xl leading-8 font-semibold text-foreground tabular-nums">
        {value}{" "}
        <span className="text-xs font-normal text-muted-foreground">
          {unit}
        </span>
      </p>
      {flags.length ? (
        <p
          className={cn(
            "flex items-center gap-1 text-xs font-semibold",
            urgent ? "text-destructive" : "text-warning"
          )}
        >
          <TriangleAlert className="size-3.5 shrink-0" aria-hidden />
          {t(`flags.${flags[0]}`)}
        </p>
      ) : null}
    </Card>
  )
}
