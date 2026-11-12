import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"
import { db, newId, saveAndQueue } from "@/db/local"
import type { Appointment, Patient, Vitals } from "@/db/types"
import { ApiError, api } from "@/api/client"
import { getSession, saveSession } from "@/auth/session"
import { signIn } from "@/test/renderRoute"
import { keepMine, syncNow, takeServerCopy } from "./sync"

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

function appointment(patientId = newId()): Appointment {
  const now = new Date().toISOString()
  return {
    id: newId(),
    patientId,
    type: "referral",
    scheduledAt: now,
    facility: "Dodowa District Hospital",
    reason: "High BP",
    priority: "urgent",
    status: "scheduled",
    referralStatus: "requested",
    createdAt: now,
    updatedAt: now,
    version: 0,
  }
}

function patient(): Patient {
  const now = new Date().toISOString()
  return {
    id: newId(),
    givenName: "Ama",
    familyName: "Owusu",
    sex: "female",
    dateOfBirth: "1966-07-01",
    birthDateEstimated: true,
    community: "Dodowa",
    district: "Shai-Osudoku",
    preferredLanguage: "tw",
    consent: { given: true, method: "spoken", at: now },
    createdAt: now,
    updatedAt: now,
    version: 0,
  }
}

describe("versions", () => {
  it("keeps the version the server answers with and builds queued changes on it", async () => {
    const a = appointment()
    await saveAndQueue("appointment", a)
    await saveAndQueue("appointment", { ...a, reason: "Edited" }, "update")
    const fetch = vi
      .fn()
      .mockResolvedValueOnce(
        Response.json({ ...a, version: 1 }, { status: 201 })
      )
      .mockResolvedValue(Response.json({ ...a, reason: "Edited", version: 2 }))
    vi.stubGlobal("fetch", fetch)

    await syncNow()

    const bodies = fetch.mock.calls
      .filter(([, init]) => init?.method === "PUT")
      .map(([, init]) => JSON.parse(init.body as string))
    expect(bodies.map((b) => b.version)).toEqual([0, 1])
    expect((await db.appointments.get(a.id))?.version).toBe(2)
    expect(await db.outbox.count()).toBe(0)
  })
})

describe("conflicts", () => {
  const conflict = (current: object) =>
    new Response(JSON.stringify({ error: "conflict", current }), {
      status: 409,
    })

  it("keeps the server's copy of an appointment and drops the phone's change", async () => {
    const a = appointment()
    await db.appointments.put(a)
    await saveAndQueue("appointment", { ...a, reason: "Phone edit" }, "update")
    const theirs = {
      ...a,
      referralStatus: "accepted",
      handledBy: "nurse-17",
      version: 3,
    }
    vi.stubGlobal("fetch", vi.fn().mockResolvedValueOnce(conflict(theirs)))

    expect(await syncNow()).toMatchObject({ sent: 0, conflicts: 1, failed: 0 })

    const kept = await db.appointments.get(a.id)
    expect(kept).toMatchObject({ referralStatus: "accepted", version: 3 })
    expect(await db.outbox.count()).toBe(0)
    expect(
      (await db.auditEvents.toArray()).some((e) =>
        e.detail?.includes("server's copy")
      )
    ).toBe(true)
  })

  async function heldPatientConflict() {
    const p = patient()
    await db.patients.put(p)
    await saveAndQueue("patient", { ...p, community: "Ayikuma" }, "update")
    vi.stubGlobal(
      "fetch",
      vi
        .fn()
        .mockResolvedValueOnce(
          conflict({ ...p, community: "Doryumu", version: 4 })
        )
        .mockResolvedValue(Response.json([]))
    )
    expect(await syncNow()).toMatchObject({ sent: 0, conflicts: 1 })
    return p
  }

  it("holds a patient's change for review and overwrites nothing", async () => {
    const p = await heldPatientConflict()

    const [held] = await db.outbox.toArray()
    expect(held).toMatchObject({ lastError: "conflict", attempts: 1 })
    expect(held.serverCopy).toMatchObject({ community: "Doryumu", version: 4 })
    expect(await db.patients.get(p.id)).toMatchObject({ community: "Ayikuma" })
  })

  it("keeps the phone's version on request: sends it on top of the server's version", async () => {
    const p = await heldPatientConflict()
    const [held] = await db.outbox.toArray()
    const fetch = vi
      .fn()
      .mockResolvedValueOnce(
        Response.json({ ...p, community: "Ayikuma", version: 5 })
      )
      .mockResolvedValue(Response.json([]))
    vi.stubGlobal("fetch", fetch)

    await keepMine(held.seq!)

    const sent = JSON.parse(fetch.mock.calls[0][1].body as string)
    expect(sent).toMatchObject({ community: "Ayikuma", version: 4 })
    expect(await db.outbox.count()).toBe(0)
    expect(await db.patients.get(p.id)).toMatchObject({
      community: "Ayikuma",
      version: 5,
    })
    expect(
      (await db.auditEvents.toArray()).some((e) =>
        e.detail?.includes("this phone's change was kept")
      )
    ).toBe(true)
  })

  it("takes the server's copy on request and drops the phone's change", async () => {
    const p = await heldPatientConflict()
    const [held] = await db.outbox.toArray()

    await takeServerCopy(held.seq!)

    expect(await db.outbox.count()).toBe(0)
    expect(await db.patients.get(p.id)).toMatchObject({
      community: "Doryumu",
      version: 4,
    })
    expect(
      (await db.auditEvents.toArray()).some((e) =>
        e.detail?.includes("the server's copy was kept")
      )
    ).toBe(true)
  })

  it("does nothing for an item that is not waiting for review", async () => {
    await saveAndQueue("vitals", vitals())
    const [item] = await db.outbox.toArray()

    await keepMine(item.seq!)
    await takeServerCopy(item.seq!)
    await keepMine(9999)

    expect(await db.outbox.count()).toBe(1)
  })
})

