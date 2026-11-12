import { useLiveQuery } from "dexie-react-hooks"
import { useEffect, useMemo } from "react"
import { getSession, isDemoSession } from "@/auth/session"
import { db } from "@/db/local"

/** Longest side of the stored photo: sharp on a 3x phone at 96 px, and only ~20-40 KB. */
const SIZE = 288
const BASE = import.meta.env.VITE_API_BASE_URL ?? ""
const URL_PATH = `${BASE}/api/auth/me/photo`

/** The offline demo has no server account, so its photo stays on the phone. */
function serverToken(): string | null {
  if (isDemoSession()) return null
  return getSession()?.token ?? null
}

async function upload(userId: string, photo: Blob): Promise<boolean> {
  const token = serverToken()
  if (!token || !navigator.onLine) return false
  try {
    const res = await fetch(URL_PATH, {
      method: "PUT",
      headers: {
        Authorization: `Bearer ${token}`,
        "Content-Type": photo.type || "image/jpeg",
      },
      body: photo,
    })
    if (!res.ok) return false
    await db.profilePhotos.update(userId, {
      syncedAt: new Date().toISOString(),
    })
    return true
  } catch {
    return false
  }
}

/**
 * Shrinks the picture on the phone, keeps it here (so it shows offline at once) and sends it to
 * the server. Without a signal it is sent the next time the app opens with one.
 */
export async function saveProfilePhoto(userId: string, file: File) {
  if (!file.type.startsWith("image/")) throw new Error("not an image")
  const { default: compress } = await import("browser-image-compression")
  const photo = await compress(file, {
    maxWidthOrHeight: SIZE,
    maxSizeMB: 0.08,
    fileType: "image/jpeg",
    useWebWorker: true,
  })
  await db.profilePhotos.put({
    userId,
    photo,
    updatedAt: new Date().toISOString(),
  })
  await upload(userId, photo)
}

export async function removeProfilePhoto(userId: string) {
  await db.profilePhotos.delete(userId)
  const token = serverToken()
  if (!token) return
  await fetch(URL_PATH, {
    method: "DELETE",
    headers: { Authorization: `Bearer ${token}` },
  }).catch(() => undefined)
}

const synced = new Set<string>()

/**
 * Once per app start: send a photo taken offline, or fetch the one saved on another phone.
 * The server's copy wins unless this phone has one it has not sent yet.
 */
export async function syncProfilePhoto(userId: string) {
  const token = serverToken()
  if (!token || !navigator.onLine || synced.has(userId)) return
  synced.add(userId)
  const local = await db.profilePhotos.get(userId)
  if (local && !local.syncedAt) {
    if (!(await upload(userId, local.photo))) synced.delete(userId)
    return
  }
  try {
    const res = await fetch(URL_PATH, {
      headers: { Authorization: `Bearer ${token}` },
      cache: "no-cache",
    })
    if (res.status === 404) {
      // Removed on another phone.
      if (local) await db.profilePhotos.delete(userId)
      return
    }
    if (!res.ok) {
      synced.delete(userId)
      return
    }
    const photo = await res.blob()
    const now = new Date().toISOString()
    await db.profilePhotos.put({ userId, photo, updatedAt: now, syncedAt: now })
  } catch {
    synced.delete(userId)
  }
}

/** An object URL for the user's photo (or null), released when it changes or unmounts. */
export function useProfilePhoto(userId: string | undefined): string | null {
  useEffect(() => {
    if (userId) void syncProfilePhoto(userId)
  }, [userId])
  const record = useLiveQuery(
    async () =>
      userId ? ((await db.profilePhotos.get(userId)) ?? null) : null,
    [userId]
  )
  const url = useMemo(
    () => (record ? URL.createObjectURL(record.photo) : null),
    [record]
  )
  useEffect(() => () => void (url && URL.revokeObjectURL(url)), [url])
  return url
}
