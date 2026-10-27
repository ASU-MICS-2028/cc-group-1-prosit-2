import type { LucideIcon } from "lucide-react"
import { cn } from "@/lib/utils"

export interface Choice<T extends string> {
  value: T
  label: string
  icon: LucideIcon
}

/**
 * Figma "Indica tu Género" tiles: square, outlined, icon over label; the chosen one is filled
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
      <div className="flex gap-3">
        {choices.map(({ value: v, label, icon: Icon }) => {
          const selected = v === value
          return (
            <label
              key={v}
              className={cn(
                "flex aspect-square w-[5.5rem] cursor-pointer flex-col items-center justify-center gap-2 rounded-md border text-sm transition-colors has-[:focus-visible]:ring-3 has-[:focus-visible]:ring-ring/50",
                selected
                  ? "border-primary bg-primary text-primary-foreground"
                  : "border-input bg-card text-foreground hover:border-primary",
                error && !selected && "border-destructive"
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
              <Icon className="size-8" aria-hidden strokeWidth={1.75} />
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
