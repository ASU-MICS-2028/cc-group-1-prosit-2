// The records the app keeps on the phone. Field names follow the API contract the team agrees
// on Tue 27 Oct (docs/frontend-needs.md); until then this is the frontend's proposal.

export type Sex = "female" | "male" | "other"

export type ConsentMethod = "spoken" | "thumbprint" | "signature"

/** The wording read to the patient; the evidence proves agreement to this exact version. */
export const CONSENT_STATEMENT_VERSION = "v1"

/**
 * Recorded before any health data is saved (Act 843 s.20 and s.37(2)(b): health data needs the
 * patient's consent, since CHVs are not health professionals). The evidence is a voice clip, a
 * signature, or a photo of the inked thumbprint on the paper form; its SHA-256 is kept with the
 * record so anyone can check later that the file was not changed.
 */
export interface Consent {
  given: boolean
  method: ConsentMethod
  /** ISO time */
  at: string
  statementVersion?: string
  /** language the statement was read in */
  language?: string
  /** the health worker who asked (user id) */
  obtainedBy?: string
  /** someone else who heard the patient agree; required when there is no recording */
  witness?: string
  evidence?: { mediaId: string; sha256: string; mime: string }
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

/**
 * Where a referral or teleconsult request stands (maps to FHIR ServiceRequest.status:
 * requested = active, accepted = active + accepted task, declined = revoked, seen = completed).
 */
export type ReferralStatus = "requested" | "accepted" | "declined" | "seen"

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
  /** Only for referrals and teleconsults: set by the receiving nurse or doctor */
  referralStatus?: ReferralStatus
  /** Who accepted or declined it, and when */
  handledBy?: string
  handledAt?: string
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

export type MediaKind =
  "consent-audio" | "consent-signature" | "consent-photo" | "wound-photo"

/**
 * A file that belongs to a record (consent evidence, a wound photo). Kept on the phone and
 * uploaded to the private S3 bucket through a short-lived upload link when there's signal.
 */
export interface Media {
  id: string
  patientId: string
  /** the record it belongs to */
  ownerEntity: Entity
  ownerId: string
  kind: MediaKind
  blob: Blob
  mime: string
  size: number
  sha256: string
  createdAt: string
  /** set once the upload succeeded */
  uploadedAt?: string
  attempts: number
  lastError?: string
}

export type AuditAction = "view" | "create" | "update" | "denied"

/**
 * One access to patient data, recorded on the phone as it happens (FHIR AuditEvent; the
 * server's access_log, Lab 6). Kept even offline and uploaded with the next sync, where the
 * server chains it into its tamper-evident log. Act 843 s.28: who saw or changed what, when.
 */
export interface AuditEvent {
  id: string
  /** ISO time */
  at: string
  actorId: string
  actorName: string
  role: string
  action: AuditAction
  /** what was touched: a record type, or a page for a refused visit */
  entity: Entity | "page"
  entityId?: string
  patientId?: string
  outcome: "success" | "denied"
  /** FHIR purposeOfUse: TREAT for care, HOPERAT for administration */
  purpose: "TREAT" | "HOPERAT"
  /** e.g. the address of a refused page */
  detail?: string
  /** set once the server has it */
  syncedAt?: string
}

/** A health worker's profile photo: resized on the phone, stored on the phone only. */
export interface ProfilePhoto {
  userId: string
  photo: Blob
  updatedAt: string
}
