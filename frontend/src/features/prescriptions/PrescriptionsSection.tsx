import { useState } from "react"
import { useTranslation } from "react-i18next"
import { Link } from "react-router-dom"
import { useSession } from "@/auth/session"
import { Input } from "@/components/form/Field"
import { EmptyState } from "@/components/PageHeader"
import { Button } from "@/components/ui/button"
import { saveAndQueue } from "@/db/local"
import type { Patient, Prescription } from "@/db/types"
import { fullName } from "@/features/patients/usePatients"
import { formatDateTime } from "@/lib/dates"
import { saved, saveFailed } from "@/lib/feedback"
import { toast } from "@/lib/toast"
import { cn } from "@/lib/utils"
import { shareText, usePrescriptions } from "./prescriptions"

/**
 * A patient's prescriptions. A doctor writes and cancels them; whoever sees the patient get the
 * medicines (the CHV at home, the nurse at the health centre) records which pharmacy gave them
 * out, so the doctor knows the treatment started. "Send to pharmacy" shares the prescription as
 * text (SMS, WhatsApp) with its code, which the pharmacy can check with the health centre.
 */
export function PrescriptionsSection({ patient }: { patient: Patient }) {
  const { t } = useTranslation()
  const session = useSession()
  const list = usePrescriptions(patient.id)
  const isDoctor = session?.user.role === "doctor"
  if (!isDoctor && list?.length === 0) return null
  return (
    <section id="prescriptions" className="scroll-mt-6 space-y-3">
      <div className="flex items-center justify-between gap-2">
        <h2 className="text-base font-semibold text-heading">
          {t("prescriptions.title")}
        </h2>
        {isDoctor ? (
          <Link
            to={`/patients/${patient.id}/prescriptions/new`}
            className="inline-flex min-h-10 items-center rounded-full bg-primary px-4 text-sm font-semibold text-primary-foreground"
          >
            {t("prescriptions.prescribe")}
          </Link>
        ) : null}
      </div>
      {list?.length === 0 ? (
        <EmptyState>{t("prescriptions.empty")}</EmptyState>
      ) : (
        <ul className="space-y-2">
          {list?.map((p) => (
            <PrescriptionCard
              key={p.id}
              p={p}
              patient={patient}
              isDoctor={isDoctor}
            />
          ))}
        </ul>
      )}
    </section>
  )
}

function PrescriptionCard({
  p,
  patient,
  isDoctor,
}: {
  p: Prescription
  patient: Patient
  isDoctor: boolean
}) {
  const { t } = useTranslation()
  const [dispensing, setDispensing] = useState(false)
  const [pharmacy, setPharmacy] = useState("")
  const active = p.status === "active"

  async function move(status: "dispensed" | "cancelled") {
    const now = new Date().toISOString()
    try {
      await saveAndQueue(
        "prescription",
        {
          ...p,
          status,
          pharmacy: status === "dispensed" ? pharmacy.trim() : p.pharmacy,
          dispensedAt: status === "dispensed" ? now : p.dispensedAt,
          updatedAt: now,
        },
        "update"
      )
    } catch (e) {
      return saveFailed(e)
    }
    setDispensing(false)
    saved(
      status === "dispensed"
        ? t("prescriptions.dispensedSaved")
        : t("prescriptions.cancelledSaved")
    )
  }

  async function share() {
    const text = shareText(p, fullName(patient), t)
    try {
      if (navigator.share) await navigator.share({ text })
      else {
        await navigator.clipboard.writeText(text)
        toast.success(t("prescriptions.copied"))
      }
    } catch {
      // the person closed the share sheet
    }
  }

  return (
    <li
      className={cn(
        "rounded-xl bg-card p-4 text-sm shadow-card",
        p.status === "cancelled" && "opacity-70"
      )}
    >
      <div className="flex items-start justify-between gap-2">
        <div>
          <p className="font-mono text-base font-bold tracking-wider text-heading">
            {p.code}
          </p>
          <p className="text-muted-foreground">
            {formatDateTime(p.prescribedAt)}
            {p.prescriberName ? ` · ${p.prescriberName}` : ""}
          </p>
        </div>
        <span
          className={cn(
            "shrink-0 rounded-full px-2.5 py-1 text-xs font-semibold",
            active
              ? "bg-warning/10 text-warning"
              : p.status === "dispensed"
                ? "bg-secondary text-secondary-foreground"
                : "bg-muted text-muted-foreground"
          )}
        >
          {t(`prescriptions.status.${p.status}`)}
        </span>
      </div>
      <ul className="mt-3 space-y-1.5">
        {p.items.map((i, n) => (
          <li key={n} className="rounded-lg bg-secondary/50 px-3 py-2">
            <p className="font-semibold text-foreground">{i.medicine}</p>
            <p className="text-muted-foreground">
              {i.dose} · {i.frequency} ·{" "}
              {t("prescriptions.forDays", { count: i.days })}
            </p>
            {i.instructions ? (
              <p className="text-muted-foreground">{i.instructions}</p>
            ) : null}
          </li>
        ))}
      </ul>
      {p.notes ? <p className="mt-2 text-muted-foreground">{p.notes}</p> : null}
      {p.status === "dispensed" ? (
        <p className="mt-2 text-muted-foreground">
          {t("prescriptions.dispensedAt", {
            pharmacy: p.pharmacy ?? "",
            date: p.dispensedAt ? formatDateTime(p.dispensedAt) : "",
          })}
        </p>
      ) : null}

      {active && dispensing ? (
        <div className="mt-3 space-y-2">
          <label
            htmlFor={`pharmacy-${p.id}`}
            className="block text-sm font-medium text-foreground"
          >
            {t("prescriptions.whichPharmacy")}
          </label>
          <Input
            id={`pharmacy-${p.id}`}
            value={pharmacy}
            onChange={(e) => setPharmacy(e.target.value)}
          />
          <div className="flex gap-2">
            <Button
              type="button"
              size="lg"
              className="flex-1"
              disabled={!pharmacy.trim()}
              onClick={() => void move("dispensed")}
            >
              {t("prescriptions.confirmDispensed")}
            </Button>
            <Button
              type="button"
              size="lg"
              variant="ghost"
              onClick={() => setDispensing(false)}
            >
              {t("common.cancel")}
            </Button>
          </div>
        </div>
      ) : active ? (
        <div className="mt-3 flex flex-wrap gap-2">
          <Button
            type="button"
            size="lg"
            className="flex-1"
            onClick={() => setDispensing(true)}
          >
            {t("prescriptions.markDispensed")}
          </Button>
          <Button
            type="button"
            size="lg"
            variant="secondary"
            className="flex-1"
            onClick={() => void share()}
          >
            {t("prescriptions.share")}
          </Button>
          {isDoctor ? (
            <Button
              type="button"
              size="lg"
              variant="destructive"
              onClick={() => void move("cancelled")}
            >
              {t("prescriptions.cancel")}
            </Button>
          ) : null}
        </div>
      ) : null}
    </li>
  )
}
