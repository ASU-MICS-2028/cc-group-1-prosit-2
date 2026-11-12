import type { Appointment } from "@/db/types"

/** Same numbers as the API (CallWindow.cs): open 15 minutes before the booked time, closed 2 hours after. */
export const OPENS_BEFORE_MS = 15 * 60_000
export const CLOSES_AFTER_MS = 2 * 3_600_000

/** Whether a teleconsult's call can be joined now. An emergency can start as soon as it's booked. */
export function callWindow(
  a: Pick<Appointment, "scheduledAt" | "priority">,
  now = Date.now()
): { state: "early" | "open" | "ended"; opensAt: Date } {
  const booked = Date.parse(a.scheduledAt)
  const opensAt = new Date(booked - OPENS_BEFORE_MS)
  if (now > booked + CLOSES_AFTER_MS) return { state: "ended", opensAt }
  if (a.priority !== "emergency" && now < opensAt.getTime())
    return { state: "early", opensAt }
  return { state: "open", opensAt }
}
