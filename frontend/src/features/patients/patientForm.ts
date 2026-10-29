import { z } from "zod"

/** Date of birth or age, whichever the patient can give. */
export function birthIssue(v: {
  knowsBirthDate?: string
  dateOfBirth?: string
  ageYears?: string
}) {
  if (v.knowsBirthDate === "yes") {
    const ok = v.dateOfBirth && Date.parse(v.dateOfBirth) <= Date.now()
    return ok ? null : { path: "dateOfBirth", message: "errors.dateOfBirth" }
  }
  const age = Number(v.ageYears)
  const ok = v.ageYears && Number.isInteger(age) && age >= 0 && age <= 120
  return ok ? null : { path: "ageYears", message: "errors.age" }
}

// The birth check is part of each field's own rule (not a refinement on the whole object),
// so it is reported together with the other missing fields instead of after them.
export const personStep = z.object({
  givenName: z.string().trim().min(1, "errors.required"),
  familyName: z.string().trim().min(1, "errors.required"),
  sex: z.enum(["female", "male", "other"], { error: "errors.required" }),
  knowsBirthDate: z.enum(["yes", "no"]),
  dateOfBirth: z.string().optional(),
  ageYears: z.string().optional(),
})

export const placeStep = z.object({
  community: z.string().trim().min(1, "errors.required"),
  district: z.string().trim().min(1, "errors.required"),
  phone: z
    .string()
    .trim()
    .regex(/^(\+?233|0)\d{9}$/, "errors.phone")
    .or(z.literal("")),
  nationalId: z.string().trim(),
  preferredLanguage: z.string(),
})

/** The patient's details without consent: what the edit page changes. */
export const detailsSchema = personStep.and(placeStep)
export type DetailsForm = z.input<typeof detailsSchema>
