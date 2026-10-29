import { useLiveQuery } from "dexie-react-hooks"
import { db } from "@/db/local"

/** Patients on this phone, newest change first, filtered by name, community or phone. */
export function usePatients(search = "") {
  return useLiveQuery(async () => {
    const all = await db.patients.orderBy("updatedAt").reverse().toArray()
    const q = search.trim().toLowerCase()
    if (!q) return all
    return all.filter((p) =>
      [p.givenName, p.familyName, p.community, p.phone ?? ""].some((v) =>
        v.toLowerCase().includes(q)
      )
    )
  }, [search])
}

/** The patient, `null` when there is none with this id, `undefined` while loading. */
export function usePatient(id: string | undefined) {
  return useLiveQuery(
    async () => (id ? ((await db.patients.get(id)) ?? null) : null),
    [id]
  )
}

export function fullName(p: { givenName: string; familyName: string }) {
  return `${p.givenName} ${p.familyName}`
}
