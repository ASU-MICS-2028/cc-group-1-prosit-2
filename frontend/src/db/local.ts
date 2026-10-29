import Dexie, { type EntityTable } from "dexie"
import { getSession } from "@/auth/session"
import type {
  Appointment,
  Entity,
  OutboxItem,
  Patient,
  AuditEvent,
  Media,
  ProfilePhoto,
  Vitals,
} from "./types"

/**
 * The phone's own database (IndexedDB). Every save lands here first and is queued in the outbox,
 * so a CHV can work for days without signal; the sync sends the queue when the network is back.
 */
export const db = new Dexie("healthconnect") as Dexie & {
  patients: EntityTable<Patient, "id">
  vitals: EntityTable<Vitals, "id">
  appointments: EntityTable<Appointment, "id">
  outbox: EntityTable<OutboxItem, "seq">
  profilePhotos: EntityTable<ProfilePhoto, "userId">
  media: EntityTable<Media, "id">
  auditEvents: EntityTable<AuditEvent, "id">
}

db.version(1).stores({
  patients: "id, familyName, givenName, community, updatedAt",
  vitals: "id, patientId, takenAt",
  appointments: "id, patientId, scheduledAt, status",
  outbox: "++seq, entity, entityId",
})
// v2: the signed-in health worker's profile photo, kept on this phone only.
db.version(2).stores({ profilePhotos: "userId" })
// v3: files that belong to records (consent evidence, wound photos), and the consent method
// "verbal" renamed "spoken" to match the database (Liza's schema).
db.version(3)
  .stores({ media: "id, patientId, ownerId, uploadedAt" })
  .upgrade((tx) =>
    tx
      .table<Patient, string>("patients")
      .toCollection()
      .modify((p) => {
        if ((p.consent.method as string) === "verbal")
          p.consent.method = "spoken"
      })
  )

// v4: the audit trail (lib/audit.ts).
db.version(4).stores({ auditEvents: "id, at, patientId, action, syncedAt" })

const table = {
  patient: db.patients,
  vitals: db.vitals,
  appointment: db.appointments,
} as const

/**
 * Save a record and queue it for the server in one transaction: never one without the other,
 * and never without its audit entry (who created or changed it, when).
 */
export async function saveAndQueue<T extends Patient | Vitals | Appointment>(
  entity: Entity,
  record: T,
  op: OutboxItem["op"] = "create"
) {
  const at = new Date().toISOString()
  await db.transaction(
    "rw",
    [table[entity], db.outbox, db.auditEvents],
    async () => {
      await (table[entity] as unknown as EntityTable<T, "id">).put(record)
      await db.outbox.add({
        entity,
        entityId: record.id,
        op,
        payload: record,
        queuedAt: at,
        attempts: 0,
      })
      await db.auditEvents.add(
        auditEvent(
          op,
          entity,
          record.id,
          entity === "patient" ? record.id : (record as Vitals).patientId
        )
      )
    }
  )
}

/** An audit entry for the signed-in person (or "unknown" if there is none). */
export function auditEvent(
  action: AuditEvent["action"],
  entity: AuditEvent["entity"],
  entityId?: string,
  patientId?: string,
  detail?: string
): AuditEvent {
  const user = getSession()?.user
  return {
    id: newId(),
    at: new Date().toISOString(),
    actorId: user?.id ?? "unknown",
    actorName: user?.name ?? "unknown",
    role: user?.role ?? "unknown",
    action,
    entity,
    entityId,
    patientId,
    outcome: action === "denied" ? "denied" : "success",
    purpose:
      user?.role === "admin" || user?.role === "district_officer"
        ? "HOPERAT"
        : "TREAT",
    detail,
  }
}

export function newId() {
  return crypto.randomUUID()
}