describe("pulling", () => {
  beforeEach(() => signIn("nurse"))

  it("stores what other workers saved, without nulls, and only asks for newer ones next time", async () => {
    const a = {
      ...appointment(),
      handledBy: null,
      referralStatus: "accepted",
      version: 2,
    }
    const fetch = vi.fn().mockImplementation((url: string) => {
      if (url.startsWith("/api/appointments"))
        return Promise.resolve(Response.json([a]))
      return Promise.resolve(Response.json([]))
    })
    vi.stubGlobal("fetch", fetch)

    expect(await syncNow()).toMatchObject({ pulled: 1 })

    const stored = await db.appointments.get(a.id)
    expect(stored).toMatchObject({ referralStatus: "accepted", version: 2 })
    expect(stored).not.toHaveProperty("handledBy")

    await syncNow()
    const asked = fetch.mock.calls
      .map(([url]) => url as string)
      .filter((u) => u.startsWith("/api/appointments"))
    expect(asked[0]).toBe("/api/appointments")
    expect(asked[1]).toBe(
      `/api/appointments?updatedSince=${encodeURIComponent(a.updatedAt)}`
    )
  })

  it("leaves a record alone while this phone still has a change waiting for it", async () => {
    const a = appointment()
    await db.appointments.put(a)
    await saveAndQueue("appointment", { ...a, reason: "Mine" }, "update")
    const fetch = vi
      .fn()
      .mockImplementation((_url: string, init?: RequestInit) => {
        if (init?.method === "PUT")
          return Promise.reject(new TypeError("offline"))
        return Promise.resolve(Response.json([]))
      })
    vi.stubGlobal("fetch", fetch)
    // offline: nothing is pulled either, and nothing is overwritten
    await syncNow()
    expect((await db.appointments.get(a.id))?.reason).toBe("Mine")
  })

  it("does not overwrite a newer local copy and stops quietly when offline", async () => {
    const a = { ...appointment(), version: 5 }
    await db.appointments.put(a)
    vi.stubGlobal(
      "fetch",
      vi
        .fn()
        .mockImplementation((url: string) =>
          url.startsWith("/api/appointments")
            ? Promise.resolve(
                Response.json([{ ...a, reason: "Older", version: 4 }])
              )
            : Promise.reject(new TypeError("offline"))
        )
    )

    const result = await syncNow()

    expect(result.pulled).toBeUndefined()
    expect((await db.appointments.get(a.id))?.reason).toBe(a.reason)
  })
})

describe("audit upload", () => {
  it("sends the events inside { events } and marks them as sent", async () => {
    const v = vitals()
    await saveAndQueue("vitals", v)
    const fetch = vi
      .fn()
      .mockImplementation((url: string) =>
        Promise.resolve(
          url === "/api/audit-events"
            ? new Response(null, { status: 204 })
            : Response.json([])
        )
      )
    vi.stubGlobal("fetch", fetch)

    await syncNow()

    const call = fetch.mock.calls.find(([url]) => url === "/api/audit-events")!
    const body = JSON.parse(call[1].body as string)
    expect(body.events[0]).toMatchObject({ entity: "vitals", action: "create" })
    expect((await db.auditEvents.toArray()).every((e) => e.syncedAt)).toBe(true)
  })
})

