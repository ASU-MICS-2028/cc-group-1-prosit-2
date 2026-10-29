import { zodResolver } from "@hookform/resolvers/zod"
import { Ruler, TriangleAlert, X } from "@/components/icons"
import { useState } from "react"
import { Controller, useForm, useWatch } from "react-hook-form"
import { useTranslation } from "react-i18next"
import { useNavigate, useParams } from "react-router-dom"
import { z } from "zod"
import { Field, Input, Textarea } from "@/components/form/Field"
import { describe } from "@/components/form/describe"
import { NumberWheel } from "@/components/form/NumberWheel"
import { PageHeader } from "@/components/PageHeader"
import { Button } from "@/components/ui/button"
import { newId, saveAndQueue } from "@/db/local"
import type { Vitals } from "@/db/types"
import { toLocalInput } from "@/lib/dates"
import { cn } from "@/lib/utils"
import { URGENT_FLAGS, vitalsFlags } from "@/lib/vitals"
import { fullName, usePatient } from "@/features/patients/usePatients"

/** An optional number within a plausible range ("" = not measured). */
const measure = (min: number, max: number) =>
  z
    .string()
    .trim()
    .refine(
      (s) => s === "" || (Number(s) >= min && Number(s) <= max),
      "errors.range"
    )

const schema = z
  .object({
    takenAt: z.string().min(1, "errors.required"),
    systolic: measure(50, 260),
    diastolic: measure(30, 160),
    temperatureC: measure(30, 45),
    pulse: measure(20, 250),
    weightKg: measure(0.5, 300),
    heightCm: measure(30, 250),
    notes: z.string(),
  })
  .refine((v) => (v.systolic === "") === (v.diastolic === ""), {
    path: ["diastolic"],
    message: "errors.bothBp",
  })
  .refine(
    (v) =>
      [v.systolic, v.temperatureC, v.pulse, v.weightKg, v.heightCm].some(
        (s) => s !== ""
      ),
    { path: ["systolic"], message: "errors.oneVital" }
  )

type Form = z.input<typeof schema>
const num = (s: string) => (s.trim() === "" ? undefined : Number(s))

