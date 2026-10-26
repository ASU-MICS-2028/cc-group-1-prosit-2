import { zodResolver } from "@hookform/resolvers/zod"
import { useForm, useWatch } from "react-hook-form"
import { useTranslation } from "react-i18next"
import { useNavigate } from "react-router-dom"
import { z } from "zod"
import { Field, Input, Select } from "@/components/form/Field"
import { describe } from "@/components/form/describe"
import { PageHeader } from "@/components/PageHeader"
import { Button } from "@/components/ui/button"
import { newId, saveAndQueue } from "@/db/local"
import type { Patient } from "@/db/types"
import { LANGUAGES } from "@/i18n"
import { estimatedBirthDate } from "@/lib/vitals"

// Messages are translation keys, so errors show in the app's language.
const schema = z
  .object({
    givenName: z.string().trim().min(1, "errors.required"),
    familyName: z.string().trim().min(1, "errors.required"),
    sex: z.enum(["female", "male"], { error: "errors.required" }),
    knowsBirthDate: z.enum(["yes", "no"]),
    dateOfBirth: z.string().optional(),
    ageYears: z.string().optional(),
    phone: z
      .string()
      .trim()
      .regex(/^(\+?233|0)\d{9}$/, "errors.phone")
      .or(z.literal("")),
    nationalId: z.string().trim(),
    community: z.string().trim().min(1, "errors.required"),
    district: z.string().trim().min(1, "errors.required"),
    preferredLanguage: z.string(),
    consentMethod: z.enum(["verbal", "thumbprint", "signature"]),
    consent: z.literal(true, { error: "errors.consent" }),
  })
  .superRefine((v, ctx) => {
    if (v.knowsBirthDate === "yes") {
      const ok = v.dateOfBirth && Date.parse(v.dateOfBirth) <= Date.now()
      if (!ok)
        ctx.addIssue({
          code: "custom",
          path: ["dateOfBirth"],
          message: "errors.dateOfBirth",
        })
    } else {
      const age = Number(v.ageYears)
      if (!v.ageYears || !Number.isInteger(age) || age < 0 || age > 120)
        ctx.addIssue({
          code: "custom",
          path: ["ageYears"],
          message: "errors.age",
        })
    }
  })

type Form = z.input<typeof schema>

export function Component() {
  const { t, i18n } = useTranslation()
  const navigate = useNavigate()
  const {
    register,
    handleSubmit,
    control,
    formState: { errors, isSubmitting },
  } = useForm<Form>({
    resolver: zodResolver(schema),
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
    navigate(`/patients/${patient.id}`, { replace: true })
  }

  return (
    <>
      <PageHeader title={t("newPatient.title")} back="/patients" />
      <form noValidate onSubmit={handleSubmit(onSubmit)} className="space-y-5">
        <fieldset className="space-y-4 rounded-2xl bg-secondary/60 p-4">
          <legend className="sr-only">{t("newPatient.consentTitle")}</legend>
          <p className="font-medium text-foreground">
            {t("newPatient.consentTitle")}
          </p>
          <p className="text-sm text-muted-foreground">
            {t("newPatient.consentText")}
          </p>
          <Field id="consentMethod" label={t("newPatient.consentMethod")}>
            <Select {...register("consentMethod")} id="consentMethod">
              <option value="verbal">{t("consent.verbal")}</option>
              <option value="thumbprint">{t("consent.thumbprint")}</option>
              <option value="signature">{t("consent.signature")}</option>
            </Select>
          </Field>
          <label className="flex items-start gap-3 text-sm text-foreground">
            <input
              type="checkbox"
              className="mt-0.5 size-5 accent-primary"
              {...register("consent")}
              aria-invalid={errors.consent ? true : undefined}
            />
            {t("newPatient.consentGiven")}
          </label>
          {errors.consent ? (
            <p role="alert" className="text-sm text-destructive">
              {err("consent")}
            </p>
          ) : null}
        </fieldset>

        <div className="grid gap-4 sm:grid-cols-2">
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
        </div>

        <Field id="sex" label={t("patient.sex")} error={err("sex")}>
          <Select
            {...register("sex")}
            {...describe("sex", err("sex"))}
            defaultValue=""
          >
            <option value="" disabled>
              {t("common.choose")}
            </option>
            <option value="female">{t("sex.female")}</option>
            <option value="male">{t("sex.male")}</option>
          </Select>
        </Field>

        <Field id="knowsBirthDate" label={t("newPatient.knowsBirthDate")}>
          <Select {...register("knowsBirthDate")} id="knowsBirthDate">
            <option value="yes">{t("newPatient.birthDateYes")}</option>
            <option value="no">{t("newPatient.birthDateNo")}</option>
          </Select>
        </Field>
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

        <div className="grid gap-4 sm:grid-cols-2">
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
        </div>

        <Field
          id="phone"
          label={t("patient.phone")}
          hint={t("common.optional")}
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
          hint={t("common.optional")}
        >
          <Input {...register("nationalId")} {...describe("nationalId")} />
        </Field>
        <Field id="preferredLanguage" label={t("patient.preferredLanguage")}>
          <Select {...register("preferredLanguage")} id="preferredLanguage">
            {LANGUAGES.map((l) => (
              <option key={l.code} value={l.code}>
                {l.label}
              </option>
            ))}
          </Select>
        </Field>

        <Button
          type="submit"
          size="xl"
          className="w-full"
          disabled={isSubmitting}
        >
          {t("newPatient.save")}
        </Button>
      </form>
    </>
  )
}
