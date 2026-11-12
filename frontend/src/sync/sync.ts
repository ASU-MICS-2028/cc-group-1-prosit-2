import { useLiveQuery } from "dexie-react-hooks"
import type { EntityTable } from "dexie"
import { ApiError, api } from "@/api/client"
import { getSession, isDemoSession } from "@/auth/session"
import { auditEvent, db } from "@/db/local"
import type {
  Appointment,
  Entity,
  OutboxItem,
  Patient,
  Vitals,
} from "@/db/types"

type AnyRecord = Patient | Vitals | Appointment

const PATH: Record<Entity, string> = {
  patient: "/patients",
  vitals: "/vitals",
  appointment: "/appointments",
}

export interface SyncResult {
  sent: number
  failed: number
  conflicts: number
  /** files (consent evidence, photos) uploaded */
  uploaded?: number
  /** records other workers saved that this phone now has (a colleague's patient, a nurse's answer to a referral) */
  pulled?: number
}

function table(entity: Entity) {
  const tables = {
    patient: db.patients,
    vitals: db.vitals,
    appointment: db.appointments,
  }
  return tables[entity] as unknown as EntityTable<AnyRecord, "id">
}

let running: Promise<SyncResult> | null = null

/**
 * Sends the outbox to the API, oldest first, then fetches what other workers saved.
 *
 * Ids are made on the phone, so a create is a PUT to /<entity>/<id>: sending the same item twice
 * (lost reply on 2G) does no harm. Each save carries the version the phone last saw; the server
 * answers with the saved record and its new version, which the phone keeps. A 409 means someone
 * else changed the record first, and docs/sync.md says what happens then (appointments and
 * readings: the server's copy is kept; patients: held for the worker to review). A 409 the phone
 * cannot read is kept in the outbox, marked conflict, instead.
 */
export function syncNow(): Promise<SyncResult> {
  running ??= send().finally(() => {
    running = null
  })
  return running
}

async function send(): Promise<SyncResult> {
  const result: SyncResult = { sent: 0, failed: 0, conflicts: 0 }
  // Offline demo mode has no server account: everything stays on this phone, and the server is not asked.
  if (isDemoSession()) return result
  const queued = await db.outbox.orderBy("seq").toArray()
  for (const waiting of queued) {
    // read it again: an earlier save or conflict in this run may have moved its version or dropped it
    const item = await db.outbox.get(waiting.seq!)
    if (!item) continue
    try {
      await put(item, item.payload)
      result.sent++
    } catch (first) {
      let error = first
      // A new patient typed in with another district: put it in the CHV's own and send it again.
      if (await healDistrict(item, error)) {
        try {
          await put(item, item.payload)
          result.sent++
          continue
        } catch (again) {
          error = again
        }
      }
      const current = conflictCopy(error)
      if (current) {
        result.conflicts++
        if (await resolve(item, current)) result.sent++
        continue
      }
      const conflict = error instanceof ApiError && error.status === 409
      if (conflict) result.conflicts++
      else result.failed++
      await db.outbox.update(item.seq!, {
        attempts: item.attempts + 1,
        lastError: conflict ? "conflict" : String(error),
      })
      // Offline or server down: stop and keep the order; the rest waits for the next try.
      if (!(error instanceof ApiError)) break
    }
  }
  if (result.failed === 0) {
    const uploaded = await upload()
    if (uploaded) result.uploaded = uploaded
    await uploadAudit()
    const pulled = await pull()
    if (pulled) result.pulled = pulled
  }
  return result
}

/**
 * Before the district was filled in for them, a CHV could type another district, which the server
 * refuses for ever (403 wrong_district): the patient, and every reading and visit after it, stayed
 * stuck. For a patient the server has never accepted, the district is the CHV's own, so it is
 * corrected here, on the phone and in the queue, and sent again.
 */
async function healDistrict(
  item: OutboxItem,
  error: unknown
): Promise<boolean> {
  const session = getSession()
  if (
    !(error instanceof ApiError) ||
    error.status !== 403 ||
    !error.message.includes("wrong_district") ||
    item.entity !== "patient" ||
    session?.user.role !== "chv"
  )
    return false
  const district = session.user.district
  const payload = item.payload as AnyRecord & { district?: string }
  if (!district || payload.district === district) return false
  item.payload = { ...payload, district }
  await db.outbox.update(item.seq!, { payload: item.payload })
  await db.patients.update(item.entityId, { district })
  return true
}

/** PUT one queued change; once the server has it, take its copy and version and drop the change from the queue. */
async function put(item: OutboxItem, body: AnyRecord) {
  const saved = await api<AnyRecord | undefined>(
    `${PATH[item.entity]}/${item.entityId}`,
    { method: "PUT", body }
  )
  await db.outbox.delete(item.seq!)
  if (saved && typeof saved.version === "number") {
    await adopt(item.entity, saved)
  }
}

