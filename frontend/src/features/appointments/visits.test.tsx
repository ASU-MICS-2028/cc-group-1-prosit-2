import { screen, waitFor, within } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { describe, expect, it } from "vitest"
import { db } from "@/db/local"
import type { Appointment, Patient } from "@/db/types"
import { renderRoute } from "@/test/renderRoute"

const now = new Date().toISOString()
const patient: Patient = {
  id: "p1",
  givenName: "Kofi",
  familyName: "Asare",
  sex: "male",
  dateOfBirth: "1960-07-01",
  birthDateEstimated: true,
  community: "Dodowa",
  district: "Shai-Osudoku",
  preferredLanguage: "tw",
  consent: { given: true, method: "spoken", at: now },
  createdAt: now,
  updatedAt: now,
  version: 0,
}
const visit = (id: string, hoursAhead: number): Appointment => ({
  id,
  patientId: "p1",
  type: "follow_up",
  facility: `Facility ${id}`,
  reason: "BP check",
  priority: "routine",
  status: "scheduled",
  scheduledAt: new Date(Date.now() + hoursAhead * 3600_000).toISOString(),
  createdAt: now,
  updatedAt: now,
  version: 0,
})

describe("visits", () => {
  it("splits upcoming and overdue, and marks a visit done or missed", async () => {
    const user = userEvent.setup()
    await db.patients.add(patient)
    await db.appointments.bulkAdd([visit("soon", 2), visit("late", -48)])
    renderRoute("/appointments")

    const upcoming = await screen.findAllByRole("listitem")
    expect(upcoming).toHaveLength(1)
    expect(within(upcoming[0]).getByText(/Facility soon/)).toBeInTheDocument()
    await user.click(within(upcoming[0]).getByRole("button", { name: "Done" }))
    await waitFor(async () =>
      expect((await db.appointments.get("soon"))?.status).toBe("completed")
    )
    expect(
      await db.outbox.where("entityId").equals("soon").first()
    ).toMatchObject({
      op: "update",
    })

    await user.click(screen.getByRole("radio", { name: "Overdue (1)" }))
    const late = await screen.findByText(/Facility late/)
    const card = late.closest("li")!
    await user.click(within(card).getByRole("button", { name: "More actions" }))
    await user.click(
      await screen.findByRole("menuitem", { name: "Patient didn't come" })
    )
    await waitFor(async () =>
      expect((await db.appointments.get("late"))?.status).toBe("missed")
    )

    await user.click(screen.getByRole("radio", { name: "Past" }))
    expect(await screen.findByText("Missed")).toBeInTheDocument()
    expect(screen.getByText("Done")).toBeInTheDocument()
  })

  it("reschedules a visit through the booking form", async () => {
    await db.patients.add(patient)
    await db.appointments.add(visit("late", -48))
    renderRoute("/patients/p1/appointments/late/edit")
    expect(
      await screen.findByRole("heading", { name: "Change the visit" })
    ).toBeInTheDocument()
    expect(screen.getByLabelText("Health facility")).toHaveValue(
      "Facility late"
    )
    // an overdue visit needs a new time before it can be saved
    await userEvent.click(screen.getByRole("button", { name: "Save changes" }))
    expect(await screen.findByText("Fill this in.")).toBeInTheDocument()
  })
})
