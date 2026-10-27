import { afterEach, describe, expect, it, vi } from "vitest"
import { db, newId, saveAndQueue } from "@/db/local"
import type { Vitals } from "@/db/types"
import { syncNow } from "./sync"

function vitals(): Vitals {
  const now = new Date().toISOString()
  return {
    id: newId(),
    patientId: newId(),
    takenAt: now,
    pulse: 70,
    createdAt: now,
    version: 0,
  }
}

afterEach(() => vi.unstubAllGlobals())

describe("saveAndQueue", () => {
  it("saves the record and queues it together", async () => {
    const v = vitals()
    await saveAndQueue("vitals", v)
    expect(await db.vitals.get(v.id)).toEqual(v)
    const [item] = await db.outbox.toArray()
    expect(item).toMatchObject({
      entity: "vitals",
      entityId: v.id,
      op: "create",
      attempts: 0,
    })
  })
})

describe("syncNow", () => {
  it("PUTs each item by id and empties the outbox", async () => {
    const fetch = vi.fn().mockResolvedValue(new Response(null, { status: 204 }))
    vi.stubGlobal("fetch", fetch)
    const v = vitals()
    await saveAndQueue("vitals", v)
    expect(await syncNow()).toEqual({ sent: 1, failed: 0, conflicts: 0 })
    expect(fetch).toHaveBeenCalledWith(
      `/api/vitals/${v.id}`,
      expect.objectContaining({ method: "PUT" })
    )
    expect(await db.outbox.count()).toBe(0)
  })

  it("keeps a conflict for review and carries on", async () => {
    vi.stubGlobal(
      "fetch",
      vi
        .fn()
        .mockResolvedValueOnce(new Response("stale", { status: 409 }))
        .mockResolvedValueOnce(new Response(null, { status: 204 }))
    )
    await saveAndQueue("vitals", vitals())
    await saveAndQueue("vitals", vitals())
    expect(await syncNow()).toEqual({ sent: 1, failed: 0, conflicts: 1 })
    const [left] = await db.outbox.toArray()
    expect(left).toMatchObject({ attempts: 1, lastError: "conflict" })
  })

  it("stops at the first network error and keeps the order", async () => {
    vi.stubGlobal("fetch", vi.fn().mockRejectedValue(new TypeError("offline")))
    await saveAndQueue("vitals", vitals())
    await saveAndQueue("vitals", vitals())
    expect(await syncNow()).toEqual({ sent: 0, failed: 1, conflicts: 0 })
    const items = await db.outbox.orderBy("seq").toArray()
    expect(items.map((i) => i.attempts)).toEqual([1, 0])
  })
})

describe("file uploads", () => {
  it("asks for an upload link, PUTs the file to it, and marks it uploaded", async () => {
    const { saveMedia } = await import("@/lib/media")
    const media = await saveMedia({
      blob: new Blob(["voice"], { type: "audio/webm" }),
      kind: "consent-audio",
      patientId: "p1",
      ownerEntity: "patient",
      ownerId: "p1",
    })
    expect(media.sha256).toMatch(/^[0-9a-f]{64}$/)
    const fetch = vi
      .fn()
      .mockResolvedValueOnce(
        Response.json({ url: "https://bucket.s3/upload?sig=1" })
      )
      .mockResolvedValueOnce(new Response(null, { status: 200 }))
    vi.stubGlobal("fetch", fetch)
    expect(await syncNow()).toMatchObject({ uploaded: 1 })
    expect(fetch).toHaveBeenNthCalledWith(
      1,
      `/api/media/${media.id}/upload-url`,
      expect.objectContaining({ method: "POST" })
    )
    expect(fetch).toHaveBeenNthCalledWith(
      2,
      "https://bucket.s3/upload?sig=1",
      expect.objectContaining({
        method: "PUT",
        headers: { "Content-Type": "audio/webm" },
      })
    )
    expect((await db.media.get(media.id))?.uploadedAt).toBeTruthy()
  })
})