/**
 * The server saved it. Later changes to the same record still waiting on this phone are built on
 * the new version. When nothing else is waiting, the server's whole copy replaces the phone's,
 * because it carries what the server filled in (who answered a referral, and when).
 */
async function adopt(entity: Entity, saved: AnyRecord) {
  const waiting = await db.outbox.where("entityId").equals(saved.id).toArray()
  for (const w of waiting) {
    await db.outbox.update(w.seq!, {
      payload: { ...w.payload, version: saved.version } as AnyRecord,
    })
  }
  if (waiting.length === 0) {
    await table(entity).put(clean(saved))
  } else {
    await table(entity).update(saved.id, { version: saved.version })
  }
}

/** The server's copy out of a 409 reply ({ error: "conflict", current }), when the reply has one. */
function conflictCopy(error: unknown): AnyRecord | undefined {
  if (!(error instanceof ApiError) || error.status !== 409) return undefined
  try {
    const body = JSON.parse(error.message) as { current?: AnyRecord }
    return body.current && typeof body.current.version === "number"
      ? body.current
      : undefined
  } catch {
    return undefined
  }
}

/**
 * Someone else changed the record first (docs/sync.md). Returns false: this phone's change did
 * not go through yet.
 *  - patient: nothing is overwritten either way. The server's copy is kept next to this phone's
 *    change and the worker chooses on the Sync page: keepMine() or takeServerCopy().
 *  - appointment, vitals: the server's copy wins and this phone's change is dropped, because a
 *    nurse's answer to a referral must not be undone by an older edit, and a reading is never edited.
 * Each outcome is audited.
 */
async function resolve(item: OutboxItem, current: AnyRecord): Promise<boolean> {
  if (item.entity === "patient") {
    await db.outbox.update(item.seq!, {
      attempts: item.attempts + 1,
      lastError: "conflict",
      serverCopy: current,
    })
    return false
  }
  const patientId = (current as Vitals).patientId
  await table(item.entity).put(clean(current))
  await db.outbox.where("entityId").equals(item.entityId).delete()
  await db.auditEvents.add(
    auditEvent(
      "update",
      item.entity,
      item.entityId,
      patientId,
      "conflict: the server's copy was kept"
    )
  )
  return false
}

/**
 * Review step, choice 1: send this phone's version of the patient on top of the server's. The
 * server's change is replaced by this phone's (the audit trail notes it).
 */
export async function keepMine(seq: number): Promise<void> {
  const item = await db.outbox.get(seq)
  if (!item?.serverCopy) return
  await db.outbox.update(seq, {
    payload: { ...item.payload, version: item.serverCopy.version } as AnyRecord,
    serverCopy: undefined,
    lastError: undefined,
  })
  await db.auditEvents.add(
    auditEvent(
      "update",
      item.entity,
      item.entityId,
      item.entityId,
      "conflict reviewed: this phone's change was kept"
    )
  )
  await syncNow()
}

/** Review step, choice 2: drop this phone's change and take the server's copy. */
export async function takeServerCopy(seq: number): Promise<void> {
  const item = await db.outbox.get(seq)
  if (!item?.serverCopy) return
  await table(item.entity).put(clean(item.serverCopy))
  await db.outbox.delete(seq)
  await db.auditEvents.add(
    auditEvent(
      "update",
      item.entity,
      item.entityId,
      item.entityId,
      "conflict reviewed: the server's copy was kept"
    )
  )
}

// ---------------------------------------------------------------- pulling what others saved

const PULL: {
  entity: Entity
  cursor: "updatedAt" | "createdAt"
}[] = [
  { entity: "patient", cursor: "updatedAt" },
  { entity: "appointment", cursor: "updatedAt" },
  { entity: "vitals", cursor: "createdAt" },
]
const PAGE = 500

/** A JSON null is "nothing here" to the app, which uses undefined. */
function clean<T>(value: T): T {
  if (Array.isArray(value)) return value.map(clean) as T
  if (value && typeof value === "object") {
    return Object.fromEntries(
      Object.entries(value as Record<string, unknown>)
        .filter(([, v]) => v !== null)
        .map(([k, v]) => [k, clean(v)])
    ) as T
  }
  return value
}

function cursorKey(entity: Entity) {
  return `hc.pulled.${getSession()?.user.id ?? "anon"}.${entity}`
}

function readCursor(entity: Entity): string | null {
  try {
    return localStorage.getItem(cursorKey(entity))
  } catch {
    return null
  }
}

