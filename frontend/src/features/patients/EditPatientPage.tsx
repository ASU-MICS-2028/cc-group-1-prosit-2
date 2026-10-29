import { zodResolver } from "@hookform/resolvers/zod"
import { FormProvider, useForm } from "react-hook-form"
import { useTranslation } from "react-i18next"
import { useNavigate, useParams } from "react-router-dom"
import { EmptyState, PageHeader } from "@/components/PageHeader"
import { Button } from "@/components/ui/button"
import { saveAndQueue } from "@/db/local"
import type { Patient } from "@/db/types"
import { ageFrom, estimatedBirthDate } from "@/lib/vitals"
import { birthIssue, detailsSchema, type DetailsForm } from "./patientForm"
import { PersonFields, PlaceFields } from "./PatientFields"
import { usePatient } from "./usePatients"

const schema = detailsSchema.superRefine((v, ctx) => {
  const issue = birthIssue(v)
  if (issue)
    ctx.addIssue({ code: "custom", path: [issue.path], message: issue.message })
})

/**
 * Fix a patient's details after registration (a misspelt name, a new phone number). Consent
 * isn't asked again: it was recorded once and stays with the record. Queued as an update, so the
 * server's version check catches two health workers editing at once (Liza's conflict policy).
 */
export function Component() {
  const { t } = useTranslation()
  const { id } = useParams()
  const patient = usePatient(id)
  if (patient === undefined) return null
  if (patient === null) return <EmptyState>{t("patients.notFound")}</EmptyState>
  return <EditForm patient={patient} />
}

function EditForm({ patient }: { patient: Patient }) {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const form = useForm<DetailsForm>({
    resolver: zodResolver(schema),
    defaultValues: {
      givenName: patient.givenName,
      familyName: patient.familyName,
      sex: patient.sex,
      knowsBirthDate: patient.birthDateEstimated ? "no" : "yes",
      dateOfBirth: patient.birthDateEstimated ? undefined : patient.dateOfBirth,
      ageYears: patient.birthDateEstimated
        ? String(ageFrom(patient.dateOfBirth))
        : undefined,
      community: patient.community,
      district: patient.district,
      phone: patient.phone ?? "",
      nationalId: patient.nationalId ?? "",
      preferredLanguage: patient.preferredLanguage,
    },
  })
  const back = `/patients/${patient.id}`

  async function onSubmit(v: DetailsForm) {
    const known = v.knowsBirthDate === "yes"
    // keep the stored estimate if the age wasn't changed, so the date doesn't drift each edit
    const sameAge =
      !known &&
      patient.birthDateEstimated &&
      Number(v.ageYears) === ageFrom(patient.dateOfBirth)
    await saveAndQueue(
      "patient",
      {
        ...patient,
        givenName: v.givenName.trim(),
        familyName: v.familyName.trim(),
        sex: v.sex,
        dateOfBirth: known
          ? v.dateOfBirth!
          : sameAge
            ? patient.dateOfBirth
            : estimatedBirthDate(Number(v.ageYears)),
        birthDateEstimated: !known,
        phone: v.phone || undefined,
        nationalId: v.nationalId || undefined,
        community: v.community.trim(),
        district: v.district.trim(),
        preferredLanguage: v.preferredLanguage,
        updatedAt: new Date().toISOString(),
      },
      "update"
    )
    navigate(back, { replace: true })
  }

  return (
    <div className="mx-auto flex min-h-[calc(100svh-5rem)] max-w-lg flex-col">
      <PageHeader title={t("editPatient.title")} back={back} />
      <FormProvider {...form}>
        <form
          noValidate
          onSubmit={form.handleSubmit(onSubmit)}
          className="flex flex-1 flex-col gap-5"
        >
          <PersonFields />
          <h2 className="pt-2 text-base font-semibold text-heading">
            {t("newPatient.placeTitle")}
          </h2>
          <PlaceFields />
          <Button
            type="submit"
            size="xl"
            className="mt-auto w-full"
            disabled={form.formState.isSubmitting}
          >
            {t("editPatient.save")}
          </Button>
        </form>
      </FormProvider>
    </div>
  )
}
