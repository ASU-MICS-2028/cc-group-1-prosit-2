import { render, screen } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { useState } from "react"
import { describe, expect, it } from "vitest"
import * as Icons from "./icons"
import { Dropdown } from "./form/Dropdown"

describe("icons", () => {
  it.each(Object.entries(Icons).filter(([, v]) => typeof v === "function"))(
    "%s draws something visible (an explicit fill or stroke)",
    (_, Icon) => {
      const C = Icon as Icons.IconComponent
      const { container } = render(<C />)
      const shapes = container.querySelectorAll(
        "path, circle, rect, polygon, ellipse"
      )
      expect(shapes.length).toBeGreaterThan(0)
      for (const s of shapes) {
        const paints = ["fill", "stroke"]
          .map((a) => s.getAttribute(a))
          .filter((v) => v && v !== "none")
        expect(paints.length).toBeGreaterThan(0)
      }
    }
  )
})

describe("Dropdown", () => {
  function Demo() {
    const [v, setV] = useState<"a" | "b">("a")
    return (
      <>
        <label htmlFor="pick">Pick</label>
        <Dropdown
          id="pick"
          value={v}
          onChange={setV}
          options={[
            { value: "a", label: "Apple" },
            { value: "b", label: "Banana", hint: "yellow" },
          ]}
        />
        <output>{v}</output>
      </>
    )
  }
  it("opens on click, shows the hint, and picks an option", async () => {
    const user = userEvent.setup()
    render(<Demo />)
    const trigger = screen.getByLabelText("Pick")
    expect(trigger).toHaveTextContent("Apple")
    await user.click(trigger)
    expect(await screen.findByText("yellow")).toBeInTheDocument()
    await user.click(screen.getByRole("option", { name: /Banana/ }))
    expect(trigger).toHaveTextContent("Banana")
  })
})
