import { useLiveQuery } from "dexie-react-hooks"
import { useEffect, useMemo } from "react"
import { db, newId } from "@/db/local"
import type { Entity, Media, MediaKind } from "@/db/types"

/** Hex SHA-256 of a file, kept with the record so a changed file can be detected. */
export async function sha256(blob: Blob): Promise<string> {
  const digest = await crypto.subtle.digest("SHA-256", await blob.arrayBuffer())
  return Array.from(new Uint8Array(digest), (b) =>
    b.toString(16).padStart(2, "0")
  ).join("")
}

/**
 * Shrinks a camera photo on the phone: big enough to read a thumbprint or see a wound, small
 * enough (~100-200 KB) to upload on 2G. The library is loaded only when a photo is taken.
 */
export async function compressPhoto(file: Blob, longestSide = 1280) {
  const { default: compress } = await import("browser-image-compression")
  return compress(file as File, {
    maxWidthOrHeight: longestSide,
    maxSizeMB: 0.2,
    fileType: "image/jpeg",
    useWebWorker: true,
  })
}

/** Store a file for a record and return it; the sync uploads it later. */
export async function saveMedia(input: {
  blob: Blob
  kind: MediaKind
  patientId: string
  ownerEntity: Entity
  ownerId: string
}): Promise<Media> {
  const media: Media = {
    id: newId(),
    ...input,
    mime: input.blob.type || "application/octet-stream",
    size: input.blob.size,
    sha256: await sha256(input.blob),
    createdAt: new Date().toISOString(),
    attempts: 0,
  }
  await db.media.add(media)
  return media
}

/** An object URL for a Blob, released when it changes or unmounts. */
export function useObjectUrl(blob: Blob | undefined | null): string | null {
  const url = useMemo(() => (blob ? URL.createObjectURL(blob) : null), [blob])
  useEffect(() => () => void (url && URL.revokeObjectURL(url)), [url])
  return url
}

/** The files that belong to one record, oldest first. */
export function useMediaFor(ownerId: string | undefined) {
  return useLiveQuery(
    () =>
      ownerId
        ? db.media.where("ownerId").equals(ownerId).sortBy("createdAt")
        : [],
    [ownerId]
  )
}
