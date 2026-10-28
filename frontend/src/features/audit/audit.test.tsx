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
