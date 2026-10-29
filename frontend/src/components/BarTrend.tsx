import { useState } from "react"

const W = 340
const H = 150
const PAD = { top: 10, right: 6, bottom: 22, left: 26 }

export interface Bar {
  label: string // axis label, e.g. "12 Oct"
  long: string // readout, e.g. "Week of 12 Oct 2026"
  value: number | null // null = suppressed (fewer than 5)
  flagged?: number // optional: how many of them needed attention
}

/**
 * One series over time as bars (single series, so the title names it and there is no legend).
 * Thin bars with 4 px rounded tops, a 2 px gap, recessive grid. Tap or hover a bar to read it.
 * Suppressed weeks are drawn as an outlined stub labelled "<5", never as zero.
 */
export function BarTrend({
  title,
  bars,
  color,
  suppressedLabel,
  readout,
}: {
  title: string
  bars: Bar[]
  color: string
  suppressedLabel: string
  /** the line above the chart for the chosen bar; default "long · value" */
  readout?: (bar: Bar) => string
}) {
  const [active, setActive] = useState<number | null>(null)
  const max = Math.max(5, ...bars.map((b) => b.value ?? 0))
  const step = Math.ceil(max / 4 / 5) * 5
  const top = step * 4
  const plotW = W - PAD.left - PAD.right
  const slot = plotW / bars.length
  const barW = Math.max(6, Math.min(22, slot * 0.6))
  const y = (v: number) =>
    PAD.top + ((top - v) * (H - PAD.top - PAD.bottom)) / top
  const shown = active ?? bars.length - 1
  const current = bars[shown]

  return (
    <figure className="rounded-xl bg-card shadow-card p-4">
      <figcaption className="text-base font-semibold text-heading">
        {title}
      </figcaption>
      <p className="mb-1 text-sm text-foreground" aria-live="polite">
        {readout ? (
          readout(current)
        ) : (
          <>
            {current.long} ·{" "}
            <span className="font-semibold">
              {current.value ?? suppressedLabel}
            </span>
          </>
        )}
      </p>
      <svg
        viewBox={`0 0 ${W} ${H}`}
        className="h-auto w-full"
        role="img"
        aria-label={`${title}: ${bars.map((b) => `${b.long} ${b.value ?? suppressedLabel}`).join("; ")}`}
        onMouseLeave={() => setActive(null)}
      >
        {[0, 1, 2, 3, 4].map((i) => (
          <g key={i}>
            <line
              x1={PAD.left}
              x2={W - PAD.right}
              y1={y(step * i)}
              y2={y(step * i)}
              stroke="var(--border)"
              strokeOpacity={i ? 0.6 : 1}
            />
            <text
              x={PAD.left - 5}
              y={y(step * i) + 3}
              textAnchor="end"
              fontSize="9"
              fill="var(--muted-foreground)"
            >
              {step * i}
            </text>
          </g>
        ))}
        {bars.map((b, i) => {
          const x = PAD.left + i * slot + (slot - barW) / 2
          const v = b.value
          const h = v == null ? 10 : Math.max(0, y(0) - y(v))
          const r = Math.min(4, barW / 2, h)
          return (
            <g
              key={i}
              onMouseEnter={() => setActive(i)}
              onClick={() => setActive(i)}
            >
              {v == null ? (
                <>
                  <rect
                    x={x}
                    y={y(0) - h}
                    width={barW}
                    height={h}
                    fill="none"
                    stroke="var(--muted-foreground)"
                    strokeDasharray="2 2"
                  />
                  <text
                    x={x + barW / 2}
                    y={y(0) - h - 3}
                    textAnchor="middle"
                    fontSize="8"
                    fill="var(--muted-foreground)"
                  >
                    {"<5"}
                  </text>
                </>
              ) : (
                <path
                  d={`M${x},${y(0)} V${y(0) - h + r} q0,-${r} ${r},-${r} H${x + barW - r} q${r},0 ${r},${r} V${y(0)} Z`}
                  fill={color}
                  opacity={active === null || active === i ? 1 : 0.45}
                />
              )}
              {/* hit area wider than the bar */}
              <rect
                x={PAD.left + i * slot}
                y={0}
                width={slot}
                height={H}
                fill="transparent"
              />
              {i % 2 === bars.length % 2 || bars.length <= 6 ? (
                <text
                  x={x + barW / 2}
                  y={H - 6}
                  textAnchor="middle"
                  fontSize="9"
                  fill="var(--muted-foreground)"
                >
                  {b.label}
                </text>
              ) : null}
            </g>
          )
        })}
      </svg>
    </figure>
  )
}
