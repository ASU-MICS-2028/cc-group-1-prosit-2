import { useRef } from "react"
import { cn } from "@/lib/utils"

/**
 * Figma "Validar Registro": six boxes for the SMS code. One real input underneath, so the
 * phone's "fill code from SMS" (autocomplete one-time-code), paste and screen readers all work;
 * the boxes only draw what is typed.
 */
export function CodeInput({
  value,
  onChange,
  label,
  invalid,
  length = 6,
}: {
  value: string
  onChange: (code: string) => void
  label: string
  invalid?: boolean
  length?: number
}) {
  const ref = useRef<HTMLInputElement>(null)
  return (
    <div
      className="relative mx-auto w-fit"
      onClick={() => ref.current?.focus()}
    >
      <input
        ref={ref}
        value={value}
        onChange={(e) =>
          onChange(e.target.value.replace(/\D/g, "").slice(0, length))
        }
        inputMode="numeric"
        autoComplete="one-time-code"
        aria-label={label}
        aria-invalid={invalid || undefined}
        maxLength={length}
        autoFocus
        className="peer absolute inset-0 h-full w-full cursor-default opacity-0"
      />
      <div aria-hidden className="flex gap-2">
        {Array.from({ length }, (_, i) => (
          <span
            key={i}
            className={cn(
              "grid h-14 w-11 place-items-center rounded-sm border bg-card text-xl font-semibold text-foreground",
              i === 3 && "ml-3",
              invalid
                ? "border-2 border-destructive"
                : i === Math.min(value.length, length - 1)
                  ? "border-input peer-focus:border-2 peer-focus:border-primary"
                  : "border-input"
            )}
          >
            {value[i] ?? ""}
          </span>
        ))}
      </div>
    </div>
  )
}
