import type { IconComponent } from "@/components/icons"
import { cn } from "@/lib/utils"

export interface Choice<T extends string> {
  value: T
  label: string
  icon: IconComponent
}

/**
 * Figma "Indica tu Género" tiles: icon over label, sharing the row equally; the chosen one is filled
 * blue. One tap instead of opening a dropdown, and the symbol helps patients who can't read the
 * word. Native radio buttons underneath, so keyboard and screen readers work as usual.
 */
export function ChoiceTiles<T extends string>({
  name,
  legend,
  choices,
  value,
  onChange,
  error,
}: {
  name: string
  legend: string
  choices: Choice<T>[]
  value: T | undefined
  onChange: (value: T) => void
  error?: string
}) {
  return (
    <fieldset
      className="space-y-1.5"
      aria-describedby={error ? `${name}-error` : undefined}
    >
      <legend className="mb-1.5 text-sm font-medium tracking-[0.01em] text-foreground">
        {legend}
      </legend>
      {/* the tiles share the full width equally, so 2 or 3 choices both fill the row */}
      <div
        className="grid gap-3"
        style={{
          gridTemplateColumns: `repeat(${choices.length}, minmax(0, 1fr))`,
        }}
      >
        {choices.map(({ value: v, label, icon: Icon }) => {
          const selected = v === value
          return (
            <label
              key={v}
              className={cn(
                "flex min-h-28 cursor-pointer flex-col items-center justify-center gap-2.5 rounded-xl px-2 py-4 text-center text-sm font-medium transition-colors has-[:focus-visible]:ring-3 has-[:focus-visible]:ring-ring/50",
                selected
                  ? "bg-primary text-primary-foreground shadow-card"
                  : "bg-card text-foreground shadow-card hover:bg-secondary",
                error && !selected && "ring-2 ring-destructive"
              )}
            >
              <input
                type="radio"
                name={name}
                value={v}
                checked={selected}
                onChange={() => onChange(v)}
                className="sr-only"
              />
              <Icon className="size-9" aria-hidden />
              {label}
            </label>
          )
        })}
      </div>
      {error ? (
        <p
          id={`${name}-error`}
          role="alert"
          className="text-xs font-medium text-destructive"
        >
          {error}
        </p>
      ) : null}
    </fieldset>
  )
}
