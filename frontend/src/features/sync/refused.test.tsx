import { screen, waitFor } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { afterEach, describe, expect, it, vi } from "vitest"
import { db } from "@/db/local"
import type { Patient, Vitals } from "@/db/types"
import { renderRoute } from "@/test/renderRoute"

const now = new Date().toISOString()
const patient: Patient = {
  id: "p1",
  givenName: "Kofi",
  familyName: "Mensah",
  sex: "male",
  dateOfBirth: "1980-01-01",
  birthDateEstimated: false,
  community: "Dodowa",
  district: "Shai-Osudoku",
  preferredLanguage: "en",
  consent: { given: true, method: "spoken", at: now },
  createdAt: now,
  updatedAt: now,
  version: 0,
}

afterEach(() => vi.unstubAllGlobals())

describe("a change the server refuses for good", () => {
  it("shows the server's reason and can be discarded", async () => {
    const user = userEvent.setup()
    const reading: Vitals = {
      id: "v1",
      patientId: "p1",
      takenAt: now,
      systolic: 80,
      diastolic: 120,
      createdAt: now,
      version: 0,
    }
    // The app syncs on its own while the page is open: the server keeps refusing it the same way.
    vi.stubGlobal(
      "fetch",
      vi.fn(async (_url: string, init?: RequestInit) =>
        init?.method === "PUT"
          ? Response.json(
              {
                errors: {
                  systolic:
                    "The top number must be higher than the bottom number.",
                },
              },
              { status: 422 }
            )
          : Response.json([])
      )
    )
    await db.vitals.add(reading)
    await db.outbox.add({
      entity: "vitals",
      entityId: "v1",
      op: "create",
      payload: reading,
      queuedAt: now,
      attempts: 3,
      lastError:
        '422 {"errors":{"systolic":"The top number must be higher than the bottom number."}}',
    })
    renderRoute("/sync")

    expect(
      await screen.findByText(
        /The top number must be higher than the bottom number/
      )
    ).toBeInTheDocument()
    await user.click(
      screen.getByRole("button", { name: "Discard this reading" })
    )
    await user.click(screen.getByRole("button", { name: "Yes, discard it" }))

    await waitFor(async () => expect(await db.outbox.count()).toBe(0))
    expect(await db.vitals.get("v1")).toBeUndefined()
  })

  it("is not offered for a reading only waiting for its patient", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(async () => new Response(null, { status: 503 }))
    )
    await db.outbox.add({
      entity: "patient",
      entityId: "p1",
      op: "create",
      payload: patient,
      queuedAt: now,
      attempts: 1,
    })
    await db.outbox.add({
      entity: "vitals",
      entityId: "v2",
      op: "create",
      payload: {
        id: "v2",
        patientId: "p1",
        takenAt: now,
        pulse: 70,
        createdAt: now,
        version: 0,
      },
      queuedAt: now,
      attempts: 1,
      lastError:
        '422 {"errors":{"patientId":"This patient is not on the server yet."}}',
    })
    renderRoute("/sync")

    expect(
      await screen.findByText(/Waiting for its patient/)
    ).toBeInTheDocument()
    expect(
      screen.queryByRole("button", { name: /Discard/ })
    ).not.toBeInTheDocument()
  })

  it("is offered for a reading whose patient will never arrive", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(async () => new Response(null, { status: 503 }))
    )
    await db.outbox.add({
      entity: "vitals",
      entityId: "v3",
      op: "create",
      payload: {
        id: "v3",
        patientId: "gone",
        takenAt: now,
        pulse: 70,
        createdAt: now,
        version: 0,
      },
      queuedAt: now,
      attempts: 4,
      lastError:
        '422 {"errors":{"patientId":"This patient is not on the server yet."}}',
    })
    renderRoute("/sync")

    expect(
      await screen.findByText(/never reached the server/)
    ).toBeInTheDocument()
    expect(
      screen.getByRole("button", { name: "Discard this reading" })
    ).toBeInTheDocument()
  })
})

describe("the vitals form", () => {
  it("won't save blood pressure the wrong way round", async () => {
    const user = userEvent.setup()
    await db.patients.add(patient)
    renderRoute("/patients/p1/vitals/new")

    await user.type(await screen.findByLabelText(/Systolic/), "80")
    await user.type(screen.getByLabelText(/Diastolic/), "120")
    await user.click(screen.getByRole("button", { name: "Save vitals" }))

    expect(
      await screen.findByText(
        "The top number must be higher than the bottom number."
      )
    ).toBeInTheDocument()
    expect(await db.vitals.count()).toBe(0)
  })
})