describe("demo sessions", () => {
  it("never ask the server and keep everything on the phone", async () => {
    saveSession({
      token: "demo-chv-042",
      expiresAt: new Date(Date.now() + 86_400_000).toISOString(),
      user: {
        id: "chv-042",
        name: "Akosua Mensah",
        role: "chv",
        district: "Shai-Osudoku",
      },
    })
    const fetch = vi.fn().mockResolvedValue(new Response(null, { status: 401 }))
    vi.stubGlobal("fetch", fetch)
    await saveAndQueue("vitals", vitals())

    expect(await syncNow()).toEqual({ sent: 0, failed: 0, conflicts: 0 })

    expect(fetch).not.toHaveBeenCalled()
    expect(await db.outbox.count()).toBe(1)
    expect(getSession()).not.toBeNull()
  })

  it("are not signed out by a 401 from the server", async () => {
    saveSession({
      token: "demo-chv-042",
      expiresAt: new Date(Date.now() + 86_400_000).toISOString(),
      user: {
        id: "chv-042",
        name: "Akosua Mensah",
        role: "chv",
        district: "Shai-Osudoku",
      },
    })
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue(new Response("", { status: 401 }))
    )

    await expect(api("/patients")).rejects.toBeInstanceOf(ApiError)

    expect(getSession()).not.toBeNull()
  })

  it("a real session is still signed out by a 401", async () => {
    signIn("chv")
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue(new Response("", { status: 401 }))
    )

    await expect(api("/patients")).rejects.toBeInstanceOf(ApiError)

    expect(getSession()).toBeNull()
  })
})

describe("pulling by role", () => {
  it.each(["district_officer", "admin"] as const)(
    "does not ask for patient records as the %s, who may not read them",
    async (role) => {
      signIn(role)
      const fetch = vi
        .fn()
        .mockImplementation(() => Promise.resolve(Response.json([])))
      vi.stubGlobal("fetch", fetch)

      await syncNow()

      const asked = fetch.mock.calls.map(([url]) => url as string)
      expect(
        asked.filter((u) => /\/api\/(patients|vitals|appointments)/.test(u))
      ).toEqual([])
    }
  )

  it.each(["chv", "nurse", "doctor"] as const)(
    "still pulls as the %s",
    async (role) => {
      signIn(role)
      const fetch = vi
        .fn()
        .mockImplementation(() => Promise.resolve(Response.json([])))
      vi.stubGlobal("fetch", fetch)

      await syncNow()

      const asked = fetch.mock.calls.map(([url]) => url as string)
      expect(asked).toEqual(
        expect.arrayContaining([
          "/api/patients",
          "/api/appointments",
          "/api/vitals",
        ])
      )
    }
  )
})

describe("a patient typed in with another district", () => {
  it("is moved to the CHV's own district and sent again, and the queue behind it goes too", async () => {
    signIn("chv") // district Shai-Osudoku
    const now = new Date().toISOString()
    const p: Patient = {
      id: newId(),
      givenName: "Ama",
      familyName: "Owusu",
      sex: "female",
      dateOfBirth: "1990-01-01",
      birthDateEstimated: false,
      community: "Dodowa",
      district: "Dodowa", // the old free-text box
      preferredLanguage: "tw",
      consent: { given: true, method: "spoken", at: now },
      createdAt: now,
      updatedAt: now,
      version: 0,
    }
    await saveAndQueue("patient", p)
    const v = { ...vitals(), patientId: p.id }
    await saveAndQueue("vitals", v)
    const sent: { url: string; district?: string }[] = []
    vi.stubGlobal(
      "fetch",
      vi.fn(async (url: string, init?: RequestInit) => {
        const body = init?.body ? JSON.parse(String(init.body)) : {}
        if (init?.method === "PUT") sent.push({ url, district: body.district })
        if (url.includes("/patients/") && body.district !== "Shai-Osudoku")
          return Response.json({ error: "wrong_district" }, { status: 403 })
        if (init?.method === "PUT") return new Response(null, { status: 204 })
        return Response.json([])
      })
    )

    const result = await syncNow()

    expect(result.failed).toBe(0)
    expect(await db.outbox.count()).toBe(0)
    expect((await db.patients.get(p.id))?.district).toBe("Shai-Osudoku")
    expect(sent.map((s) => s.district ?? "vitals")).toEqual([
      "Dodowa",
      "Shai-Osudoku",
      "vitals",
    ])
  })
})
