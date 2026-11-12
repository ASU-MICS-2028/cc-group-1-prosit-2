import { useLiveQuery } from "dexie-react-hooks"
import { db } from "@/db/local"
import type { TestCategory, TestResult } from "@/db/types"

/**
 * Tests to pick from, by where they are done. A CHV can do the quick ones on the spot; the others
 * need a laboratory or an X-ray machine, which most villages don't have, so the patient is sent.
 * Any other test can be typed in.
 */
export const COMMON_TESTS: Record<TestCategory, readonly string[]> = {
  rapid: [
    "Malaria RDT",
    "Blood sugar (finger prick)",
    "Haemoglobin (Hb)",
    "Pregnancy test (urine)",
    "Urine dipstick (protein)",
    "HIV rapid test",
    "Syphilis rapid test",
    "Hepatitis B rapid test",
  ],
  lab: [
    "Full blood count",
    "Malaria microscopy",
    "Fasting blood sugar",
    "Urine routine examination",
    "Stool routine examination",
    "Sickling test",
    "Kidney function (creatinine)",
    "Liver function tests",
    "TB sputum (GeneXpert)",
    "Blood group",
  ],
  imaging: [
    "Chest X-ray",
    "Obstetric ultrasound",
    "Abdominal ultrasound",
    "Limb X-ray",
  ],
}

/** A patient's tests, newest request first. */
export function useResults(patientId: string | undefined) {
  return useLiveQuery(
    async () =>
      patientId
        ? (
            await db.results.where("patientId").equals(patientId).toArray()
          ).sort((a, b) => b.requestedAt.localeCompare(a.requestedAt))
        : [],
    [patientId]
  )
}

/** Tests sent elsewhere whose answer has not come back yet. */
export const isWaiting = (r: TestResult) => r.status === "requested"