function writeCursor(entity: Entity, value: string) {
  try {
    localStorage.setItem(cursorKey(entity), value)
  } catch {
    // the next pull simply starts again from the beginning
  }
}

/**
 * Fetches what other workers saved since this phone last asked: a CHV's referral for the nurse,
 * the nurse's answer for the CHV. A record this phone still has changes waiting for is left
 * alone (its own save, and the conflict rules, come first). Offline or refused is not an error:
 * the next sync tries again.
 *   GET /api/patients | /api/appointments | /api/vitals   ?updatedSince=<time of the last record>
 */
async function pull(): Promise<number> {
  let merged = 0
  // Only the roles that work with patients may read them. The district officer and the auditor
  // would just get a 403 for each request.
  const role = getSession()?.user.role
  if (role !== "chv" && role !== "nurse" && role !== "doctor") return merged
  for (const { entity, cursor } of PULL) {
    try {
      for (;;) {
        const since = readCursor(entity)
        const rows =
          (await api<AnyRecord[] | undefined>(
            `${PATH[entity]}${since ? `?updatedSince=${encodeURIComponent(since)}` : ""}`
          )) ?? []
        for (const row of rows) merged += await merge(entity, row)
        const last = rows.at(-1) as unknown as
          Record<string, string> | undefined
        if (last?.[cursor]) writeCursor(entity, last[cursor])
        if (rows.length < PAGE) break
      }
    } catch {
      return merged // offline, signed out, or not allowed: the next sync tries again
    }
  }
  return merged
}

async function merge(entity: Entity, row: AnyRecord): Promise<number> {
  if ((await db.outbox.where("entityId").equals(row.id).count()) > 0) return 0
  const local = await table(entity).get(row.id)
  if (local && local.version > row.version) return 0
  await table(entity).put(clean(row))
  return local && local.version === row.version ? 0 : 1
}

// ---------------------------------------------------------------- audit trail and files

/**
 * The audit trail goes up in batches after the records it describes. The server appends each
 * event to access_log (hash-chained, append-only) and ignores ids it already has, so a batch
 * resent after a lost reply does no harm.
 *   POST /api/audit-events  { events: AuditEvent[] }  -> 204
 */
async function uploadAudit() {
  for (;;) {
    const batch = await db.auditEvents
      .filter((e) => !e.syncedAt)
      .limit(100)
      .toArray()
    if (batch.length === 0) return
    try {
      await api("/audit-events", {
        method: "POST",
        body: { events: batch.map((e) => ({ ...e, syncedAt: undefined })) },
      })
    } catch {
      return // offline or not deployed yet: the events wait on the phone
    }
    const at = new Date().toISOString()
    await db.auditEvents.bulkUpdate(
      batch.map((e) => ({ key: e.id, changes: { syncedAt: at } }))
    )
  }
}

/**
 * Uploads files after the records they belong to. The API answers with a short-lived S3 link
 * (presigned PUT, SSE-KMS bucket); the file goes straight to S3, never through the API server.
 *   POST /api/media/<id>/upload-url  { kind, mime, size, sha256, patientId, ownerEntity, ownerId }
 *     -> { url, headers? }
 */
async function upload(): Promise<number> {
  let uploaded = 0
  const waiting = await db.media.filter((m) => !m.uploadedAt).toArray()
  for (const m of waiting) {
    try {
      const { url, headers } = await api<{
        url: string
        headers?: Record<string, string>
      }>(`/media/${m.id}/upload-url`, {
        method: "POST",
        body: {
          kind: m.kind,
          mime: m.mime,
          size: m.size,
          sha256: m.sha256,
          patientId: m.patientId,
          ownerEntity: m.ownerEntity,
          ownerId: m.ownerId,
        },
      })
      const res = await fetch(url, {
        method: "PUT",
        headers: { "Content-Type": m.mime, ...headers },
        body: m.blob,
      })
      if (!res.ok) throw new ApiError(res.status, res.statusText)
      await db.media.update(m.id, { uploadedAt: new Date().toISOString() })
      uploaded++
    } catch (error) {
      await db.media.update(m.id, {
        attempts: m.attempts + 1,
        lastError: String(error),
      })
      if (!(error instanceof ApiError)) break // offline: try again next time
    }
  }
  return uploaded
}

/** Files still waiting to upload, kept up to date. */
export function usePendingMedia(): number | undefined {
  return useLiveQuery(() => db.media.filter((m) => !m.uploadedAt).count(), [])
}

/** The changes still waiting, kept up to date. */
export function useOutbox(): OutboxItem[] | undefined {
  return useLiveQuery(() => db.outbox.toArray(), [])
}
