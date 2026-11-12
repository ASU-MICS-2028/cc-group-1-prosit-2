import { screen, waitFor } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { afterEach, describe, expect, it, vi } from "vitest"
import { navFor } from "@/app/navItems"
import { homeFor } from "@/auth/session"
import { renderRoute } from "@/test/renderRoute"

// Tests run the app in its offline demo; these screens read from the (stubbed) API.
vi.mock("@/api/auth", async (actual) => ({
  ...(await actual<typeof import("@/api/auth")>()),
  DEMO_MODE: false,
}))

afterEach(() => vi.unstubAllGlobals())

const soon = new Date(Date.now() + 2 * 86_400_000).toISOString()

describe("patient sign-in", () => {
  it("starts a patient on their own page with only their own menu", () => {
    expect(homeFor("patient")).toBe("/my")
    expect(navFor("patient").map((n) => n.to)).toEqual(["/my", "/profile"])
  })

  it("shows the patient their visits, the fee for a teleconsult and their readings", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(async (url: string) =>
        url.endsWith("/api/my/health")
          ? Response.json({
              people: [
                {
                  id: "p1",
                  name: "Abena Owusu",
                  community: "Dodowa",
                  sex: "female",
                  dateOfBirth: null,
                },
              ],
              appointments: [
                {
                  id: "11111111-1111-4111-8111-111111111111",
                  patientId: "p1",
                  type: "teleconsult",
                  scheduledAt: soon,
                  facility: "Dodowa District Hospital",
                  reason: "Headaches",
                  priority: "urgent",
                  status: "scheduled",
                  referralStatus: "accepted",
                  createdAt: soon,
                  updatedAt: soon,
                  version: 1,
                },
              ],
              vitals: [
                {
                  id: "v1",
                  patientId: "p1",
                  takenAt: soon,
                  systolic: 145,
                  diastolic: 85,
                  createdAt: soon,
                  version: 1,
                },
              ],
            })
          : Response.json({
              appointmentId: "11111111-1111-4111-8111-111111111111",
              enabled: true,
              status: "unpaid",
              amountPesewas: 2000,
              currency: "GHS",
              reference: null,
              channel: null,
              paidAt: null,
            })
      )
    )
    renderRoute("/my", { as: "patient" })

    expect(await screen.findByText("Upcoming visits")).toBeInTheDocument()
    expect(screen.getByText("Dodowa District Hospital")).toBeInTheDocument()
    expect(screen.getByText("BP 145/85")).toBeInTheDocument()
    expect(
      await screen.findByRole("button", {
        name: /Pay .*20\.00 with mobile money/,
      })
    ).toBeInTheDocument()
  })

  it("keeps a patient out of the workers' pages", async () => {
    const { router } = renderRoute("/patients", { as: "patient" })
    await waitFor(() => expect(router.state.location.pathname).toBe("/my"))
  })
})

describe("health workers page", () => {
  it("lets the district officer add a CHV in their own district", async () => {
    const user = userEvent.setup()
    const fetch = vi.fn(async (_url: string, init?: RequestInit) =>
      init?.method === "POST"
        ? Response.json(
            {
              id: "chv-k3f9q2",
              name: "Yaa Asantewaa",
              phone: "+233245550001",
              role: "chv",
              district: "Shai-Osudoku",
              active: true,
              demo: false,
              addedBy: "test-district_officer",
              createdAt: soon,
            },
            { status: 201 }
          )
        : Response.json([])
    )
    vi.stubGlobal("fetch", fetch)
    renderRoute("/workers", { as: "district_officer" })

    await user.type(await screen.findByLabelText("Full name"), "Yaa Asantewaa")
    await user.type(screen.getByLabelText("Phone number"), "024 555 0001")
    expect(screen.queryByLabelText("District")).not.toBeInTheDocument()
    expect(
      screen.queryByRole("option", { name: "District officer" })
    ).not.toBeInTheDocument()
    await user.click(screen.getByRole("button", { name: "Add" }))

    expect(
      await screen.findByText(/Yaa Asantewaa was added/)
    ).toBeInTheDocument()
    const post = fetch.mock.calls.find(([, init]) => init?.method === "POST")!
    expect(JSON.parse(String(post[1]!.body))).toEqual({
      name: "Yaa Asantewaa",
      phone: "024 555 0001",
      role: "chv",
    })
  })
})
