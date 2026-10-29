import { screen, within } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { describe, expect, it } from "vitest"
import { db } from "@/db/local"
import type { Appointment, Patient } from "@/db/types"
import { renderRoute } from "@/test/renderRoute"

const now = new Date().toISOString()
const patient: Patient = {
  id: "p1",
  givenName: "Ama",
  familyName: "Owusu",
  sex: "female",
  dateOfBirth: "1972-07-01",
  birthDateEstimated: true,
  community: "Dodowa",
  district: "Shai-Osudoku",
  preferredLanguage: "tw",
  consent: { given: true, method: "verbal", at: now },
  createdAt: now,
  updatedAt: now,
  version: 0,
}
const referral = (
  id: string,
  priority: Appointment["priority"],
  hoursAhead: number
): Appointment => ({
  id,
  patientId: "p1",
  type: "referral",
  facility: "Dodowa District Hospital",
  reason: `reason ${id}`,
  priority,
  status: "scheduled",
  referralStatus: "requested",
  scheduledAt: new Date(Date.now() + hoursAhead * 3600_000).toISOString(),
  createdAt: now,
  updatedAt: now,
  version: 0,
})

describe("referral inbox", () => {
  it("lists emergencies first, and a nurse accepts then marks a referral seen", async () => {
    const user = userEvent.setup()
    await db.patients.add(patient)
    await db.appointments.bulkAdd([
      referral("r-routine", "routine", 1),
      referral("r-emergency", "emergency", 5),
    ])
    renderRoute("/referrals", { as: "nurse" })

    const cards = await screen.findAllByRole("listitem")
    expect(within(cards[0]).getByText("reason r-emergency")).toBeInTheDocument()
    expect(within(cards[0]).getByText("Waiting")).toBeInTheDocument()

    await user.click(within(cards[0]).getByRole("button", { name: /Accept/ }))
    expect(await within(cards[0]).findByText("Accepted")).toBeInTheDocument()
    await user.click(
      within(cards[0]).getByRole("button", { name: /Mark as seen/ })
    )
    // moves to Done
    expect(
      await screen.findByRole("tab", { name: "Done (1)" })
    ).toBeInTheDocument()
    const saved = await db.appointments.get("r-emergency")
    expect(saved).toMatchObject({
      referralStatus: "seen",
      status: "completed",
      handledBy: "test-nurse",
    })
    expect(await db.outbox.where("entity").equals("appointment").count()).toBe(
      2
    )
  })

  it("is not open to CHVs", async () => {
    const { router } = renderRoute("/referrals", { as: "chv" })
    await screen.findByText("Register a patient")
    expect(router.state.location.pathname).toBe("/")
  })
})

describe("district summary", () => {
  it("shows counts, hides small cells and raises the fever signal", async () => {
    renderRoute("/surveillance", { as: "district_officer" })
    expect(await screen.findByText(/Demo data/)).toBeInTheDocument()
    expect(screen.getByRole("alert")).toHaveTextContent(
      "Fever rising in Kintampo North"
    )
    const table = screen.getByRole("table")
    const small = within(table).getByText("Sawla-Tuna-Kalba").closest("tr")!
    expect(within(small).getAllByText("<5")).toHaveLength(3)
    await userEvent.click(screen.getByRole("tab", { name: "Month" }))
    expect(screen.getByRole("tab", { name: "Month" })).toHaveAttribute(
      "aria-selected",
      "true"
    )
  })
})
