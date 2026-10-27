import { zodResolver } from "@hookform/resolvers/zod"
import { useLiveQuery } from "dexie-react-hooks"
import { Controller, useForm } from "react-hook-form"
import { useTranslation } from "react-i18next"
import { useNavigate, useParams, useSearchParams } from "react-router-dom"
import { z } from "zod"
import { DatePicker } from "@/components/form/DatePicker"
import { Dropdown } from "@/components/form/Dropdown"
import { Field, Input, Textarea } from "@/components/form/Field"
import { Segmented } from "@/components/form/Segmented"
import { describe } from "@/components/form/describe"
import { PageHeader } from "@/components/PageHeader"
import { Button } from "@/components/ui/button"
import { db, newId, saveAndQueue } from "@/db/local"
import { toLocalInput } from "@/lib/dates"
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
 * With an appointment id in the address it changes that booking instead (reschedule).
 */
export function Component() {
  const { appointmentId } = useParams()
  const existing = useLiveQuery(
    async () =>
      appointmentId
        ? ((await db.appointments.get(appointmentId)) ?? null)
        : null,
    [appointmentId]
  )
  if (existing === undefined) return null
  return <AppointmentForm existing={existing ?? undefined} />
}

function AppointmentForm({ existing }: { existing?: Appointment }) {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const { id = "" } = useParams()
  const patient = usePatient(id)
  // The "Refer today" screen opens this form with the referral already filled in.
  const [query] = useSearchParams()
  const pick = <T extends string>(
    key: string,
    allowed: readonly T[],
    fallback: T
  ): T =>
    (allowed as readonly string[]).includes(query.get(key) ?? "")
      ? (query.get(key) as T)
      : fallback
  const {
    register,
    control,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<Form>({
    resolver: zodResolver(schema),
    defaultValues: existing
      ? {
          type: existing.type,
          priority: existing.priority,
          facility: existing.facility,
          reason: existing.reason,
          // an overdue visit must be moved to a new time, so start empty
          scheduledAt:
            existing.scheduledAt > new Date().toISOString()
              ? toLocalInput(new Date(existing.scheduledAt))
              : "",
        }
      : {
          type: pick("type", APPOINTMENT_TYPES, "follow_up"),
          priority: pick(
            "priority",
            ["routine", "urgent", "emergency"] as const,
            "routine"
          ),
          facility: "",
          reason: query.get("reason") ?? "",
        },
  })
  const err = (key: keyof Form) => {
    const message = errors[key]?.message
    return message ? t(message as "errors.required") : undefined
  }

  async function onSubmit(f: Form) {
    const now = new Date().toISOString()
    if (existing) {
      await saveAndQueue(
        "appointment",
        {
          ...existing,
          type: f.type,
          scheduledAt: new Date(f.scheduledAt).toISOString(),
          facility: f.facility.trim(),
          priority: f.priority,
          reason: f.reason.trim(),
          status: "scheduled",
          updatedAt: now,
        },
        "update"
      )
      navigate("/appointments", { replace: true })
      return
    }
    const appointment: Appointment = {
      id: newId(),
      patientId: id,
      type: f.type,
      scheduledAt: new Date(f.scheduledAt).toISOString(),
      facility: f.facility.trim(),
      priority: f.priority,
      reason: f.reason.trim(),
      status: "scheduled",
      referralStatus:
        f.type === "referral" || f.type === "teleconsult"
          ? "requested"
          : undefined,
      createdAt: now,
      updatedAt: now,
      version: 0,
    }
    await saveAndQueue("appointment", appointment)
    navigate(`/patients/${id}`, { replace: true })
  }

  return (
    <div className="mx-auto flex min-h-[calc(100svh-5rem)] flex-col max-w-lg">
      <PageHeader
        title={
          existing ? t("newAppointment.editTitle") : t("newAppointment.title")
        }
        back={existing ? "/appointments" : `/patients/${id}`}
      />
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
        <Field id="type" label={t("newAppointment.type")}>
          <Controller
            control={control}
            name="type"
            render={({ field }) => (
              <Dropdown
                id="type"
                value={field.value}
                onChange={field.onChange}
                options={APPOINTMENT_TYPES.map((type) => ({
                  value: type,
                  label: t(`appointmentType.${type}`),
                }))}
              />
            )}
          />
        </Field>
        <Field
          id="scheduledAt"
          label={t("newAppointment.when")}
          error={err("scheduledAt")}
        >
          <Controller
            control={control}
            name="scheduledAt"
            render={({ field }) => (
              <DatePicker
                id="scheduledAt"
                withTime
                value={field.value}
                onChange={field.onChange}
                min={new Date()}
                invalid={!!err("scheduledAt")}
                describedBy={
                  err("scheduledAt") ? "scheduledAt-error" : undefined
                }
              />
            )}
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
        <Controller
          control={control}
          name="priority"
          render={({ field }) => (
            <Segmented
              name="priority"
              legend={t("newAppointment.priority")}
              value={field.value}
              onChange={field.onChange}
              tone={(p) =>
                p === "emergency"
                  ? "destructive"
                  : p === "urgent"
                    ? "warning"
                    : "primary"
              }
              options={(["routine", "urgent", "emergency"] as const).map(
                (p) => ({
                  value: p,
                  label: t(`priority.${p}`),
                })
              )}
            />
          )}
        />
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
          className="mt-auto w-full"
          disabled={isSubmitting}
        >
          {existing
            ? t("newAppointment.saveChanges")
            : t("newAppointment.save")}
        </Button>
      </form>
    </div>
  )
}
