import { useLiveQuery } from "dexie-react-hooks"
import { useState } from "react"
import { useTranslation } from "react-i18next"
import { useNavigate, useParams } from "react-router-dom"
import { Plus, X } from "@/components/icons"
import { Field, Input, Select, Textarea } from "@/components/form/Field"
import { PageHeader } from "@/components/PageHeader"
import { Button } from "@/components/ui/button"
import { useSession } from "@/auth/session"
import { db, newId, saveAndQueue } from "@/db/local"
import type { Prescription, PrescriptionItem } from "@/db/types"
import { fullName, usePatient } from "@/features/patients/usePatients"
import { formatDateTime } from "@/lib/dates"
import { saved, saveFailed } from "@/lib/feedback"
import { cn } from "@/lib/utils"
import { codeFor, COMMON_MEDICINES, FREQUENCIES } from "./prescriptions"

const MAX_ITEMS = 10
const blank = (): PrescriptionItem => ({
  medicine: "",
  dose: "",
  frequency: "",
  days: 5,
})

/**
 * A doctor writes a prescription, usually during or after a referral or a teleconsult. It gets a
 * code (RX-XXXX-XXXX) at once, so the CHV or the patient can give it to any pharmacy, and it is
 * on the CHV's phone after the next sync. Medicines never change after saving: a wrong one is
 * cancelled and a new one written.
 */
