import { useLiveQuery } from "dexie-react-hooks"
import { db } from "@/db/local"
import type { Appointment, Patient, Vitals } from "@/db/types"
import { type Flag, URGENT_FLAGS, vitalsFlags } from "@/lib/vitals"

const DAY = 86_400_000
const BP_FLAGS: Flag[] = ["bpCrisis", "bpHigh"]
const FEVER_FLAGS: Flag[] = ["fever", "highFever"]

export interface PatientStatus {
  patient: Patient
  /** the most recent reading, if any */
  latest?: Vitals
  flags: Flag[]
  /** the latest reading said "refer today" */
  urgent: boolean
  /** the soonest booked visit, overdue ones included */
  nextVisit?: Appointment
  overdue: boolean
  dueToday: boolean
  /** has changes on this phone that haven't reached the server */
  unsynced: boolean
}

export const startOfToday = (now = new Date()) =>
  new Date(now.getFullYear(), now.getMonth(), now.getDate())

/**
 * Everything the Home dashboard and the patient list need about the health worker's caseload,
 * worked out from the phone's own database in one pass (so it works offline and stays live).
 */
export function useCaseload(): PatientStatus[] | undefined {
  return useLiveQuery(async () => {
    const [patients, vitals, appointments, outbox] = await Promise.all([
      db.patients.orderBy("updatedAt").reverse().toArray(),
      db.vitals.orderBy("takenAt").toArray(),
      db.appointments.where("status").equals("scheduled").sortBy("scheduledAt"),
      db.outbox.toArray(),
    ])
    const latest = new Map<string, Vitals>()
    for (const v of vitals) latest.set(v.patientId, v) // ascending, so the last one wins
    const next = new Map<string, Appointment>()
    for (const a of appointments)
      if (!next.has(a.patientId)) next.set(a.patientId, a)
    const pending = new Set<string>()
    for (const item of outbox) {
      const payload = item.payload as { patientId?: string }
      pending.add(
        item.entity === "patient" ? item.entityId : (payload.patientId ?? "")
      )
    }

    const today = startOfToday()
    const tomorrow = new Date(today.getTime() + DAY)
    return patients.map((patient) => {
      const reading = latest.get(patient.id)
      const flags = reading ? vitalsFlags(reading) : []
      const visit = next.get(patient.id)
      const at = visit ? new Date(visit.scheduledAt) : null
      return {
        patient,
        latest: reading,
        flags,
        urgent: flags.some((f) => URGENT_FLAGS.includes(f)),
        nextVisit: visit,
        overdue: !!at && at < today,
        dueToday: !!at && at >= today && at < tomorrow,
        unsynced: pending.has(patient.id),
      }
    })
  }, [])
}

export interface HomeNumbers {
  patients: number
  visitsToday: { done: number; total: number }
  overdue: number
  openReferrals: number
  /** readings per day, oldest first, for the last 7 days */
  week: { day: Date; readings: number; flagged: number }[]
  /** patients whose latest reading shows high blood pressure, of those with a BP reading */
  highBp: { count: number; of: number }
  /** patients with a fever reading in the last 7 days, of those with a temperature reading */
  fever: { count: number; of: number }
  nextVisits: (Appointment & { patient?: Patient })[]
  toSend: number
}

/** The numbers on the Home dashboard. */
export function useHomeNumbers(): HomeNumbers | undefined {
  return useLiveQuery(async () => {
    const now = new Date()
    const today = startOfToday(now)
    const weekStart = new Date(today.getTime() - 6 * DAY)
    const [patients, recent, all, outbox] = await Promise.all([
      db.patients.count(),
      db.vitals
        .where("takenAt")
        .aboveOrEqual(weekStart.toISOString())
        .toArray(),
      db.appointments.toArray(),
      db.outbox.count(),
    ])
    const tomorrow = new Date(today.getTime() + DAY)
    const isToday = (a: Appointment) => {
      const at = new Date(a.scheduledAt)
      return at >= today && at < tomorrow
    }
    const todays = all.filter((a) => isToday(a) && a.status !== "cancelled")
    const week = Array.from({ length: 7 }, (_, i) => ({
      day: new Date(weekStart.getTime() + i * DAY),
      readings: 0,
      flagged: 0,
    }))
    for (const v of recent) {
      const i = Math.floor(
        (startOfToday(new Date(v.takenAt)).getTime() - weekStart.getTime()) /
          DAY
      )
      if (!week[i]) continue
      week[i].readings++
      if (vitalsFlags(v).length) week[i].flagged++
    }

    // Latest reading per patient, for the blood-pressure card; any fever this week.
    const allVitals = await db.vitals.orderBy("takenAt").toArray()
    const latestBp = new Map<string, Vitals>()
    for (const v of allVitals)
      if (v.systolic != null) latestBp.set(v.patientId, v)
    const feverPatients = new Set<string>()
    const tempPatients = new Set<string>()
    for (const v of recent) {
      if (v.temperatureC == null) continue
      tempPatients.add(v.patientId)
      if (vitalsFlags(v).some((f) => FEVER_FLAGS.includes(f)))
        feverPatients.add(v.patientId)
    }

    const upcoming = all
      .filter((a) => a.status === "scheduled" && new Date(a.scheduledAt) >= now)
      .sort((a, b) => a.scheduledAt.localeCompare(b.scheduledAt))
      .slice(0, 3)
    const people = await db.patients.bulkGet(upcoming.map((a) => a.patientId))

    return {
      patients,
      visitsToday: {
        done: todays.filter((a) => a.status === "completed").length,
        total: todays.length,
      },
      overdue: all.filter(
        (a) => a.status === "scheduled" && new Date(a.scheduledAt) < today
      ).length,
      openReferrals: all.filter(
        (a) =>
          a.referralStatus === "requested" || a.referralStatus === "accepted"
      ).length,
      week,
      highBp: {
        count: [...latestBp.values()].filter((v) =>
          vitalsFlags(v).some((f) => BP_FLAGS.includes(f))
        ).length,
        of: latestBp.size,
      },
      fever: { count: feverPatients.size, of: tempPatients.size },
      nextVisits: upcoming.map((a, i) => ({ ...a, patient: people[i] })),
      toSend: outbox,
    }
  }, [])
}
