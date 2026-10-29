// The records the app keeps on the phone. Field names follow the API contract the team agrees
// on Tue 27 Oct (docs/frontend-needs.md); until then this is the frontend's proposal.

export type Sex = "female" | "male"

export type ConsentMethod = "verbal" | "thumbprint" | "signature"

/** Recorded before any health data is saved (Ghana Data Protection Act; Germain's consent flow). */
export interface Consent {
  given: boolean
  method: ConsentMethod
  /** ISO time */
  at: string
}

export interface Patient {
  /** Made on the phone (crypto.randomUUID) so a record has its id before it ever reaches the server */
  id: string
  givenName: string
  familyName: string
  sex: Sex
  /** YYYY-MM-DD; when unknown, the CHV enters an age and we store an estimate */
  dateOfBirth: string
  birthDateEstimated: boolean
  phone?: string
  /** Ghana Card or NHIS number, when the patient has one */
  nationalId?: string
  community: string
  district: string
  preferredLanguage: string
  consent: Consent
  createdAt: string
  updatedAt: string
  /** Server version, for conflict checks on sync (Liza's policy); 0 = never synced */
  version: number
}

export interface Vitals {
  id: string
  patientId: string
  /** ISO time the measurement was taken */
  takenAt: string
  systolic?: number
  diastolic?: number
  temperatureC?: number
  weightKg?: number
  heightCm?: number
  pulse?: number
  notes?: string
  createdAt: string
  version: number
}

export const APPOINTMENT_TYPES = [
  "follow_up",
  "antenatal",
  "postnatal",
  "referral",
  "teleconsult",
] as const

export type AppointmentType = (typeof APPOINTMENT_TYPES)[number]

export type AppointmentStatus =
  "scheduled" | "completed" | "missed" | "cancelled"

export type Priority = "routine" | "urgent" | "emergency"

export interface Appointment {
  id: string
  patientId: string
  type: AppointmentType
  /** ISO time */
  scheduledAt: string
  facility: string
  reason: string
  priority: Priority
  status: AppointmentStatus
  createdAt: string
  updatedAt: string
  version: number
}

export type Entity = "patient" | "vitals" | "appointment"

/** One change waiting to go to the server, oldest first. */
export interface OutboxItem {
  seq?: number
  entity: Entity
  entityId: string
  op: "create" | "update"
  payload: Patient | Vitals | Appointment
  queuedAt: string
  attempts: number
  lastError?: string
}
