import { act, render, screen } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"
import "@/i18n"
import { ErrorText } from "@/components/form/ErrorText"
import { saved, saveFailed } from "@/lib/feedback"
import { toast } from "@/lib/toast"
import { Toaster } from "./Toaster"

describe("Toaster", () => {
  beforeEach(() => vi.useFakeTimers({ shouldAdvanceTime: true }))
  afterEach(() => {
    act(() => toast.clear())
    vi.useRealTimers()
  })

  it("shows a success message, then takes it away by itself", () => {
    render(<Toaster />)
    act(() => void toast.success("Vitals saved"))
    expect(screen.getByRole("status")).toHaveTextContent("Vitals saved")
    act(() => void vi.advanceTimersByTime(3500 + 250))
    expect(screen.queryByText("Vitals saved")).not.toBeInTheDocument()
  })

  it("reads errors out at once and keeps them up longer", () => {
    render(<Toaster />)
    act(() => void toast.error("Couldn't save", "Try again."))
    expect(screen.getByRole("alert")).toHaveTextContent(
      "Couldn't saveTry again."
    )
    act(() => void vi.advanceTimersByTime(4000))
    expect(screen.getByRole("alert")).toBeInTheDocument()
  })

  it("closes with its button or Escape", async () => {
    const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime })
    render(<Toaster />)
    act(() => void toast.info("One"))
    await user.click(screen.getByRole("button", { name: "Dismiss" }))
    act(() => void vi.advanceTimersByTime(250))
    expect(screen.queryByText("One")).not.toBeInTheDocument()

    act(() => void toast.info("Two"))
    await user.keyboard("{Escape}")
    act(() => void vi.advanceTimersByTime(250))
    expect(screen.queryByText("Two")).not.toBeInTheDocument()
  })

  it("doesn't stack the same message twice, and shows at most three", () => {
    render(<Toaster />)
    act(() => {
      toast.success("Saved")
      toast.success("Saved")
    })
    expect(screen.getAllByText("Saved")).toHaveLength(1)
    act(() => ["a", "b", "c", "d"].forEach((m) => toast.info(m)))
    act(() => void vi.advanceTimersByTime(250))
    expect(screen.getAllByRole("status")).toHaveLength(3)
  })

  it("says the standard save messages, adding the offline note when offline", () => {
    render(<Toaster />)
    const online = vi.spyOn(navigator, "onLine", "get").mockReturnValue(false)
    act(() => saved("Vitals saved"))
    expect(screen.getByRole("status")).toHaveTextContent(
      "It will be sent when you're back online."
    )
    online.mockRestore()
    act(() => saveFailed())
    expect(screen.getByRole("alert")).toHaveTextContent(
      "Couldn't save on this phone"
    )
  })
})

describe("ErrorText", () => {
  it("is announced and can be tied to its field", () => {
    render(<ErrorText id="phone-error">Enter a Ghana number.</ErrorText>)
    const error = screen.getByRole("alert")
    expect(error).toHaveAttribute("id", "phone-error")
    expect(error).toHaveTextContent("Enter a Ghana number.")
  })
})