export function Component() {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const { id = "" } = useParams()
  const patient = usePatient(id)
  const {
    register,
    handleSubmit,
    control,
    setValue,
    formState: { errors, isSubmitting },
  } = useForm<Form>({
    resolver: zodResolver(schema),
    defaultValues: {
      takenAt: toLocalInput(new Date()),
      systolic: "",
      diastolic: "",
      temperatureC: "",
      pulse: "",
      weightKg: "",
      heightCm: "",
      notes: "",
    },
  })
  const err = (key: keyof Form) => {
    const message = errors[key]?.message
    return message ? t(message as "errors.required") : undefined
  }
  // Live prompts while typing, so the CHV sees "refer today" before saving.
  const v = useWatch({ control })
  const flags = vitalsFlags({
    systolic: num(v.systolic ?? ""),
    diastolic: num(v.diastolic ?? ""),
    temperatureC: num(v.temperatureC ?? ""),
    pulse: num(v.pulse ?? ""),
  })
  const urgent = flags.some((f) => URGENT_FLAGS.includes(f))
  // Weight and height are optional and picked on wheels (Figma "Altura y Peso").
  const [body, setBody] = useState(false)
  function toggleBody(on: boolean) {
    setBody(on)
    setValue("weightKg", on ? "60" : "")
    setValue("heightCm", on ? "160" : "")
  }

  async function onSubmit(f: Form) {
    const now = new Date().toISOString()
    const vitals: Vitals = {
      id: newId(),
      patientId: id,
      takenAt: new Date(f.takenAt).toISOString(),
      systolic: num(f.systolic),
      diastolic: num(f.diastolic),
      temperatureC: num(f.temperatureC),
      pulse: num(f.pulse),
      weightKg: num(f.weightKg),
      heightCm: num(f.heightCm),
      notes: f.notes.trim() || undefined,
      createdAt: now,
      version: 0,
    }
    await saveAndQueue("vitals", vitals)
    // Something needs a facility today: go straight to the referral prompt.
    const needsReferral = vitalsFlags(vitals).some((f) =>
      URGENT_FLAGS.includes(f)
    )
    navigate(needsReferral ? `/patients/${id}/refer` : `/patients/${id}`, {
      replace: true,
    })
  }

  const number = (key: keyof Form, label: string, unit: string, step = "1") => (
    <Field id={key} label={`${label} (${unit})`} error={err(key)}>
      <Input
        type="number"
        inputMode="decimal"
        step={step}
        {...register(key)}
        {...describe(key, err(key))}
      />
    </Field>
  )

  return (
    <div className="mx-auto flex min-h-[calc(100svh-5rem)] flex-col max-w-lg">
      <PageHeader title={t("vitals.title")} back={`/patients/${id}`} />
      {patient ? (
        <p className="-mt-3 mb-5 text-sm text-muted-foreground">
          {fullName(patient)}
        </p>
      ) : null}
      <form
        noValidate
        onSubmit={handleSubmit(onSubmit)}
        className="flex flex-1 flex-col gap-5"
      >
        <Field id="takenAt" label={t("vitals.takenAt")} error={err("takenAt")}>
          <Input
            type="datetime-local"
            {...register("takenAt")}
            {...describe("takenAt", err("takenAt"))}
          />
        </Field>
        <div className="grid grid-cols-2 gap-4">
          {number("systolic", t("vitals.systolic"), "mmHg")}
          {number("diastolic", t("vitals.diastolic"), "mmHg")}
          {number("temperatureC", t("vitals.temperature"), "°C", "0.1")}
          {number("pulse", t("vitals.pulse"), t("vitals.perMinute"))}
        </div>
        {body ? (
          <fieldset className="rounded-xl bg-card shadow-card p-4">
            <legend className="sr-only">{t("vitals.bodyTitle")}</legend>
            <div className="mb-3 flex items-center justify-between">
              <p className="font-semibold text-foreground">
                {t("vitals.bodyTitle")}
              </p>
              <button
                type="button"
                onClick={() => toggleBody(false)}
                className="flex h-11 items-center gap-1 px-2 text-sm font-semibold text-primary"
              >
                <X className="size-4" aria-hidden />
                {t("vitals.bodyRemove")}
              </button>
            </div>
            <div className="grid grid-cols-2 gap-4">
              <Controller
                control={control}
                name="weightKg"
                render={({ field }) => (
                  <NumberWheel
                    label={t("vitals.weight")}
                    unit="kg"
                    min={2}
                    max={200}
                    step={0.5}
                    value={Number(field.value) || 60}
                    onChange={(n) => field.onChange(String(n))}
                  />
                )}
              />
              <Controller
                control={control}
                name="heightCm"
                render={({ field }) => (
                  <NumberWheel
                    label={t("vitals.height")}
                    unit="cm"
                    min={40}
                    max={220}
                    value={Number(field.value) || 160}
                    onChange={(n) => field.onChange(String(n))}
                  />
                )}
              />
            </div>
          </fieldset>
        ) : (
          <Button
            type="button"
            size="xl"
            variant="secondary"
            className="w-full"
            onClick={() => toggleBody(true)}
          >
            <Ruler aria-hidden />
            {t("vitals.bodyAdd")}
          </Button>
        )}
        {flags.length > 0 ? (
          <div
            role="status"
            className={cn(
              "flex gap-3 rounded-xl border p-4 text-sm font-medium",
              urgent
                ? "border-destructive bg-destructive-soft text-destructive"
                : "border-warning bg-warning-soft text-warning"
            )}
          >
            <TriangleAlert className="size-5 shrink-0" aria-hidden />
            <div>
              <p>{flags.map((f) => t(`flags.${f}`)).join(", ")}</p>
              <p className="font-normal">
                {urgent ? t("vitals.referToday") : t("vitals.recheck")}
              </p>
            </div>
          </div>
        ) : null}
        <Field id="notes" label={t("vitals.notes")} hint={t("common.optional")}>
          <Textarea {...register("notes")} {...describe("notes")} />
        </Field>
        <Button
          type="submit"
          size="xl"
          className="mt-auto w-full"
          disabled={isSubmitting}
        >
          {t("vitals.save")}
        </Button>
      </form>
    </div>
  )
}