export function Component() {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const session = useSession()
  const { id = "" } = useParams()
  const patient = usePatient(id)
  // The referrals and teleconsults it can belong to, newest first.
  const visits = useLiveQuery(
    async () =>
      (await db.appointments.where("patientId").equals(id).toArray())
        .filter((a) => a.type === "referral" || a.type === "teleconsult")
        .sort((a, b) => b.scheduledAt.localeCompare(a.scheduledAt)),
    [id]
  )
  const [items, setItems] = useState<PrescriptionItem[]>([blank()])
  const [appointmentId, setAppointmentId] = useState("")
  const [notes, setNotes] = useState("")
  const [error, setError] = useState<string>()
  const [busy, setBusy] = useState(false)

  const change = (i: number, patch: Partial<PrescriptionItem>) => {
    setError(undefined)
    setItems(items.map((item, j) => (j === i ? { ...item, ...patch } : item)))
  }

  async function save() {
    const filled = items.filter((i) => i.medicine.trim())
    if (filled.length === 0) return setError(t("prescriptions.needOne"))
    if (
      filled.some(
        (i) =>
          !i.dose.trim() ||
          !i.frequency.trim() ||
          !(i.days >= 1 && i.days <= 365)
      )
    )
      return setError(t("prescriptions.needDetails"))
    setBusy(true)
    const now = new Date().toISOString()
    const rxId = newId()
    const prescription: Prescription = {
      id: rxId,
      patientId: id,
      appointmentId: appointmentId || undefined,
      code: codeFor(rxId),
      items: filled.map((i) => ({
        medicine: i.medicine.trim(),
        dose: i.dose.trim(),
        frequency: i.frequency.trim(),
        days: i.days,
        instructions: i.instructions?.trim() || undefined,
      })),
      notes: notes.trim() || undefined,
      status: "active",
      prescribedBy: session?.user.id,
      prescriberName: session?.user.name,
      prescribedAt: now,
      createdAt: now,
      updatedAt: now,
      version: 0,
    }
    try {
      await saveAndQueue("prescription", prescription)
    } catch (e) {
      setBusy(false)
      return saveFailed(e)
    }
    saved(t("prescriptions.saved", { code: prescription.code }))
    navigate(`/patients/${id}#prescriptions`, { replace: true })
  }

  return (
    <div className="mx-auto flex min-h-[calc(100svh-5rem)] max-w-lg flex-col">
      <PageHeader
        title={t("prescriptions.newTitle")}
        back={`/patients/${id}`}
      />
      {patient ? (
        <p className="-mt-3 mb-5 text-sm text-muted-foreground">
          {fullName(patient)}
        </p>
      ) : null}
      <div className="flex flex-1 flex-col gap-5">
        <datalist id="medicines">
          {COMMON_MEDICINES.map((m) => (
            <option key={m} value={m} />
          ))}
        </datalist>
        {items.map((item, i) => (
          <fieldset
            key={i}
            className="space-y-4 rounded-xl bg-card p-4 shadow-card"
          >
            <div className="flex items-center justify-between">
              <legend className="font-semibold text-heading">
                {t("prescriptions.medicineN", { n: i + 1 })}
              </legend>
              {items.length > 1 ? (
                <button
                  type="button"
                  onClick={() => setItems(items.filter((_, j) => j !== i))}
                  className="flex h-11 items-center gap-1 px-2 text-sm font-semibold text-primary"
                >
                  <X className="size-4" aria-hidden />
                  {t("prescriptions.remove")}
                </button>
              ) : null}
            </div>
            <Field id={`medicine-${i}`} label={t("prescriptions.medicine")}>
              <Input
                id={`medicine-${i}`}
                list="medicines"
                autoComplete="off"
                value={item.medicine}
                onChange={(e) => change(i, { medicine: e.target.value })}
              />
            </Field>
            <div className="grid grid-cols-2 gap-4">
              <Field id={`dose-${i}`} label={t("prescriptions.dose")}>
                <Input
                  id={`dose-${i}`}
                  placeholder={t("prescriptions.dosePlaceholder")}
                  value={item.dose}
                  onChange={(e) => change(i, { dose: e.target.value })}
                />
              </Field>
              <Field id={`days-${i}`} label={t("prescriptions.days")}>
                <Input
                  id={`days-${i}`}
                  type="number"
                  inputMode="numeric"
                  min={1}
                  max={365}
                  value={String(item.days)}
                  onChange={(e) => change(i, { days: Number(e.target.value) })}
                />
              </Field>
            </div>
            <fieldset>
              <legend className="mb-1.5 text-sm font-medium tracking-[0.01em] text-foreground">
                {t("prescriptions.frequency")}
              </legend>
              <div className="flex flex-wrap gap-2">
                {FREQUENCIES.map((f) => {
                  const label = t(`prescriptions.freq.${f}`)
                  return (
                    <button
                      key={f}
                      type="button"
                      aria-pressed={item.frequency === label}
                      onClick={() => change(i, { frequency: label })}
                      className={cn(
                        "min-h-9 rounded-full border px-3 text-xs font-semibold",
                        item.frequency === label
                          ? "border-primary bg-primary text-primary-foreground"
                          : "border-border bg-card text-foreground"
                      )}
                    >
                      {label}
                    </button>
                  )
                })}
              </div>
            </fieldset>
            <Field
              id={`instructions-${i}`}
              label={t("prescriptions.instructions")}
              hint={t("common.optional")}
            >
              <Input
                id={`instructions-${i}`}
                placeholder={t("prescriptions.instructionsPlaceholder")}
                value={item.instructions ?? ""}
                onChange={(e) => change(i, { instructions: e.target.value })}
              />
            </Field>
          </fieldset>
        ))}
        {items.length < MAX_ITEMS ? (
          <Button
            type="button"
            size="xl"
            variant="secondary"
            className="w-full"
            onClick={() => setItems([...items, blank()])}
          >
            <Plus aria-hidden />
            {t("prescriptions.addMedicine")}
          </Button>
        ) : null}

        {visits?.length ? (
          <Field
            id="visit"
            label={t("prescriptions.forVisit")}
            hint={t("common.optional")}
          >
            <Select
              id="visit"
              value={appointmentId}
              onChange={(e) => setAppointmentId(e.target.value)}
            >
              <option value="">{t("prescriptions.noVisit")}</option>
              {visits.map((a) => (
                <option key={a.id} value={a.id}>
                  {t(`appointmentType.${a.type}`)} ·{" "}
                  {formatDateTime(a.scheduledAt)}
                </option>
              ))}
            </Select>
          </Field>
        ) : null}

        <Field
          id="rx-notes"
          label={t("vitals.notes")}
          hint={t("common.optional")}
        >
          <Textarea
            id="rx-notes"
            value={notes}
            onChange={(e) => setNotes(e.target.value)}
          />
        </Field>

        {error ? (
          <p role="alert" className="text-sm font-medium text-destructive">
            {error}
          </p>
        ) : null}
        <Button
          type="button"
          size="xl"
          className="mt-auto w-full"
          disabled={busy}
          onClick={() => void save()}
        >
          {t("prescriptions.save")}
        </Button>
      </div>
    </div>
  )
}
