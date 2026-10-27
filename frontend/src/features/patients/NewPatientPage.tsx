import { zodResolver } from "@hookform/resolvers/zod"
import {
  Fingerprint,
  Mars,
  MessageCircle,
  PenLine,
  ShieldCheck,
  Venus,
  VenusAndMars,
} from "@/components/icons"
import { useState } from "react"
import { Controller, useForm, useWatch } from "react-hook-form"
import { useTranslation } from "react-i18next"
import { useNavigate } from "react-router-dom"
import { z } from "zod"
import { ChoiceTiles } from "@/components/form/ChoiceTiles"
import { Dropdown } from "@/components/form/Dropdown"
import { Segmented } from "@/components/form/Segmented"
import { Field, Input } from "@/components/form/Field"
import { describe } from "@/components/form/describe"
import { Stepper } from "@/components/form/Stepper"
import { Card, PageHeader } from "@/components/PageHeader"
import { Button } from "@/components/ui/button"
import { newId, saveAndQueue } from "@/db/local"
import type { Patient } from "@/db/types"
import { LANGUAGES } from "@/i18n"
import { estimatedBirthDate } from "@/lib/vitals"

// Three steps, as in the Figma "Añadir Contacto" forms. Messages are translation keys, so errors
// show in the app's language. Each step is checked on its own before Next.
const consentStep = z.object({
  consentMethod: z.enum(["verbal", "thumbprint", "signature"]),
  consent: z.literal(true, { error: "errors.consent" }),
})

