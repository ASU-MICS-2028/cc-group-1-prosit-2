import { cn } from "@/lib/utils"

/**
 * Figma "Día / Semana / Mes": a white track with the chosen option filled blue. For 2-3 short
 * choices, where a dropdown would hide them behind an extra tap. Native radios underneath.
 * The highlight's corners follow the track's (18 px track - 4 px padding = 14 px).
 */
export function Segmented<T extends string>({
  name,
  legend,
  value,
  onChange,
  options,
  tone,
  hideLegend = false,
}: {
  name: string
  legend: string
  value: T | undefined
  onChange: (value: T) => void
  options: readonly { value: T; label: string }[]
  /** colour the chosen option by meaning, e.g. emergency in red */
  tone?: (value: T) => "primary" | "warning" | "destructive"
  /** when the page title already says what the choice is (tabs) */
  hideLegend?: boolean
}) {
  return (
    <fieldset>
      <legend
        className={cn(
          "mb-1.5 text-sm font-medium tracking-[0.01em] text-foreground",
          hideLegend && "sr-only"
        )}
      >
        {legend}
      </legend>
      <div
        className="grid gap-1 rounded-xl bg-card p-1 shadow-card"
        style={{
          gridTemplateColumns: `repeat(${options.length}, minmax(0, 1fr))`,
        }}
      >
        {options.map((o) => {
          const selected = o.value === value
          const t = tone?.(o.value) ?? "primary"
          return (
            <label
              key={o.value}
              className={cn(
                "flex min-h-12 cursor-pointer items-center justify-center rounded-[14px] px-2 text-center text-sm font-semibold transition-colors has-[:focus-visible]:ring-3 has-[:focus-visible]:ring-ring/50",
                !selected && "text-foreground hover:bg-muted",
                selected &&
                  t === "primary" &&
                  "bg-primary text-primary-foreground",
                selected && t === "warning" && "bg-warning text-white",
                selected && t === "destructive" && "bg-destructive text-white"
              )}
            >
              <input
                type="radio"
                name={name}
                value={o.value}
                checked={selected}
                onChange={() => onChange(o.value)}
                className="sr-only"
              />
              {o.label}
            </label>
          )
        })}
      </div>
    </fieldset>
  )
}
