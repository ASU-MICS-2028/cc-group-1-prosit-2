import { zodResolver } from "@hookform/resolvers/zod"
import { useForm } from "react-hook-form"
import { useTranslation } from "react-i18next"
import { useNavigate, useParams } from "react-router-dom"
import { z } from "zod"
import { Field, Input, Select, Textarea } from "@/components/form/Field"
import { describe } from "@/components/form/describe"
import { PageHeader } from "@/components/PageHeader"
import { Button } from "@/components/ui/button"
import { newId, saveAndQueue } from "@/db/local"
import { APPOINTMENT_TYPES, type Appointment } from "@/db/types"
import { fullName, usePatient } from "@/features/patients/usePatients"

const schema = z.object({
  type: z.enum(APPOINTMENT_TYPES),
  scheduledAt: z
    .string()
    .min(1, "errors.required")
    .refine((s) => Date.parse(s) > Date.now(), "errors.future"),
  facility: z.string().trim().min(1, "errors.required"),
  priority: z.enum(["routine", "urgent", "emergency"]),
  reason: z.string().trim().min(1, "errors.required"),
})

type Form = z.input<typeof schema>

/**
 * Book a follow-up, or refer the patient (referral / teleconsult). The specialist side and the
 * referral queue are Bernard's mock flow for the demo; here the CHV only creates the request.
 */
export function Component() {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const { id = "" } = useParams()
  const patient = usePatient(id)
  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<Form>({
    resolver: zodResolver(schema),
    defaultValues: {
      type: "follow_up",
      priority: "routine",
      facility: "",
      reason: "",
    },
  })
  const err = (key: keyof Form) => {
    const message = errors[key]?.message
    return message ? t(message as "errors.required") : undefined
  }

  async function onSubmit(f: Form) {
    const now = new Date().toISOString()
    const appointment: Appointment = {
      id: newId(),
      patientId: id,
      type: f.type,
      scheduledAt: new Date(f.scheduledAt).toISOString(),
      facility: f.facility.trim(),
      priority: f.priority,
      reason: f.reason.trim(),
      status: "scheduled",
      createdAt: now,
      updatedAt: now,
      version: 0,
    }
    await saveAndQueue("appointment", appointment)
    navigate(`/patients/${id}`, { replace: true })
  }

  return (
    <>
      <PageHeader title={t("newAppointment.title")} back={`/patients/${id}`} />
      {patient ? (
        <p className="-mt-3 mb-5 text-sm text-muted-foreground">
          {fullName(patient)}
        </p>
      ) : null}
      <form noValidate onSubmit={handleSubmit(onSubmit)} className="space-y-5">
        <Field id="type" label={t("newAppointment.type")}>
          <Select {...register("type")} id="type">
            {APPOINTMENT_TYPES.map((type) => (
              <option key={type} value={type}>
                {t(`appointmentType.${type}`)}
              </option>
            ))}
          </Select>
        </Field>
        <Field
          id="scheduledAt"
          label={t("newAppointment.when")}
          error={err("scheduledAt")}
        >
          <Input
            type="datetime-local"
            {...register("scheduledAt")}
            {...describe("scheduledAt", err("scheduledAt"))}
          />
        </Field>
        <Field
          id="facility"
          label={t("newAppointment.facility")}
          error={err("facility")}
        >
          <Input
            {...register("facility")}
            {...describe("facility", err("facility"))}
          />
        </Field>
        <Field id="priority" label={t("newAppointment.priority")}>
          <Select {...register("priority")} id="priority">
            <option value="routine">{t("priority.routine")}</option>
            <option value="urgent">{t("priority.urgent")}</option>
            <option value="emergency">{t("priority.emergency")}</option>
          </Select>
        </Field>
        <Field
          id="reason"
          label={t("newAppointment.reason")}
          error={err("reason")}
        >
          <Textarea
            {...register("reason")}
            {...describe("reason", err("reason"))}
          />
        </Field>
        <Button
          type="submit"
          size="xl"
          className="w-full"
          disabled={isSubmitting}
        >
          {t("newAppointment.save")}
        </Button>
      </form>
    </>
  )
}
