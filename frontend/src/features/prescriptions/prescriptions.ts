import { useLiveQuery } from "dexie-react-hooks"
import type { TFunction } from "i18next"
import { db } from "@/db/local"
import type { Prescription } from "@/db/types"

/**
 * The code a pharmacy reads: the first 8 hex digits of the id (the server checks the same rule),
 * so it shows on the phone before the server has the prescription, even offline.
 */
export function codeFor(id: string): string {
  const hex = id.replace(/-/g, "").slice(0, 8).toUpperCase()
  return `RX-${hex.slice(0, 4)}-${hex.slice(4)}`
}

/** Medicines a doctor can pick from (Ghana's essential medicines list, primary care); any other can be typed. */
export const COMMON_MEDICINES = [
  "Artemether-lumefantrine 20/120 mg",
  "Paracetamol 500 mg",
  "Amoxicillin 500 mg",
  "Oral rehydration salts (ORS)",
  "Zinc 20 mg",
  "Ferrous sulphate + folic acid",
  "Amlodipine 5 mg",
  "Nifedipine 20 mg",
  "Hydrochlorothiazide 25 mg",
  "Metformin 500 mg",
  "Metronidazole 400 mg",
  "Cotrimoxazole 960 mg",
  "Ibuprofen 400 mg",
  "Albendazole 400 mg",
] as const

export const FREQUENCIES = [
  "once",
  "daily",
  "twice",
  "three",
  "four",
  "night",
  "needed",
] as const

/** A patient's prescriptions, newest first. */
export function usePrescriptions(patientId: string | undefined) {
  return useLiveQuery(
    async () =>
      patientId
        ? (
            await db.prescriptions
              .where("patientId")
              .equals(patientId)
              .toArray()
          ).sort((a, b) => b.prescribedAt.localeCompare(a.prescribedAt))
        : [],
    [patientId]
  )
}

/** The prescription as text to send to a pharmacy (SMS, WhatsApp) or read over the phone. */
export function shareText(
  p: Prescription,
  patientName: string,
  t: TFunction
): string {
  const lines = p.items.map(
    (i) =>
      `• ${i.medicine}: ${i.dose}, ${i.frequency}, ${t("prescriptions.forDays", { count: i.days })}${i.instructions ? ` (${i.instructions})` : ""}`
  )
  return [
    `${t("prescriptions.shareHeading")} ${p.code}`,
    `${t("prescriptions.patient")}: ${patientName}`,
    ...lines,
    p.prescriberName ? `${t("prescriptions.by")}: ${p.prescriberName}` : "",
  ]
    .filter(Boolean)
    .join("\n")
}