/** Date of birth or age, whichever the patient can give. */
function birthIssue(v: {
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
const personStep = z.object({
  givenName: z.string().trim().min(1, "errors.required"),
  familyName: z.string().trim().min(1, "errors.required"),
  sex: z.enum(["female", "male", "other"], { error: "errors.required" }),
  knowsBirthDate: z.enum(["yes", "no"]),
  dateOfBirth: z.string().optional(),
  ageYears: z.string().optional(),
})

const placeStep = z.object({
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

const STEPS = [consentStep, personStep, placeStep] as const
const schema = consentStep
  .and(personStep)
  .and(placeStep)
  .refine((v) => birthIssue(v) === null, {
    path: ["dateOfBirth"],
    message: "errors.dateOfBirth",
  })

type Form = z.input<typeof schema>

export function Component() {
  const { t, i18n } = useTranslation()
  const navigate = useNavigate()
  const [step, setStep] = useState(0)
  const {
    register,
    handleSubmit,
    control,
    getValues,
    setError,
    clearErrors,
    setFocus,
    formState: { errors, isSubmitting },
  } = useForm<Form>({
    resolver: zodResolver(schema),
    shouldUnregister: false,
    defaultValues: {
      knowsBirthDate: "yes",
      phone: "",
      nationalId: "",
      preferredLanguage: i18n.language,
      consentMethod: "verbal",
    },
  })
  const knowsBirthDate = useWatch({ control, name: "knowsBirthDate" }) === "yes"
  const err = (key: keyof Form) => {
    const message = errors[key]?.message
    return message ? t(message as "errors.required") : undefined
  }

  function next() {
    clearErrors()
    const values = getValues()
    const result = STEPS[step].safeParse(values)
    const issues = (result.success ? [] : result.error.issues).map((i) => ({
      path: String(i.path[0]),
      message: i.message,
    }))
    const birth = step === 1 ? birthIssue(values) : null
    if (birth) issues.push(birth)
    if (issues.length === 0) {
      setStep(step + 1)
      window.scrollTo(0, 0)
      return
    }
    for (const issue of issues) {
      setError(issue.path as keyof Form, { message: issue.message })
    }
    setFocus(issues[0].path as keyof Form)
  }

  async function onSubmit(v: Form) {
    const now = new Date().toISOString()
    const patient: Patient = {
      id: newId(),
      givenName: v.givenName.trim(),
      familyName: v.familyName.trim(),
      sex: v.sex,
      dateOfBirth: knowsBirthDate
        ? v.dateOfBirth!
        : estimatedBirthDate(Number(v.ageYears)),
      birthDateEstimated: !knowsBirthDate,
      phone: v.phone || undefined,
      nationalId: v.nationalId || undefined,
      community: v.community.trim(),
      district: v.district.trim(),
      preferredLanguage: v.preferredLanguage,
      consent: { given: true, method: v.consentMethod, at: now },
      createdAt: now,
      updatedAt: now,
      version: 0,
    }
    await saveAndQueue("patient", patient)
    navigate(`/patients/${patient.id}/registered`, { replace: true })
  }

  const titles = [
    t("newPatient.consentTitle"),
    t("newPatient.personTitle"),
    t("newPatient.placeTitle"),
  ]
  const subtitles = [
    t("newPatient.consentText"),
    t("newPatient.personText"),
    t("newPatient.placeText"),
  ]
  const last = step === STEPS.length - 1

  return (
    <div className="mx-auto flex min-h-[calc(100svh-5rem)] flex-col max-w-lg">
      <PageHeader
        title={titles[step]}
        subtitle={subtitles[step]}
        back={step === 0 ? "/patients" : () => setStep(step - 1)}
      >
        <Stepper step={step} total={STEPS.length} />
      </PageHeader>

      <form
        noValidate
        onSubmit={
          last
            ? handleSubmit(onSubmit)
            : (e) => {
                e.preventDefault()
                next()
              }
        }
        className="flex flex-1 flex-col gap-5"
      >
        {step === 0 ? (
          <>
            <img
              src="/illustrations/consent.svg"
              alt=""
              className="mx-auto h-36 w-auto"
            />
            <Card className="flex gap-3 p-4">
              <ShieldCheck
                className="size-6 shrink-0 text-primary"
                aria-hidden
              />
              <p className="text-sm text-foreground">
                {t("newPatient.consentPromise")}
              </p>
            </Card>
            <Controller
              control={control}
              name="consentMethod"
              render={({ field }) => (
                <ChoiceTiles
                  name="consentMethod"
                  legend={t("newPatient.consentMethod")}
                  value={field.value}
                  onChange={field.onChange}
                  choices={[
                    {
                      value: "verbal",
                      label: t("consent.verbal"),
                      icon: MessageCircle,
                    },
                    {
                      value: "thumbprint",
                      label: t("consent.thumbprint"),
                      icon: Fingerprint,
                    },
                    {
                      value: "signature",
                      label: t("consent.signature"),
                      icon: PenLine,
                    },
                  ]}
                />
              )}
            />
            <label className="flex min-h-11 items-start gap-3 text-sm text-foreground">
              <input
                type="checkbox"
                className="mt-0.5 size-5 shrink-0 accent-primary"
                {...register("consent")}
                aria-invalid={errors.consent ? true : undefined}
              />
              {t("newPatient.consentGiven")}
            </label>
            {errors.consent ? (
              <p role="alert" className="text-xs font-medium text-destructive">
                {err("consent")}
              </p>
            ) : null}
          </>
        ) : null}

        {step === 1 ? (
          <>
            <Field
              id="givenName"
              label={t("patient.givenName")}
              error={err("givenName")}
            >
              <Input
                autoComplete="off"
                {...register("givenName")}
                {...describe("givenName", err("givenName"))}
              />
            </Field>
            <Field
              id="familyName"
              label={t("patient.familyName")}
              error={err("familyName")}
            >
              <Input
                autoComplete="off"
                {...register("familyName")}
                {...describe("familyName", err("familyName"))}
              />
            </Field>
            <Controller
              control={control}
              name="sex"
              render={({ field }) => (
                <ChoiceTiles
                  name="sex"
                  legend={t("patient.sex")}
                  value={field.value}
                  onChange={field.onChange}
                  error={err("sex")}
                  choices={[
                    { value: "female", label: t("sex.female"), icon: Venus },
                    { value: "male", label: t("sex.male"), icon: Mars },
                    {
                      value: "other",
                      label: t("sex.other"),
                      icon: VenusAndMars,
                    },
                  ]}
                />
              )}
            />
            <Controller
              control={control}
              name="knowsBirthDate"
              render={({ field }) => (
                <Segmented
                  name="knowsBirthDate"
                  legend={t("newPatient.knowsBirthDate")}
                  value={field.value}
                  onChange={field.onChange}
                  options={[
                    { value: "yes", label: t("newPatient.birthDateYes") },
                    { value: "no", label: t("newPatient.birthDateNo") },
                  ]}
                />
              )}
            />
            {knowsBirthDate ? (
              <Field
                id="dateOfBirth"
                label={t("patient.dateOfBirth")}
                error={err("dateOfBirth")}
              >
                <Input
                  type="date"
                  {...register("dateOfBirth")}
                  {...describe("dateOfBirth", err("dateOfBirth"))}
                />
              </Field>
            ) : (
              <Field
                id="ageYears"
                label={t("patient.ageYears")}
                error={err("ageYears")}
              >
                <Input
                  inputMode="numeric"
                  {...register("ageYears")}
                  {...describe("ageYears", err("ageYears"))}
                />
              </Field>
            )}
          </>
        ) : null}

        {step === 2 ? (
          <>
            <Field
              id="community"
              label={t("patient.community")}
              error={err("community")}
            >
              <Input
                {...register("community")}
                {...describe("community", err("community"))}
              />
            </Field>
            <Field
              id="district"
              label={t("patient.district")}
              error={err("district")}
            >
              <Input
                {...register("district")}
                {...describe("district", err("district"))}
              />
            </Field>
            <Field
              id="phone"
              label={t("patient.phone")}
              hint={t("newPatient.phoneWhy")}
              error={err("phone")}
            >
              <Input
                type="tel"
                inputMode="tel"
                {...register("phone")}
                {...describe("phone", err("phone"))}
              />
            </Field>
            <Field
              id="nationalId"
              label={t("patient.nationalId")}
              hint={t("newPatient.nationalIdWhy")}
            >
              <Input {...register("nationalId")} {...describe("nationalId")} />
            </Field>
            <Field
              id="preferredLanguage"
              label={t("patient.preferredLanguage")}
            >
              <Controller
                control={control}
                name="preferredLanguage"
                render={({ field }) => (
                  <Dropdown
                    id="preferredLanguage"
                    value={field.value}
                    onChange={field.onChange}
                    options={LANGUAGES.map((l) => ({
                      value: l.code,
                      label: l.label,
                      hint: l.sub,
                    }))}
                  />
                )}
              />
            </Field>
          </>
        ) : null}

        <Button
          type="submit"
          size="xl"
          className="mt-auto w-full"
          disabled={isSubmitting}
        >
          {last ? t("newPatient.save") : t("common.next")}
        </Button>
      </form>
    </div>
  )
}
