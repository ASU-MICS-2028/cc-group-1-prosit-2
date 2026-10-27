import { useState } from "react"
import { useTranslation } from "react-i18next"
import type { Vitals } from "@/db/types"
import { formatDate, formatDateTime } from "@/lib/dates"

const W = 340
const H = 190
const PAD = { top: 16, right: 74, bottom: 26, left: 32 }

/**
 * Blood pressure over visits (in the spirit of the Figma heart-rate card): systolic and
 * diastolic share one mmHg axis, with dashed reference lines at 140 and 90. Legend and direct
 * labels on the latest point, so the series are never told apart by colour alone; tap or hover
 * a visit to read it. The readings list under the chart is the table view.
 */
export function BpTrend({ readings }: { readings: Vitals[] }) {
  const { t } = useTranslation()
  const points = readings
    .filter((v) => v.systolic && v.diastolic)
    .sort((a, b) => a.takenAt.localeCompare(b.takenAt))
    .slice(-12)
  const [active, setActive] = useState<number | null>(null)
  if (points.length < 2) return null

  const lo = Math.min(60, ...points.map((p) => p.diastolic!)) - 10
  const hi = Math.max(150, ...points.map((p) => p.systolic!)) + 10
  const x = (i: number) =>
    PAD.left + (i * (W - PAD.left - PAD.right)) / (points.length - 1)
  const y = (v: number) =>
    PAD.top + ((hi - v) * (H - PAD.top - PAD.bottom)) / (hi - lo)
  const line = (key: "systolic" | "diastolic") =>
    points
      .map(
        (p, i) => `${i ? "L" : "M"}${x(i).toFixed(1)},${y(p[key]!).toFixed(1)}`
      )
      .join(" ")
  const last = points.length - 1
  const shown = active ?? last
  const series = [
    {
      key: "systolic" as const,
      label: t("chart.systolic"),
      color: "var(--chart-sys)",
    },
    {
      key: "diastolic" as const,
      label: t("chart.diastolic"),
      color: "var(--chart-dia)",
    },
  ]

  return (
    <figure className="rounded-xl border border-border bg-card p-4">
      <figcaption className="mb-2 flex flex-wrap items-baseline justify-between gap-2">
        <span className="text-base font-semibold text-heading">
          {t("chart.title")}
        </span>
        <span className="flex gap-4 text-xs text-muted-foreground">
          {series.map((s) => (
            <span key={s.key} className="flex items-center gap-1.5">
              <span
                className="h-0.5 w-4 rounded-full"
                style={{ background: s.color }}
              />
              {s.label}
            </span>
          ))}
        </span>
      </figcaption>
      <p className="mb-1 text-sm text-foreground" aria-live="polite">
        {formatDateTime(points[shown].takenAt)} ·{" "}
        <span className="font-semibold">
          {points[shown].systolic}/{points[shown].diastolic}
        </span>{" "}
        mmHg
      </p>
      <svg
        viewBox={`0 0 ${W} ${H}`}
        className="h-auto w-full"
        role="img"
        aria-label={t("chart.aria", {
          count: points.length,
          sys: points[last].systolic,
          dia: points[last].diastolic,
        })}
        onMouseLeave={() => setActive(null)}
      >
        {/* reference lines: high blood pressure from 140 / 90 */}
        {[140, 90].map((ref) => (
          <g key={ref}>
            <line
              x1={PAD.left}
              x2={W - PAD.right}
              y1={y(ref)}
              y2={y(ref)}
              stroke="var(--border)"
              strokeDasharray="4 4"
            />
            <text
              x={PAD.left - 6}
              y={y(ref) + 4}
              textAnchor="end"
              fontSize="10"
              fill="var(--muted-foreground)"
            >
              {ref}
            </text>
          </g>
        ))}
        {/* first and last visit dates */}
        {[0, last].map((i) => (
          <text
            key={i}
            x={x(i)}
            y={H - 6}
            textAnchor={i === 0 ? "start" : "end"}
            fontSize="10"
            fill="var(--muted-foreground)"
          >
            {formatDate(points[i].takenAt)}
          </text>
        ))}
        {active !== null ? (
          <line
            x1={x(active)}
            x2={x(active)}
            y1={PAD.top}
            y2={H - PAD.bottom}
            stroke="var(--muted-foreground)"
            strokeWidth="1"
          />
        ) : null}
        {series.map((s) => (
          <g key={s.key}>
            <path
              d={line(s.key)}
              fill="none"
              stroke={s.color}
              strokeWidth="2"
              strokeLinejoin="round"
              strokeLinecap="round"
            />
            {points.map((p, i) => (
              <circle
                key={p.id}
                cx={x(i)}
                cy={y(p[s.key]!)}
                r={i === shown ? 5 : 4}
                fill={s.color}
                stroke="var(--card)"
                strokeWidth="2"
              />
            ))}
            {/* direct label on the latest point, in text ink */}
            <text
              x={x(last) + 9}
              y={y(points[last][s.key]!) + 4}
              fontSize="11"
              fill="var(--foreground)"
            >
              {points[last][s.key]}{" "}
              <tspan fill="var(--muted-foreground)">
                {s.key === "systolic" ? "sys" : "dia"}
              </tspan>
            </text>
          </g>
        ))}
        {/* hit areas wider than the marks, one per visit */}
        {points.map((p, i) => {
          const half = (W - PAD.left - PAD.right) / (points.length - 1) / 2
          return (
            <rect
              key={p.id}
              x={x(i) - half}
              y={0}
              width={half * 2}
              height={H}
              fill="transparent"
              onMouseEnter={() => setActive(i)}
              onClick={() => setActive(i)}
            />
          )
        })}
      </svg>
    </figure>
  )
}
