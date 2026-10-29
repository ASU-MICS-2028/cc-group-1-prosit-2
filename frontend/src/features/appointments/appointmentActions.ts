import { saveAndQueue } from "@/db/local"
import type { Appointment, AppointmentStatus } from "@/db/types"

/** Mark a visit done, missed or cancelled; queued for sync like any other change. */
export async function setAppointmentStatus(
  appointment: Appointment,
  status: AppointmentStatus
) {
  await saveAndQueue(
    "appointment",
    { ...appointment, status, updatedAt: new Date().toISOString() },
    "update"
  )
}
