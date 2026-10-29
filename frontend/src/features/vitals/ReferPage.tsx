import { useLiveQuery } from "dexie-react-hooks"
import { Hospital, Siren } from "@/components/icons"
import { useTranslation } from "react-i18next"
import { Link, useParams } from "react-router-dom"
import { buttonVariants } from "@/components/ui/button"
import { db } from "@/db/local"
import { fullName, usePatient } from "@/features/patients/usePatients"
import { formatDateTime } from "@/lib/dates"
import { cn } from "@/lib/utils"
import { URGENT_FLAGS, vitalsFlags } from "@/lib/vitals"

/**
 * Shown straight after saving vitals that need a facility today (in the spirit of the Figma
 * "Ante una Emergencia" screen): what was found, and one tap to book the referral with the
 * reason and priority already filled in.
 */
export function Component() {
  const { t } = useTranslation()
  const { id = "" } = useParams()
  const patient = usePatient(id)
  const latest = useLiveQuery(
    async () =>
      (await db.vitals.where("patientId").equals(id).sortBy("takenAt")).at(
        -1
      ) ?? null,
    [id]
  )

  if (latest === undefined || patient === undefined) return null
  const flags = latest ? vitalsFlags(latest) : []
  const urgent = flags.filter((f) => URGENT_FLAGS.includes(f))
  const reasons = flags.map((f) => t(`flags.${f}`))
  const readings = latest
    ? [
        latest.systolic && latest.diastolic
          ? `${t("vitals.bp")} ${latest.systolic}/${latest.diastolic} mmHg`
          : null,
        latest.temperatureC ? `${latest.temperatureC} °C` : null,
        latest.pulse ? `${t("vitals.pulse")} ${latest.pulse}/min` : null,
      ]
        .filter(Boolean)
        .join(" · ")
    : ""
  const referral = new URLSearchParams({
    type: "referral",
    priority: urgent.length ? "urgent" : "routine",
    reason: `${reasons.join(", ")}. ${readings}`,
  })

  return (
    <div className="mx-auto flex min-h-[calc(100svh-6rem)] max-w-md flex-col gap-5">
      <h1 className="pt-4 text-[1.75rem] leading-9 font-bold text-heading">
        {t("refer.title")}
      </h1>
      <p className="text-base text-foreground">
        {t("refer.text", { name: patient ? fullName(patient) : "" })}
      </p>

      <img
        src="/illustrations/refer.svg"
        alt=""
        className="mx-auto h-36 w-auto"
      />
      <div
        role="alert"
        className="space-y-3 rounded-xl border-2 border-destructive bg-destructive-soft p-5 text-destructive"
      >
        <p className="flex items-center gap-3 text-lg font-bold">
          <Siren className="size-7 shrink-0" aria-hidden />
          {reasons.join(", ")}
        </p>
        <p className="text-base font-semibold text-foreground">{readings}</p>
        {latest ? (
          <p className="text-sm text-muted-foreground">
            {formatDateTime(latest.takenAt)}
          </p>
        ) : null}
      </div>

      <ul className="space-y-2 text-sm text-foreground">
        <li className="flex gap-2">
          <Hospital className="size-5 shrink-0 text-primary" aria-hidden />
          {t("refer.tip1")}
        </li>
        <li className="flex gap-2">
          <Hospital className="size-5 shrink-0 text-primary" aria-hidden />
          {t("refer.tip2")}
        </li>
      </ul>

      <div className="mt-auto space-y-3 pt-2">
        <Link
          to={`/patients/${id}/appointments/new?${referral}`}
          replace
          className={cn(buttonVariants({ size: "xl" }), "w-full")}
        >
          {t("refer.book")}
        </Link>
        <Link
          to={`/patients/${id}`}
          replace
          className={cn(
            buttonVariants({ size: "xl", variant: "secondary" }),
            "w-full"
          )}
        >
          {t("refer.later")}
        </Link>
      </div>
    </div>
  )
}
