import { useEffect, useRef } from "react"
import { cn } from "@/lib/utils"

const ROW = 44 // px, also the touch target

/**
 * Figma "Altura y Peso" wheel: a scrolling column of values with the chosen one highlighted in
 * the middle. Scroll or swipe to pick; arrow keys and Page Up/Down work too (it is a spinbutton
 * for screen readers). Picking from a range avoids the typos of typing on a small keypad.
 */
export function NumberWheel({
  label,
  unit,
  min,
  max,
  step = 1,
  value,
  onChange,
}: {
  label: string
  unit: string
  min: number
  max: number
  step?: number
  value: number
  onChange: (value: number) => void
}) {
  const ref = useRef<HTMLDivElement>(null)
  const count = Math.round((max - min) / step) + 1
  const decimals = step < 1 ? 1 : 0
  const at = (i: number) => +(min + i * step).toFixed(decimals)
  const index = Math.round((value - min) / step)
  const settling = useRef<number | undefined>(undefined)

  // Keep the scroll position on the value (first draw, and keyboard changes).
  useEffect(() => {
    const el = ref.current
    if (el && Math.round(el.scrollTop / ROW) !== index)
      el.scrollTop = index * ROW
  }, [index])

  function onScroll() {
    window.clearTimeout(settling.current)
    settling.current = window.setTimeout(() => {
      const el = ref.current
      if (!el) return
      const i = Math.min(count - 1, Math.max(0, Math.round(el.scrollTop / ROW)))
      if (i !== index) onChange(at(i))
    }, 80)
  }

  function onKeyDown(e: React.KeyboardEvent) {
    const jump = { ArrowUp: -1, ArrowDown: 1, PageUp: -10, PageDown: 10 }[e.key]
    if (jump === undefined) return
    e.preventDefault()
    onChange(at(Math.min(count - 1, Math.max(0, index + jump))))
  }

  return (
    <div className="flex flex-col items-center gap-2">
      <span className="rounded-md bg-primary px-4 py-1 text-sm font-semibold text-primary-foreground">
        {unit}
      </span>
      <div className="relative w-full max-w-36">
        {/* the highlighted middle row */}
        <div
          aria-hidden
          className="pointer-events-none absolute inset-x-0 top-[88px] h-11 rounded-md bg-primary/15"
        />
        <div
          ref={ref}
          role="spinbutton"
          aria-label={`${label} (${unit})`}
          aria-valuemin={min}
          aria-valuemax={max}
          aria-valuenow={value}
          aria-valuetext={`${value} ${unit}`}
          tabIndex={0}
          onScroll={onScroll}
          onKeyDown={onKeyDown}
          className="relative h-[220px] snap-y snap-mandatory overflow-y-scroll rounded-md [scrollbar-width:none] focus-visible:ring-3 focus-visible:ring-ring/50 focus-visible:outline-none [&::-webkit-scrollbar]:hidden"
        >
          <div style={{ height: ROW * 2 }} />
          {Array.from({ length: count }, (_, i) => {
            const distance = Math.abs(i - index)
            return (
              <div
                key={i}
                aria-hidden
                onClick={() => onChange(at(i))}
                className={cn(
                  "flex h-11 snap-center items-center justify-center tabular-nums",
                  distance === 0
                    ? "text-lg font-bold text-primary"
                    : distance === 1
                      ? "text-base text-foreground"
                      : "text-sm text-muted-foreground/60"
                )}
              >
                {at(i)}
              </div>
            )
          })}
          <div style={{ height: ROW * 2 }} />
        </div>
      </div>
    </div>
  )
}
