import { screen } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { describe, expect, it } from "vitest"
import { renderRoute } from "@/test/renderRoute"

describe("routes and roles", () => {
  it("sends someone who is not signed in to the sign-in screen", async () => {
    const { router } = renderRoute("/patients", { as: null })
    expect(await screen.findByText(/Demo sign-in/)).toBeInTheDocument()
    expect(router.state.location.pathname).toBe("/login")
  })

  it("signs in as a CHV from the demo screen", async () => {
    const { router } = renderRoute("/login", { as: null })
    await userEvent.click(
      await screen.findByRole("button", { name: /Community Health Volunteer/ })
    )
    expect(await screen.findByText("Register a patient")).toBeInTheDocument()
    expect(router.state.location.pathname).toBe("/")
  })

  it("keeps a district officer out of patient records", async () => {
    const { router } = renderRoute("/patients", { as: "district_officer" })
    expect(await screen.findByText("District summary")).toBeInTheDocument()
    expect(router.state.location.pathname).toBe("/surveillance")
  })

  it("shows a not-found page for unknown addresses", async () => {
    renderRoute("/nowhere")
    expect(
      await screen.findByText("This page does not exist.")
    ).toBeInTheDocument()
  })

  it("signs out from the profile", async () => {
    const { router } = renderRoute("/profile")
    await userEvent.click(
      await screen.findByRole("button", { name: "Sign out" })
    )
    expect(router.state.location.pathname).toBe("/login")
  })
})
