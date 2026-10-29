import { render, screen } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { describe, expect, it, vi } from "vitest"
import "@/i18n"
import { DatePicker } from "./DatePicker"

describe("DatePicker", () => {
  it("picks a birth date: year first, then the day, and closes", async () => {
    const user = userEvent.setup()
    const onChange = vi.fn()
    render(
      <DatePicker
        id="dob"
        value={undefined}
        onChange={onChange}
        max={new Date()}
        startWithYear
      />
    )
    await user.click(screen.getByRole("button", { name: "Choose a date" }))
    await user.click(await screen.findByRole("button", { name: "1990" }))
    await user.click(screen.getByRole("button", { name: /15, 1990$/ }))
    expect(onChange).toHaveBeenCalledWith(
      expect.stringMatching(/^1990-\d\d-15$/)
    )
    expect(
      screen.queryByRole("button", { name: "1990" })
    ).not.toBeInTheDocument()
  })

  it("keeps days after the latest date out of reach", async () => {
    const user = userEvent.setup()
    render(
      <DatePicker
        id="d"
        value="2026-10-27"
        onChange={() => {}}
        max={new Date(2026, 9, 27)}
      />
    )
    await user.click(screen.getByRole("button", { name: /Oct 27, 2026/ }))
    expect(
      await screen.findByRole("button", { name: /October 28, 2026/ })
    ).toBeDisabled()
    expect(screen.getByRole("button", { name: "Next month" })).toBeDisabled()
  })
})
