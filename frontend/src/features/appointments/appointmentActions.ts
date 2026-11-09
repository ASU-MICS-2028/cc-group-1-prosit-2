import i18n from "@/i18n"
import { saved, saveFailed } from "@/lib/feedback"
import { saveAndQueue } from "@/db/local"
import type { Appointment, AppointmentStatus } from "@/db/types"

/** Mark a visit done, missed or cancelled; queued for sync like any other change. */
export async function setAppointmentStatus(
  appointment: Appointment,
  status: AppointmentStatus
) {
  try {
    await saveAndQueue(
      "appointment",
      { ...appointment, status, updatedAt: new Date().toISOString() },
      "update"
    )
  } catch (error) {
    return saveFailed(error)
  }
  saved(
    i18n.t("toast.appointmentStatus", { status: i18n.t(`status.${status}`) })
  )
}
