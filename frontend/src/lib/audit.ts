import { useLiveQuery } from "dexie-react-hooks"
import { auditEvent, db } from "@/db/local"
import type { AuditAction, AuditEvent, Entity } from "@/db/types"

/** Record an access that isn't a save (saves are recorded by saveAndQueue). Never throws. */
export async function logAccess(
  action: AuditAction,
  entity: Entity | "page",
  ids: { entityId?: string; patientId?: string; detail?: string } = {}
) {
  try {
    await db.auditEvents.add(
      auditEvent(action, entity, ids.entityId, ids.patientId, ids.detail)
    )
  } catch {
    // the audit trail must never block care; a failure here is visible as a gap on the server
  }
}

/** The audit trail on this phone, newest first; optionally one patient's. */
export function useAuditEvents(patientId?: string, limit = 500) {
  return useLiveQuery(
    () =>
      (patientId
        ? db.auditEvents.where("patientId").equals(patientId)
        : db.auditEvents.toCollection()
      )
        .reverse()
        .sortBy("at")
        .then((rows: AuditEvent[]) => rows.slice(0, limit)),
    [patientId, limit]
  )
}
