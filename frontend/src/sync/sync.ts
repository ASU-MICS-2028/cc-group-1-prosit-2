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
  return result
}

/** The changes still waiting, kept up to date. */
export function useOutbox(): OutboxItem[] | undefined {
  return useLiveQuery(() => db.outbox.toArray(), [])
}
