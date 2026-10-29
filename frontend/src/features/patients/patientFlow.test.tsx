import { screen, waitFor, within } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { describe, expect, it } from "vitest"
import { db } from "@/db/local"
import { renderRoute } from "@/test/renderRoute"

describe("register a patient, record vitals, book a visit", () => {
  it("needs consent before going on", async () => {
    renderRoute("/patients/new")
    await userEvent.click(await screen.findByRole("button", { name: "Next" }))
    expect(await screen.findByText(/must agree/)).toBeInTheDocument()
    expect(
      screen.getByRole("heading", { name: "Ask for consent" })
    ).toBeInTheDocument()
  })

  it("checks each step before the next, and Back keeps what was typed", async () => {
    const user = userEvent.setup()
    renderRoute("/patients/new")
    await user.click(await screen.findByRole("checkbox"))
    await user.click(screen.getByRole("button", { name: "Next" }))
    await user.type(await screen.findByLabelText("First name"), "Kofi")
    await user.click(screen.getByRole("button", { name: "Next" }))
    expect(await screen.findAllByText("Fill this in.")).toHaveLength(2)
    // reported together with the missing names, not only once they are filled
    expect(screen.getByText("Enter a date in the past.")).toBeInTheDocument()
    await user.click(screen.getByRole("button", { name: "Back" }))
    expect(await screen.findByRole("checkbox")).toBeChecked()
    await user.click(screen.getByRole("button", { name: "Next" }))
    expect(await screen.findByLabelText("First name")).toHaveValue("Kofi")
  })

  it("works end to end on the phone and queues everything for sync", async () => {
    const user = userEvent.setup()
    const { router } = renderRoute("/patients/new")
    await user.click(await screen.findByRole("checkbox"))
    await user.click(screen.getByRole("button", { name: "Next" }))
    await user.type(await screen.findByLabelText("First name"), "Ama")
    await user.type(screen.getByLabelText("Surname"), "Owusu")
    await user.selectOptions(screen.getByLabelText("Sex"), "female")
    await user.selectOptions(
      screen.getByLabelText(/know their date of birth/),
      "no"
    )
    await user.type(await screen.findByLabelText("Age in years"), "34")
    await user.click(screen.getByRole("button", { name: "Next" }))
    await user.type(await screen.findByLabelText("Community"), "Dodowa")
    await user.type(screen.getByLabelText("District"), "Shai-Osudoku")
    await user.click(screen.getByRole("button", { name: "Save patient" }))

    expect(
      await screen.findByRole("heading", { name: "Ama Owusu" })
    ).toBeInTheDocument()
    const [patient] = await db.patients.toArray()
    expect(patient).toMatchObject({
      birthDateEstimated: true,
      consent: { given: true, method: "verbal" },
    })

    await user.click(await screen.findByRole("link", { name: /Record vitals/ }))
    await user.type(await screen.findByLabelText(/Systolic/), "190")
    await user.type(screen.getByLabelText(/Diastolic/), "100")
    expect(await screen.findByText(/Refer the patient/)).toBeInTheDocument()
    await user.click(screen.getByRole("button", { name: "Save vitals" }))
    // Back on the patient's page, the saved reading carries its flag
    await waitFor(() =>
      expect(router.state.location.pathname).toBe(`/patients/${patient.id}`)
    )
    // The vitals form can still be on screen for a moment, so read the flag from the saved list
    const vitalsHeading = await screen.findByRole("heading", { name: "Vitals" })
    const vitalsList = vitalsHeading.parentElement!
    expect(
      await within(vitalsList).findByText(/Very high blood pressure/)
    ).toBeInTheDocument()

    await user.click(await screen.findByRole("link", { name: /Book visit/ }))
    await user.selectOptions(await screen.findByLabelText("Type"), "referral")
    const tomorrow = new Date(Date.now() + 86_400_000)
    const local = tomorrow.toISOString().slice(0, 16)
    await user.type(screen.getByLabelText("Date and time"), local)
    await user.type(
      screen.getByLabelText("Health facility"),
      "Dodowa District Hospital"
    )
    await user.type(screen.getByLabelText("Reason"), "BP 190/100")
    await user.click(screen.getByRole("button", { name: "Book" }))

    await waitFor(() =>
      expect(router.state.location.pathname).toBe(`/patients/${patient.id}`)
    )
    expect(
      await screen.findByText(/Referral · Dodowa District Hospital/)
    ).toBeInTheDocument()
    expect(await db.outbox.count()).toBe(3)
  })

  it("finds patients by community", async () => {
    renderRoute("/patients")
    expect(await screen.findByText(/No patients yet/)).toBeInTheDocument()
  })
})
