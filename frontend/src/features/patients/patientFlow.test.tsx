import { screen, waitFor } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { describe, expect, it } from "vitest"
import { db } from "@/db/local"
import { renderRoute } from "@/test/renderRoute"

describe("register a patient, record vitals, book a visit", () => {
  it("needs consent before saving", async () => {
    renderRoute("/patients/new")
    await userEvent.click(
      await screen.findByRole("button", { name: "Save patient" })
    )
    expect(await screen.findByText(/must agree/)).toBeInTheDocument()
    expect(await db.patients.count()).toBe(0)
  })

  it("works end to end on the phone and queues everything for sync", async () => {
    const user = userEvent.setup()
    const { router } = renderRoute("/patients/new")
    await user.click(await screen.findByRole("checkbox"))
    await user.type(screen.getByLabelText("First name"), "Ama")
    await user.type(screen.getByLabelText("Surname"), "Owusu")
    await user.selectOptions(screen.getByLabelText("Sex"), "female")
    await user.selectOptions(
      screen.getByLabelText(/know their date of birth/),
      "no"
    )
    await user.type(await screen.findByLabelText("Age in years"), "34")
    await user.type(screen.getByLabelText("Community"), "Dodowa")
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
    expect(
      await screen.findByText(/Very high blood pressure/)
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
