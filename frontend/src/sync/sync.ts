import { useLiveQuery } from "dexie-react-hooks"
import { ApiError, api } from "@/api/client"
import { db } from "@/db/local"
import type { Entity, OutboxItem } from "@/db/types"

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
}

let running: Promise<SyncResult> | null = null

/**
 * Sends the outbox to the API, oldest first. Ids are made on the phone, so a create is a PUT to
 * /<entity>/<id>: sending the same item twice (lost reply on 2G) does no harm. A 409 is a
 * conflict: the item is kept and marked for Liza's conflict policy instead of overwriting.
 */
export function syncNow(): Promise<SyncResult> {
  running ??= send().finally(() => {
    running = null
  })
  return running
}

async function send(): Promise<SyncResult> {
  const result: SyncResult = { sent: 0, failed: 0, conflicts: 0 }
  const items = await db.outbox.orderBy("seq").toArray()
  for (const item of items) {
    try {
      await api(`${PATH[item.entity]}/${item.entityId}`, {
        method: "PUT",
        body: item.payload,
      })
      await db.outbox.delete(item.seq!)
      result.sent++
    } catch (error) {
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
  }
  return result
}

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
