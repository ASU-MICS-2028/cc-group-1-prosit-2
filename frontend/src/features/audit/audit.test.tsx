import { screen, waitFor, within } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { afterEach, describe, expect, it, vi } from "vitest"
import { clearSession } from "@/auth/session"
import { db, saveAndQueue } from "@/db/local"
import type { Patient } from "@/db/types"
import { syncNow } from "@/sync/sync"
import { renderRoute, signIn } from "@/test/renderRoute"

const now = new Date().toISOString()
const ama: Patient = {
  id: "p1",
  givenName: "Ama",
  familyName: "Owusu",
  sex: "female",
  dateOfBirth: "1980-07-01",
  birthDateEstimated: true,
  community: "Dodowa",
  district: "Shai-Osudoku",
  preferredLanguage: "tw",
  consent: { given: true, method: "spoken", at: now },
  createdAt: now,
  updatedAt: now,
  version: 0,
}

afterEach(() => vi.unstubAllGlobals())

describe("audit trail", () => {
  it("records who saved a record, in the same step as the save", async () => {
    signIn("chv")
    await saveAndQueue("patient", ama)
    const [e] = await db.auditEvents.toArray()
    expect(e).toMatchObject({
      action: "create",
      entity: "patient",
      patientId: "p1",
      actorId: "test-chv",
      role: "chv",
      outcome: "success",
      purpose: "TREAT",
    })
  })

  it("records opening a record once, and shows it on the record", async () => {
    await db.patients.add(ama)
    renderRoute("/patients/p1", { as: "nurse" })
    expect(
      await screen.findByRole("heading", { name: "Who opened this record" })
    ).toBeInTheDocument()
    const views = await db.auditEvents.where("action").equals("view").toArray()
    expect(views).toHaveLength(1)
    expect(views[0]).toMatchObject({ patientId: "p1", role: "nurse" })
  })

  it("records a refused page, and the auditor sees it under Refused", async () => {
    const user = userEvent.setup()
    // a CHV tries the district summary
    const { router, unmount } = renderRoute("/surveillance", { as: "chv" })
    await waitFor(() => expect(router.state.location.pathname).toBe("/"))
    unmount()
    clearSession()
    const [denied] = await db.auditEvents
      .where("action")
      .equals("denied")
      .toArray()
    expect(denied).toMatchObject({
      outcome: "denied",
      detail: "/surveillance",
      role: "chv",
    })

    renderRoute("/audit", { as: "admin" })
    expect(
      await screen.findByRole("heading", { name: "Activity log" })
    ).toBeInTheDocument()
    await user.click(screen.getByRole("radio", { name: "Refused" }))
    const row = await screen.findByText(/was refused/)
    expect(
      within(row.closest("li")!).getByText(/the page \/surveillance/)
    ).toBeInTheDocument()
  })

  it("uploads the trail in a batch and marks it sent", async () => {
    signIn("chv")
    await saveAndQueue("patient", ama)
    const fetch = vi.fn().mockResolvedValue(new Response(null, { status: 204 }))
    vi.stubGlobal("fetch", fetch)
    await syncNow()
    const call = fetch.mock.calls.find(([url]) => url === "/api/audit-events")!
    expect(JSON.parse(call[1].body).events).toHaveLength(1)
    expect((await db.auditEvents.toArray())[0].syncedAt).toBeTruthy()
  })
})

describe("server log on the audit page", () => {
  it("shows the server's entries including sign-ins, and says the chain is intact", async () => {
    const entry = (id: number, extra: object) => ({
      id,
      actorId: "chv-042",
      role: "chv",
      action: "read",
      outcome: "success",
      purpose: "HOPERAT",
      entity: "sign-in",
      entityId: "chv-042",
      patientId: null,
      detail: "signed in",
      recordedAt: now,
      ...extra,
    })
    vi.stubGlobal(
      "fetch",
      vi.fn().mockImplementation((url: string) =>
        Promise.resolve(
          url.startsWith("/api/audit-events/verify")
            ? Response.json({
                intact: true,
                entriesChecked: 2,
                firstBadId: null,
              })
            : Response.json([
                entry(2, { outcome: "denied", detail: "refused: wrong code" }),
                entry(1, {}),
              ])
        )
      )
    )

    renderRoute("/audit", { as: "admin" })

    expect(
      await screen.findByRole("heading", { name: "Server log" })
    ).toBeInTheDocument()
    expect(
      await screen.findByText("Checked: 2 entries, none changed.")
    ).toBeInTheDocument()
    expect(
      await screen.findByText(/was refused at sign-in/)
    ).toBeInTheDocument()
    expect(screen.getByText(/signed in$/)).toBeInTheDocument()
  })

  it("warns when the server says the chain does not check out", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockImplementation((url: string) =>
        Promise.resolve(
          url.startsWith("/api/audit-events/verify")
            ? Response.json({
                intact: false,
                entriesChecked: 5,
                firstBadId: 3,
              })
            : Response.json([])
        )
      )
    )

    renderRoute("/audit", { as: "admin" })

    expect(
      await screen.findByText(/entry 3 does not match the chain/)
    ).toBeInTheDocument()
    expect(
      screen.getByText("The server has no entries yet.")
    ).toBeInTheDocument()
  })

  it("says so when the server cannot be reached", async () => {
    vi.stubGlobal("fetch", vi.fn().mockRejectedValue(new TypeError("offline")))

    renderRoute("/audit", { as: "admin" })

    expect(
      await screen.findByText(
        "Connect to the internet to read the server's log."
      )
    ).toBeInTheDocument()
  })
})

describe("conflict review on the Sync page", () => {
  it("shows what differs and lets the worker keep the server's copy", async () => {
    const user = userEvent.setup()
    const mine = { ...ama, community: "Ayikuma", version: 3 }
    await db.patients.put(mine)
    await db.outbox.add({
      entity: "patient",
      entityId: ama.id,
      op: "update",
      payload: mine,
      queuedAt: now,
      attempts: 1,
      lastError: "conflict",
      serverCopy: { ...ama, community: "Doryumu", version: 4 },
    })

    renderRoute("/sync", { as: "chv" })

    const card = await screen.findByTestId("conflict-review")
    expect(
      within(card).getByText(/Someone else changed this patient: Ama Owusu/)
    ).toBeInTheDocument()
    expect(within(card).getByText("Ayikuma")).toBeInTheDocument()
    expect(within(card).getByText("Doryumu")).toBeInTheDocument()
    await user.click(
      within(card).getByRole("button", { name: "Use the server's" })
    )
    await waitFor(async () => expect(await db.outbox.count()).toBe(0))
    expect((await db.patients.get(ama.id))?.community).toBe("Doryumu")
  })
})
