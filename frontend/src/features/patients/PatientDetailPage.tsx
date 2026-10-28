import { useLiveQuery } from "dexie-react-hooks"
import {
  Fingerprint,
  HeartPulse,
  MapPin,
  Mars,
  MessageCircle,
  PenLine,
  Phone,
  ShieldCheck,
  Thermometer,
  TriangleAlert,
  Venus,
  VenusAndMars,
  Weight,
} from "@/components/icons"
import type { ReactNode } from "react"
import { useTranslation } from "react-i18next"
import { Link, useParams } from "react-router-dom"
import { BpTrend } from "@/components/BpTrend"
import { Card, EmptyState, PageHeader } from "@/components/PageHeader"
import { db } from "@/db/local"
import type { Appointment, AppointmentType, Patient, Vitals } from "@/db/types"
import { ReferralStatusChip } from "@/features/referrals/ReferralStatusChip"
import { formatDateTime } from "@/lib/dates"
import { useMediaFor, useObjectUrl } from "@/lib/media"
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
      <PageHeader
        title={fullName(patient)}
        back="/patients"
        action={
          <Link
            to={`/patients/${patient.id}/edit`}
            className="mb-0.5 inline-flex h-10 shrink-0 items-center gap-1.5 rounded-full bg-secondary px-4 text-sm font-semibold text-secondary-foreground"
          >
            <PenLine className="size-4" aria-hidden />
            {t("editPatient.edit")}
          </Link>
        }
      />
      <Summary
        patient={patient}
        readings={vitals?.length ?? 0}
        appointments={appointments ?? []}
      />

      {vitals?.length ? <Latest readings={vitals} /> : null}
      {vitals ? <BpTrend readings={vitals} /> : null}

      <section id="vitals" className="scroll-mt-6 space-y-3">
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

      <section id="visits" className="scroll-mt-6 space-y-3">
        <h2 className="text-base font-semibold text-heading">
          {t("patient.appointments")}
        </h2>
        {appointments?.length === 0 ? (
          <EmptyState>{t("appointments.empty_upcoming")}</EmptyState>
        ) : (
          <ul className="space-y-2">
            {appointments?.map((a) => (
              <li
                key={a.id}
                className="rounded-xl bg-card shadow-card p-4 text-sm"
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
      <ConsentRecord patient={patient} />
    </div>
  )
}

const CONSENT_ICON = {
  spoken: MessageCircle,
  thumbprint: Fingerprint,
  signature: PenLine,
}

/**
 * What the patient agreed to, how, and the proof: play the voice clip or see the signature or
 * thumbprint photo. Lets a nurse or an auditor check consent without leaving the record.
 */
function ConsentRecord({ patient }: { patient: Patient }) {
  const { t } = useTranslation()
  const { consent } = patient
  const media = useMediaFor(patient.id)?.find(
    (m) => m.id === consent.evidence?.mediaId
  )
  const url = useObjectUrl(media?.blob)
  const Icon = CONSENT_ICON[consent.method]
  return (
    <section className="space-y-3">
      <h2 className="text-base font-semibold text-heading">
        {t("patient.consentTitle")}
      </h2>
      <Card className="space-y-3 p-4 text-sm">
        <div className="flex items-start gap-3">
          <span className="grid size-10 shrink-0 place-items-center rounded-full bg-secondary text-secondary-foreground">
            <Icon className="size-5" aria-hidden />
          </span>
          <div className="min-w-0 flex-1">
            <p className="font-semibold text-heading">
              {t(`consent.${consent.method}`)}
            </p>
            <p className="text-muted-foreground">
              {t("patient.consentOn", {
                date: formatDateTime(consent.at),
                version: consent.statementVersion ?? "v1",
              })}
            </p>
            {consent.witness ? (
              <p className="text-muted-foreground">
                {t("patient.witness", { name: consent.witness })}
              </p>
            ) : null}
          </div>
          {media ? (
            <span
              className={cn(
                "shrink-0 rounded-full px-2.5 py-1 text-xs font-semibold",
                media.uploadedAt
                  ? "bg-secondary text-secondary-foreground"
                  : "bg-warning/10 text-warning"
              )}
            >
              {media.uploadedAt
                ? t("patient.fileSent")
                : t("patient.fileWaiting")}
            </span>
          ) : null}
        </div>
        {url && media?.kind === "consent-audio" ? (
          <audio controls src={url} className="w-full" />
        ) : url ? (
          <img
            src={url}
            alt={
              media?.kind === "consent-photo"
                ? t("capture.photoAlt")
                : t("capture.signatureLabel")
            }
            className="max-h-48 w-full rounded-lg bg-secondary/50 object-contain"
          />
        ) : !consent.evidence ? (
          <p className="text-muted-foreground">{t("patient.noEvidence")}</p>
        ) : null}
      </Card>
    </section>
  )
}

const SEX_ICON = { female: Venus, male: Mars, other: VenusAndMars }

/** "0246787657" → "024 678 7657", the way Ghanaian numbers are read out. */
const groupPhone = (phone: string) =>
  phone.replace(/\s/g, "").replace(/^(\d{3})(\d{3})(\d+)$/, "$1 $2 $3")

const REFERRAL_TYPES: AppointmentType[] = ["referral", "teleconsult"]

/**
 * Laid out like an Instagram profile header: the photo on the left with the counts lined up
 * beside it (each jumps to its list), a short "bio" under them, then the actions side by side.
 * The ID number is masked (last 4 only): it's rarely needed during a visit and shouldn't be read
 * off the screen by people nearby.
 */
function Summary({
  patient,
  readings,
  appointments,
}: {
  patient: Patient
  readings: number
  appointments: Appointment[]
}) {
  const { t } = useTranslation()
  const SexIcon = SEX_ICON[patient.sex]
  const referrals = appointments.filter((a) =>
    REFERRAL_TYPES.includes(a.type)
  ).length
  const stats = [
    { count: readings, label: t("patient.vitals"), to: "#vitals" },
    {
      count: appointments.length - referrals,
      label: t("nav.appointments"),
      to: "#visits",
    },
    { count: referrals, label: t("nav.referrals"), to: "#visits" },
  ]
  const action =
    "inline-flex min-h-11 min-w-0 flex-1 items-center justify-center rounded-lg px-3 py-2 text-center text-sm leading-tight font-semibold"
  return (
    <Card className="p-5">
      <div className="flex items-center gap-4">
        {/* the ring echoes Instagram's story ring, in the brand blue */}
        <span className="shrink-0 rounded-full bg-gradient-to-tr from-primary to-[#5b9dff] p-[3px]">
          <span
            aria-hidden
            className="grid size-[76px] place-items-center rounded-full border-[3px] border-card bg-primary text-2xl font-semibold text-primary-foreground"
          >
            {initials(fullName(patient))}
          </span>
        </span>
        <ul className="grid flex-1 grid-cols-3 gap-1 text-center">
          {stats.map((s) => (
            <li key={s.label}>
              <a
                href={s.to}
                className="flex flex-col rounded-lg py-1 hover:bg-secondary"
              >
                <span className="text-xl font-bold text-heading tabular-nums">
                  {s.count}
                </span>
                <span className="text-xs leading-tight text-muted-foreground hyphens-auto [overflow-wrap:anywhere]">
                  {s.label}
                </span>
              </a>
            </li>
          ))}
        </ul>
      </div>

      <div className="mt-4 space-y-1 text-sm">
        <p className="flex items-center gap-1.5 font-semibold text-heading">
          <SexIcon className="size-4 text-primary" aria-hidden />
          {t(`sex.${patient.sex}`)} ·{" "}
          {t("patients.age", { count: ageFrom(patient.dateOfBirth) })}
          {patient.birthDateEstimated ? (
            <span className="font-normal text-muted-foreground">
              ({t("patient.estimated")})
            </span>
          ) : null}
        </p>
        <p className="flex items-center gap-1.5 text-muted-foreground">
          <MapPin className="size-4 shrink-0 text-primary" aria-hidden />
          <span>
            {patient.community}, {patient.district}
          </span>
        </p>
        {patient.phone ? (
          <p className="flex items-center gap-1.5 text-muted-foreground tabular-nums">
            <Phone className="size-4 shrink-0 text-primary" aria-hidden />
            {groupPhone(patient.phone)}
          </p>
        ) : null}
        {patient.nationalId ? (
          <p className="flex items-center gap-1.5 text-muted-foreground">
            <ShieldCheck className="size-4 shrink-0 text-primary" aria-hidden />
            <span className="sr-only">{t("patient.nationalId")}: </span>
            <span className="tabular-nums">
              •••• {patient.nationalId.replace(/\W/g, "").slice(-4)}
            </span>
          </p>
        ) : null}
      </div>

      <div className="mt-4 flex gap-2">
        <Link
          to={`/patients/${patient.id}/vitals/new`}
          className={cn(action, "bg-primary text-primary-foreground")}
        >
          <span>{t("patient.recordVitals")}</span>
        </Link>
        <Link
          to={`/patients/${patient.id}/appointments/new`}
          className={cn(action, "bg-secondary text-secondary-foreground")}
        >
          <span>{t("patient.book")}</span>
        </Link>
        {patient.phone ? (
          <a
            href={`tel:${patient.phone.replace(/\s/g, "")}`}
            aria-label={t("patient.call", { phone: patient.phone })}
            className="grid size-11 shrink-0 place-items-center rounded-lg bg-secondary text-secondary-foreground"
          >
            <Phone className="size-5" aria-hidden />
          </a>
        ) : null}
      </div>
    </Card>
  )
}

const initials = (name: string) =>
  name
    .split(" ")
    .filter(Boolean)
    .slice(0, 2)
    .map((p) => p[0]!.toUpperCase())
    .join("")

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
    <li className="rounded-xl bg-card shadow-card p-4 text-sm">
      <p className="text-muted-foreground">{formatDateTime(v.takenAt)}</p>
      <p className="font-medium text-foreground">{parts.join(" · ")}</p>
      <VitalsPhotos vitalsId={v.id} />
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

/** Wound or skin photos taken with a reading, as small thumbnails. */
function VitalsPhotos({ vitalsId }: { vitalsId: string }) {
  const media = useMediaFor(vitalsId)
  if (!media?.length) return null
  return (
    <div className="mt-2 flex gap-2">
      {media.map((m, i) => (
        <MediaThumb key={m.id} blob={m.blob} n={i + 1} />
      ))}
    </div>
  )
}

function MediaThumb({ blob, n }: { blob: Blob; n: number }) {
  const { t } = useTranslation()
  const url = useObjectUrl(blob)
  return url ? (
    <a href={url} target="_blank" rel="noreferrer">
      <img
        src={url}
        alt={t("capture.photoN", { n })}
        className="size-16 rounded-lg object-cover"
      />
    </a>
  ) : null
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
