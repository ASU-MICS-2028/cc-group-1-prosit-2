import { afterEach, describe, expect, it, vi } from "vitest"
import { saveSession } from "@/auth/session"
import { db } from "@/db/local"
import { syncProfilePhoto } from "./profilePhoto"

function signIn(token = "real-token", id = "chv-042") {
  saveSession({
    token,
    expiresAt: new Date(Date.now() + 3_600_000).toISOString(),
    user: { id, name: "Akosua Mensah", role: "chv", district: "Shai-Osudoku" },
  })
}

const jpeg = () =>
  new Blob([new Uint8Array([0xff, 0xd8, 0xff])], { type: "image/jpeg" })

afterEach(() => vi.unstubAllGlobals())

describe("profile photo on every phone", () => {
  it("fetches the photo saved on another phone", async () => {
    signIn("t1", "user-a")
    // jsdom's Response can't hold a Blob; this is the part of it the code uses.
    const fetch = vi
      .fn()
      .mockResolvedValue({ ok: true, status: 200, blob: async () => jpeg() })
    vi.stubGlobal("fetch", fetch)

    await syncProfilePhoto("user-a")

    expect(fetch).toHaveBeenCalledWith(
      "/api/auth/me/photo",
      expect.objectContaining({ headers: { Authorization: "Bearer t1" } })
    )
    const saved = await db.profilePhotos.get("user-a")
    // (fake-indexeddb can't keep a Blob's bytes in Node; a real browser does)
    expect(saved?.syncedAt).toBeTruthy()
  })

  it("sends a photo taken offline before fetching anything", async () => {
    signIn("t2", "user-b")
    await db.profilePhotos.put({
      userId: "user-b",
      photo: jpeg(),
      updatedAt: new Date().toISOString(),
    })
    const fetch = vi.fn().mockResolvedValue(new Response(null, { status: 204 }))
    vi.stubGlobal("fetch", fetch)

    await syncProfilePhoto("user-b")

    expect(fetch).toHaveBeenCalledTimes(1)
    expect(fetch.mock.calls[0][1]).toMatchObject({ method: "PUT" })
    expect((await db.profilePhotos.get("user-b"))?.syncedAt).toBeTruthy()
  })

  it("drops the copy here when the photo was removed on another phone", async () => {
    signIn("t3", "user-c")
    const now = new Date().toISOString()
    await db.profilePhotos.put({
      userId: "user-c",
      photo: jpeg(),
      updatedAt: now,
      syncedAt: now,
    })
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue(new Response(null, { status: 404 }))
    )

    await syncProfilePhoto("user-c")

    expect(await db.profilePhotos.get("user-c")).toBeUndefined()
  })

  it("never sends the offline demo's photo to the server", async () => {
    signIn("demo-chv", "user-d")
    const fetch = vi.fn()
    vi.stubGlobal("fetch", fetch)

    await syncProfilePhoto("user-d")

    expect(fetch).not.toHaveBeenCalled()
  })
})
