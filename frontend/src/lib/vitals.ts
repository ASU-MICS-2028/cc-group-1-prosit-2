import type { Vitals } from "@/db/types"

export type Flag =
  | "bpCrisis"
  | "bpHigh"
  | "bpLow"
  | "fever"
  | "highFever"
  | "lowTemp"
  | "pulseHigh"
  | "pulseLow"

/** Flags that should send the patient to a facility today, not wait for the next visit. */
export const URGENT_FLAGS: readonly Flag[] = [
  "bpCrisis",
  "highFever",
  "lowTemp",
]

/**
 * Prompts for the CHV from common adult thresholds (WHO/ISH blood pressure bands, fever at 38 °C).
 * They are reminders to refer, not a diagnosis; clinicians set the real protocol.
 */
export function vitalsFlags(v: Partial<Vitals>): Flag[] {
  const flags: Flag[] = []
  const { systolic: sys, diastolic: dia, temperatureC: t, pulse } = v
  if ((sys ?? 0) >= 180 || (dia ?? 0) >= 120) flags.push("bpCrisis")
  else if ((sys ?? 0) >= 140 || (dia ?? 0) >= 90) flags.push("bpHigh")
  else if ((sys !== undefined && sys < 90) || (dia !== undefined && dia < 60))
    flags.push("bpLow")
  if (t !== undefined) {
    if (t >= 39.5) flags.push("highFever")
    else if (t >= 38) flags.push("fever")
    else if (t < 35) flags.push("lowTemp")
  }
  if (pulse !== undefined) {
    if (pulse > 100) flags.push("pulseHigh")
    else if (pulse < 50) flags.push("pulseLow")
  }
  return flags
}

/** Body mass index to one decimal, when both weight and height are known. */
export function bmi(weightKg?: number, heightCm?: number): number | undefined {
  if (!weightKg || !heightCm) return undefined
  const m = heightCm / 100
  return Math.round((weightKg / (m * m)) * 10) / 10
}

/** Whole years between a YYYY-MM-DD birth date and today. */
export function ageFrom(dateOfBirth: string, today = new Date()): number {
  const dob = new Date(dateOfBirth)
  let age = today.getFullYear() - dob.getFullYear()
  const beforeBirthday =
    today.getMonth() < dob.getMonth() ||
    (today.getMonth() === dob.getMonth() && today.getDate() < dob.getDate())
  if (beforeBirthday) age--
  return age
}

/** Birth date estimated from an age in years (1 July of that year), for patients who don't know it. */
export function estimatedBirthDate(ageYears: number, today = new Date()) {
  return `${today.getFullYear() - ageYears}-07-01`
}
