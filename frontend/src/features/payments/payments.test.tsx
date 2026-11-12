import { screen } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { afterEach, describe, expect, it, vi } from "vitest"
import { db } from "@/db/local"
import type { Appointment, Patient } from "@/db/types"
import { renderRoute } from "@/test/renderRoute"

const now = new Date().toISOString()
const patient: Patient = {
  id: "p1",
  givenName: "Abena",
  familyName: "Owusu",
  sex: "female",
  dateOfBirth: "1990-01-01",
  birthDateEstimated: false,
  community: "Dodowa",
  district: "Shai-Osudoku",
  preferredLanguage: "tw",
  consent: { given: true, method: "spoken", at: now },
  createdAt: now,
  updatedAt: now,
  version: 0,
}
const teleconsult: Appointment = {
  id: "11111111-1111-4111-8111-111111111111",
  patientId: "p1",
  type: "teleconsult",
  facility: "Dodowa District Hospital",
  reason: "Headaches",
  priority: "urgent",
  status: "scheduled",
  referralStatus: "accepted",
  scheduledAt: new Date(Date.now() + 3600_000).toISOString(),
  createdAt: now,
  updatedAt: now,
  version: 0,
}

/** The API as these screens see it: one fee status, and a checkout that hands back Paystack's page. */
function api(status: string, extra: Record<string, unknown> = {}) {
  const fetch = vi.fn(async (url: string, init?: RequestInit) => {
    if (url.endsWith("/checkout") && init?.method === "POST")
      return Response.json({
        reference: "hc-abc12345",
        authorizationUrl: "https://checkout.paystack.com/abc",
        amountPesewas: 2000,
        currency: "GHS",
      })
    if (url.includes("/api/payments/"))
      return Response.json({
        appointmentId: teleconsult.id,
        enabled: true,
        status,
        amountPesewas: 2000,
        currency: "GHS",
        reference: status === "paid" ? "hc-abc12345" : null,
        channel: status === "paid" ? "mobile_money" : null,
        paidAt: status === "paid" ? now : null,
        ...extra,
      })
    return new Response(null, { status: 404 })
  })
  vi.stubGlobal("fetch", fetch)
  return fetch
}

// Tests run the app in its offline demo, which has no payments; these screens talk to the (stubbed) API.
vi.mock("@/api/auth", async (actual) => ({
  ...(await actual<typeof import("@/api/auth")>()),
  DEMO_MODE: false,
}))

afterEach(() => vi.unstubAllGlobals())

describe("consultation fee", () => {
  it("asks the CHV to collect the fee before the call, and opens Paystack", async () => {
    const user = userEvent.setup()
    const fetch = api("unpaid")
    const assign = vi.fn()
    vi.stubGlobal("location", {
      ...window.location,
      assign,
      origin: "https://app.example",
    })
    await db.patients.add(patient)
    await db.appointments.add(teleconsult)
    renderRoute("/appointments")

    const pay = await screen.findByRole("button", {
      name: /Pay .*20\.00 with mobile money/,
    })
    expect(
      screen.queryByRole("link", { name: /Join video call/ })
    ).not.toBeInTheDocument()

    await user.click(pay)

    expect(assign).toHaveBeenCalledWith("https://checkout.paystack.com/abc")
    const checkout = fetch.mock.calls.find(([url]) =>
      String(url).endsWith("/checkout")
    )!
    expect(JSON.parse(String(checkout[1]!.body))).toEqual({
      callbackUrl: `https://app.example/appointments/${teleconsult.id}/payment`,
    })
  })

  it("lets everyone join once the fee is paid", async () => {
    api("paid")
    await db.patients.add(patient)
    await db.appointments.add(teleconsult)
    renderRoute("/appointments")

    expect(await screen.findByText(/Fee paid/)).toBeInTheDocument()
    expect(
      screen.getByRole("link", { name: /Join video call/ })
    ).toBeInTheDocument()
  })

  it("shows the doctor that the CHV collects the fee, without a pay button", async () => {
    api("unpaid")
    await db.patients.add(patient)
    await db.appointments.add(teleconsult)
    renderRoute("/referrals", { as: "doctor" })

    expect(
      await screen.findByText(/The CHV collects the fee/)
    ).toBeInTheDocument()
    expect(
      screen.queryByRole("button", { name: /mobile money/ })
    ).not.toBeInTheDocument()
  })

  it("confirms the payment when Paystack sends the CHV back", async () => {
    api("paid")
    await db.patients.add(patient)
    await db.appointments.add(teleconsult)
    renderRoute(`/appointments/${teleconsult.id}/payment`)

    expect(await screen.findByText(/received/)).toBeInTheDocument()
    expect(screen.getByText(/hc-abc12345/)).toBeInTheDocument()
    expect(
      screen.getByRole("link", { name: /Join video call/ })
    ).toBeInTheDocument()
  })

  it("gives the district officer the fees collected, without patients", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(async () =>
        Response.json({
          district: "Shai-Osudoku",
          paidCount: 1,
          paidPesewas: 2000,
          pendingCount: 0,
          items: [
            {
              reference: "hc-abc12345",
              appointmentId: teleconsult.id,
              district: "Shai-Osudoku",
              amountPesewas: 2000,
              status: "paid",
              channel: "mobile_money",
              collectedBy: "Akosua Mensah",
              createdAt: now,
              paidAt: now,
              facility: "Dodowa District Hospital",
              scheduledAt: now,
            },
          ],
        })
      )
    )
    renderRoute("/payments", { as: "district_officer" })

    expect(await screen.findByText("1 consultation paid")).toBeInTheDocument()
    expect(screen.getByText("Collected by Akosua Mensah")).toBeInTheDocument()
    expect(screen.queryByText(/Abena/)).not.toBeInTheDocument()
  })
})
