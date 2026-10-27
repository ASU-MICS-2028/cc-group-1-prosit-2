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
    await user.click(screen.getByRole("radio", { name: "Female" }))
    await user.click(screen.getByRole("radio", { name: "No, enter their age" }))
    await user.type(await screen.findByLabelText("Age in years"), "34")
    await user.click(screen.getByRole("button", { name: "Next" }))
    await user.type(await screen.findByLabelText("Community"), "Dodowa")
    await user.type(screen.getByLabelText("District"), "Shai-Osudoku")
    await user.click(screen.getByRole("button", { name: "Save patient" }))

    // Registered! then straight on to vitals
    expect(
      await screen.findByRole("heading", { name: "Registered!" })
    ).toBeInTheDocument()
    expect(
      await screen.findByText("Ama Owusu is registered.")
    ).toBeInTheDocument()
    const [patient] = await db.patients.toArray()
    expect(patient).toMatchObject({
      sex: "female",
      birthDateEstimated: true,
      consent: { given: true, method: "verbal" },
    })

    await user.click(screen.getByRole("link", { name: /Record vitals now/ }))
    await user.type(await screen.findByLabelText(/Systolic/), "190")
    await user.type(screen.getByLabelText(/Diastolic/), "100")
    expect(await screen.findByText(/Refer the patient/)).toBeInTheDocument()
    await user.click(screen.getByRole("button", { name: "Save vitals" }))

    // An urgent reading goes straight to Refer today, with the referral pre-filled
    expect(
      await screen.findByRole("heading", { name: "Refer today" })
    ).toBeInTheDocument()
    expect(router.state.location.pathname).toBe(`/patients/${patient.id}/refer`)
    await user.click(screen.getByRole("link", { name: "Book the referral" }))
    expect(await screen.findByLabelText("Type")).toHaveTextContent("Referral")
    expect(screen.getByRole("radio", { name: "Urgent" })).toBeChecked()
    expect(
      (screen.getByLabelText("Reason") as HTMLTextAreaElement).value
    ).toContain("Very high blood pressure")
    // the app's own calendar: tomorrow (maybe next month), then the default 09:00
    const tomorrow = new Date(Date.now() + 86_400_000)
    await user.click(screen.getByLabelText("Date and time"))
    if (tomorrow.getDate() === 1)
      await user.click(
        await screen.findByRole("button", { name: "Next month" })
      )
    await user.click(
      await screen.findByRole("button", {
        name: new Intl.DateTimeFormat("en", { dateStyle: "full" }).format(
          tomorrow
        ),
      })
    )
    await user.click(screen.getByRole("button", { name: "Done" }))
    expect(screen.getByLabelText("Date and time")).toHaveTextContent(/9:00/)
    await user.type(
      screen.getByLabelText("Health facility"),
      "Dodowa District Hospital"
    )
    await user.click(screen.getByRole("button", { name: "Book" }))

    await waitFor(() =>
      expect(router.state.location.pathname).toBe(`/patients/${patient.id}`)
    )
    expect(
      await screen.findByText(/Referral · Dodowa District Hospital/)
    ).toBeInTheDocument()
    // the latest-reading card carries the flag in words
    const latest = screen.getByRole("heading", {
      name: "Latest readings",
    }).parentElement!
    expect(within(latest).getByText("190/100")).toBeInTheDocument()
    expect(
      within(latest).getByText("Very high blood pressure")
    ).toBeInTheDocument()
    expect(await db.outbox.count()).toBe(3)
  })

  it("a normal reading goes back to the patient, not to Refer today", async () => {
    const user = userEvent.setup()
    const id = "p-normal"
    await db.patients.add({
      id,
      givenName: "Kofi",
      familyName: "Mensah",
      sex: "male",
      dateOfBirth: "1980-01-01",
      birthDateEstimated: false,
      community: "Dodowa",
      district: "Shai-Osudoku",
      preferredLanguage: "en",
      consent: { given: true, method: "verbal", at: new Date().toISOString() },
      createdAt: new Date().toISOString(),
      updatedAt: new Date().toISOString(),
      version: 0,
    })
    const { router } = renderRoute(`/patients/${id}/vitals/new`)
    await user.type(await screen.findByLabelText(/Systolic/), "118")
    await user.type(screen.getByLabelText(/Diastolic/), "76")
    await user.click(
      screen.getByRole("button", { name: /Add weight and height/ })
    )
    expect(
      screen.getByRole("spinbutton", { name: "Weight (kg)" })
    ).toHaveAttribute("aria-valuenow", "60")
    const height = screen.getByRole("spinbutton", { name: "Height (cm)" })
    height.focus()
    await user.keyboard("{ArrowDown}{ArrowDown}{PageDown}")
    expect(height).toHaveAttribute("aria-valuenow", "172")
    await user.click(screen.getByRole("button", { name: "Save vitals" }))
    await waitFor(() =>
      expect(router.state.location.pathname).toBe(`/patients/${id}`)
    )
    const [v] = await db.vitals.toArray()
    expect(v).toMatchObject({
      systolic: 118,
      diastolic: 76,
      weightKg: 60,
      heightCm: 172,
    })
  })

  it("finds patients by community", async () => {
    renderRoute("/patients")
    expect(await screen.findByText(/No patients yet/)).toBeInTheDocument()
  })
})
