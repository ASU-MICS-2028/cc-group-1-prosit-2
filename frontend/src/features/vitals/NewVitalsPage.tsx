import { zodResolver } from "@hookform/resolvers/zod"
import { TriangleAlert } from "lucide-react"
import { useForm, useWatch } from "react-hook-form"
import { useTranslation } from "react-i18next"
import { useNavigate, useParams } from "react-router-dom"
import { z } from "zod"
import { Field, Input, Textarea } from "@/components/form/Field"
import { describe } from "@/components/form/describe"
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
    navigate(`/patients/${id}`, { replace: true })
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
    <>
      <PageHeader title={t("vitals.title")} back={`/patients/${id}`} />
      {patient ? (
        <p className="-mt-3 mb-5 text-sm text-muted-foreground">
          {fullName(patient)}
        </p>
      ) : null}
      <form noValidate onSubmit={handleSubmit(onSubmit)} className="space-y-5">
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
          {number("weightKg", t("vitals.weight"), "kg", "0.1")}
          {number("heightCm", t("vitals.height"), "cm", "0.1")}
        </div>
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
          className="w-full"
          disabled={isSubmitting}
        >
          {t("vitals.save")}
        </Button>
      </form>
    </>
  )
}
