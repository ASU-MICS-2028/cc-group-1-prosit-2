import {
  Controller,
  useFormContext,
  useFormState,
  useWatch,
} from "react-hook-form"
import { useTranslation } from "react-i18next"
import { ChoiceTiles } from "@/components/form/ChoiceTiles"
import { DatePicker } from "@/components/form/DatePicker"
import { describe } from "@/components/form/describe"
import { Dropdown } from "@/components/form/Dropdown"
import { Field, Input } from "@/components/form/Field"
import { Segmented } from "@/components/form/Segmented"
import { Mars, Venus, VenusAndMars } from "@/components/icons"
import { LANGUAGES } from "@/i18n"
import type { DetailsForm } from "./patientForm"

/** The form's error for a field, in the app's language. */
function useErr() {
  const { t } = useTranslation()
  // its own subscription: the compiler memoises <PersonFields />, so it can't rely on the
  // parent re-rendering when errors change
  const { errors } = useFormState<DetailsForm>()
  return (key: keyof DetailsForm) => {
    const message = errors[key]?.message
    return message ? t(message as "errors.required") : undefined
  }
}

/** Name, sex, and date of birth or age: used when registering and when editing a patient. */
export function PersonFields() {
  const { t } = useTranslation()
  const { register, control } = useFormContext<DetailsForm>()
  const err = useErr()
  const knowsBirthDate = useWatch({ control, name: "knowsBirthDate" }) === "yes"
  return (
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
          <Controller
            control={control}
            name="dateOfBirth"
            render={({ field }) => (
              <DatePicker
                id="dateOfBirth"
                value={field.value}
                onChange={field.onChange}
                max={new Date()}
                startWithYear
                invalid={!!err("dateOfBirth")}
                describedBy={
                  err("dateOfBirth") ? "dateOfBirth-error" : undefined
                }
              />
            )}
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
  )
}

/** Where the patient lives and how to reach them. */
export function PlaceFields() {
  const { t } = useTranslation()
  const { register, control } = useFormContext<DetailsForm>()
  const err = useErr()
  return (
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
      <Field id="preferredLanguage" label={t("patient.preferredLanguage")}>
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
  )
}
