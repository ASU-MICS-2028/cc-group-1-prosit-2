import Dexie, { type EntityTable } from "dexie"
import type {
  Appointment,
  Entity,
  OutboxItem,
  Patient,
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
}

db.version(1).stores({
  patients: "id, familyName, givenName, community, updatedAt",
  vitals: "id, patientId, takenAt",
  appointments: "id, patientId, scheduledAt, status",
  outbox: "++seq, entity, entityId",
})
// v2: the signed-in health worker's profile photo, kept on this phone only.
db.version(2).stores({ profilePhotos: "userId" })

const table = {
  patient: db.patients,
  vitals: db.vitals,
  appointment: db.appointments,
} as const

/** Save a record and queue it for the server in one transaction: never one without the other. */
export async function saveAndQueue<T extends Patient | Vitals | Appointment>(
  entity: Entity,
  record: T,
  op: OutboxItem["op"] = "create"
) {
  await db.transaction("rw", [table[entity], db.outbox], async () => {
    await (table[entity] as unknown as EntityTable<T, "id">).put(record)
    await db.outbox.add({
      entity,
      entityId: record.id,
      op,
      payload: record,
      queuedAt: new Date().toISOString(),
      attempts: 0,
    })
  })
}

export function newId() {
  return crypto.randomUUID()
}
