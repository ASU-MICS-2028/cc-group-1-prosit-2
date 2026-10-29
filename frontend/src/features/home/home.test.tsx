import { screen, within } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { describe, expect, it } from "vitest"
import { db } from "@/db/local"
import type { Appointment, Patient, Vitals } from "@/db/types"
import { renderRoute } from "@/test/renderRoute"

const now = Date.now()
const D = 86_400_000
const iso = (t: number) => new Date(t).toISOString()
const patient = (id: string, givenName: string): Patient => ({
  id,
  givenName,
  familyName: "Test",
  sex: "female",
  dateOfBirth: "1980-07-01",
  birthDateEstimated: true,
  community: "Dodowa",
  district: "Shai-Osudoku",
  preferredLanguage: "tw",
  consent: { given: true, method: "spoken", at: iso(now) },
  createdAt: iso(now),
  updatedAt: iso(now),
  version: 1,
})
const reading = (
  id: string,
  patientId: string,
  sys: number,
  dia: number,
  temp: number
): Vitals => ({
  id,
  patientId,
  takenAt: iso(now - 3_600_000),
  systolic: sys,
  diastolic: dia,
  temperatureC: temp,
  createdAt: iso(now),
  version: 0,
})
const today = new Date()
today.setHours(0, 0, 0, 0)
const visit = (
  id: string,
  patientId: string,
  at: number,
  status: Appointment["status"]
): Appointment => ({
  id,
  patientId,
  type: "follow_up",
  scheduledAt: iso(at),
  facility: "Dodowa Health Centre",
  reason: "BP review",
  priority: "routine",
  status,
  createdAt: iso(now),
  updatedAt: iso(now),
  version: 0,
})

async function seed() {
  await db.patients.bulkAdd([
    patient("p1", "Ama"),
    patient("p2", "Esi"),
    patient("p3", "Yaa"),
  ])
  await db.vitals.bulkAdd([
    reading("v1", "p1", 186, 118, 37), // refer today
    reading("v2", "p2", 148, 94, 38.4), // high BP + fever
    reading("v3", "p3", 118, 76, 36.8),
  ])
  await db.appointments.bulkAdd([
    visit("a1", "p3", today.getTime() + 8 * 3_600_000, "completed"),
    visit("a2", "p2", today.getTime() + 23 * 3_600_000, "scheduled"),
    visit("a3", "p3", today.getTime() - 2 * D, "scheduled"), // overdue
  ])
}

describe("Home dashboard", () => {
  it("shows today's visits, who to refer, and the conditions to follow", async () => {
    await seed()
    renderRoute("/")
    expect(
      await screen.findByText("1 patient needs referring today")
    ).toBeInTheDocument()
    expect(
      screen.getByRole("img", { name: "1 of 2 visits done" })
    ).toBeInTheDocument()
    const highBp = screen.getByRole("heading", {
      name: "High blood pressure",
    }).parentElement!
    expect(within(highBp).getByText("2")).toBeInTheDocument() // Ama and Esi
    expect(
      within(highBp).getByText("of 3 patients checked")
    ).toBeInTheDocument()
    const fever = screen.getByRole("heading", {
      name: "Fever this week",
    }).parentElement!
    expect(within(fever).getByText("1")).toBeInTheDocument()
  })
})

describe("Patient list", () => {
  it("labels what to act on and filters to it", async () => {
    const user = userEvent.setup()
    await seed()
    renderRoute("/patients")
    const cards = await screen.findAllByRole("listitem")
    // the patient to refer comes first
    expect(within(cards[0]).getByText("Ama Test")).toBeInTheDocument()
    expect(within(cards[0]).getByText("Refer today")).toBeInTheDocument()
    expect(screen.getByText("3 patients on this phone")).toBeInTheDocument()

    await user.click(screen.getByRole("radio", { name: /Visit due/ }))
    const due = await screen.findAllByRole("listitem")
    expect(due.map((li) => within(li).getByText(/Test$/).textContent)).toEqual([
      "Yaa Test", // overdue before today
      "Esi Test",
    ])
    expect(within(due[0]).getByText("Visit overdue")).toBeInTheDocument()
  })
})
