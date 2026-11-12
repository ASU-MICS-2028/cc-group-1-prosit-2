import type { Appointment, Vitals } from "@/db/types"
import { api } from "./client"

/** One person registered with the signed-in patient's phone (a household can share one). */
export interface MyPerson {
  id: string
  name: string
  community: string | null
  sex: string | null
  dateOfBirth: string | null
}

/** GET /api/my/health: the signed-in patient's own records, read-only. */
export interface MyHealth {
  people: MyPerson[]
  appointments: Appointment[]
  vitals: (Vitals & { temperatureC?: number | null })[]
}

export const myHealth = () => api<MyHealth>("/my/health")
