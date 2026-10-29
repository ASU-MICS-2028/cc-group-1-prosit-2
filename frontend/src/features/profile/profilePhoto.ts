import { useLiveQuery } from "dexie-react-hooks"
import { useEffect, useMemo } from "react"
import { db } from "@/db/local"

/** Longest side of the stored photo: sharp on a 3x phone at 96 px, and only ~20-40 KB. */
const SIZE = 288

/**
 * Shrinks the picture on the phone before saving it (no upload; it never leaves the device for
 * now). The compression library is loaded only when someone actually picks a photo.
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
}

export async function removeProfilePhoto(userId: string) {
  await db.profilePhotos.delete(userId)
}

/** An object URL for the user's photo (or null), released when it changes or unmounts. */
export function useProfilePhoto(userId: string | undefined): string | null {
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
