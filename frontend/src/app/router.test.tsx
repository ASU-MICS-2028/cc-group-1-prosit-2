import { screen } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { describe, expect, it } from "vitest"
import { renderRoute } from "@/test/renderRoute"

describe("routes and roles", () => {
  it("sends a brand-new phone to the welcome screen first", async () => {
    const { router } = renderRoute("/patients", { as: null, firstRun: true })
    expect(await screen.findByText("Choose your language")).toBeInTheDocument()
    expect(router.state.location.pathname).toBe("/welcome")
    await userEvent.click(screen.getByRole("button", { name: "Get started" }))
    expect(
      await screen.findByRole("heading", { name: "Sign in" })
    ).toBeInTheDocument()
    expect(localStorage.getItem("healthconnect.lang")).toBe("en")
  })

  it("sends someone who is not signed in to sign-in once a language is chosen", async () => {
    const { router } = renderRoute("/patients", { as: null })
    expect(
      await screen.findByRole("heading", { name: "Sign in" })
    ).toBeInTheDocument()
    expect(router.state.location.pathname).toBe("/login")
  })

  it("signs in with a phone number and the SMS code", async () => {
    const user = userEvent.setup()
    const { router } = renderRoute("/login", { as: null })
    await user.type(
      await screen.findByLabelText("Phone number"),
      "020 000 0101"
    )
    await user.click(screen.getByRole("button", { name: "Send code" }))
    expect(
      await screen.findByText(/sent a 6-digit code to 0200000101/)
    ).toBeInTheDocument()
    await user.type(screen.getByLabelText("6-digit code"), "123456")
    expect(await screen.findByText("Welcome, Akosua")).toBeInTheDocument()
    expect(router.state.location.pathname).toBe("/")
  })

  it("rejects an unknown number and a wrong code", async () => {
    const user = userEvent.setup()
    renderRoute("/login", { as: null })
    await user.type(await screen.findByLabelText("Phone number"), "0241234567")
    await user.click(screen.getByRole("button", { name: "Send code" }))
    expect(
      await screen.findByText(/isn't registered as a health worker/)
    ).toBeInTheDocument()
    await user.clear(screen.getByLabelText("Phone number"))
    await user.type(screen.getByLabelText("Phone number"), "0200000102")
    await user.click(screen.getByRole("button", { name: "Send code" }))
    await user.type(await screen.findByLabelText("6-digit code"), "000000")
    expect(await screen.findByText(/That code isn't right/)).toBeInTheDocument()
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

describe("when a screen fails to load", () => {
  it("shows a friendly message instead of the developer error page", async () => {
    const { RouteError } = await import("./RouteError")
    const { render } = await import("@testing-library/react")
    render(<RouteError />)
    expect(screen.getByText("This screen didn't load")).toBeInTheDocument()
    expect(
      screen.getByRole("button", { name: "Try again" })
    ).toBeInTheDocument()
  })
})
