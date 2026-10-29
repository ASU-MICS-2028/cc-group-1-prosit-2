import { Select } from "@base-ui/react/select"
import { Check } from "@/components/icons"
import { cn } from "@/lib/utils"

export interface Option<T extends string> {
  value: T
  label: string
  /** smaller line under the label, e.g. a language's English name */
  hint?: string
  disabled?: boolean
}

/**
 * Figma "Dropdowns": a text-field trigger with ▾ that opens a menu of options; the chosen one is
 * highlighted with a check. Built on Base UI's Select, so it is a real listbox: arrow keys,
 * type-ahead, Escape, and screen readers all work, and it is the same on every phone (unlike the
 * browser's own select, which looks different on Android, iPhone and desktop).
 */
export function Dropdown<T extends string>({
  id,
  value,
  onChange,
  options,
  placeholder,
  invalid,
  describedBy,
}: {
  id: string
  value: T | undefined
  onChange: (value: T) => void
  options: readonly Option<T>[]
  placeholder?: string
  invalid?: boolean
  describedBy?: string
}) {
  const chosen = options.find((o) => o.value === value)
  return (
    <Select.Root
      value={value ?? null}
      onValueChange={(v) => v != null && onChange(v as T)}
      items={options.map((o) => ({ value: o.value, label: o.label }))}
    >
      <Select.Trigger
        id={id}
        aria-invalid={invalid || undefined}
        aria-describedby={describedBy}
        className={cn(
          "flex h-14 w-full items-center gap-2 rounded-sm border border-input bg-transparent px-4 text-left text-base text-foreground outline-none",
          "focus-visible:border-2 focus-visible:border-primary focus-visible:px-[15px]",
          "data-[popup-open]:border-2 data-[popup-open]:border-primary data-[popup-open]:px-[15px]",
          invalid && "border-2 border-destructive px-[15px]"
        )}
      >
        <Select.Value className="flex-1 truncate">
          {chosen ? (
            chosen.label
          ) : (
            <span className="text-muted-foreground">{placeholder}</span>
          )}
        </Select.Value>
        <Select.Icon className="text-muted-foreground transition-transform data-[popup-open]:rotate-180">
          <svg
            viewBox="0 0 24 24"
            width="24"
            height="24"
            aria-hidden
            fill="currentColor"
          >
            <path d="M7 10l5 5 5-5z" />
          </svg>
        </Select.Icon>
      </Select.Trigger>
      <Select.Portal>
        <Select.Positioner
          sideOffset={4}
          alignItemWithTrigger={false}
          className="z-50 outline-none"
        >
          <Select.Popup
            className={cn(
              "max-h-[min(22rem,var(--available-height))] w-[var(--anchor-width)] overflow-y-auto rounded-xl bg-card py-2 shadow-card ring-1 ring-black/5",
              "origin-[var(--transform-origin)] transition-[transform,opacity] duration-150 data-[ending-style]:scale-95 data-[ending-style]:opacity-0 data-[starting-style]:scale-95 data-[starting-style]:opacity-0"
            )}
          >
            <Select.List>
              {options.map((o) => (
                <Select.Item
                  key={o.value}
                  value={o.value}
                  disabled={o.disabled}
                  className={cn(
                    "mx-2 flex min-h-12 cursor-pointer items-center gap-3 rounded-lg px-3 py-2 text-base text-foreground outline-none select-none",
                    "data-[highlighted]:bg-secondary data-[selected]:font-semibold data-[selected]:text-primary",
                    "data-[disabled]:cursor-not-allowed data-[disabled]:opacity-50"
                  )}
                >
                  <span className="min-w-0 flex-1">
                    <Select.ItemText className="block truncate">
                      {o.label}
                    </Select.ItemText>
                    {o.hint ? (
                      <span className="block text-xs font-normal text-muted-foreground">
                        {o.hint}
                      </span>
                    ) : null}
                  </span>
                  <Select.ItemIndicator>
                    <Check className="size-5" />
                  </Select.ItemIndicator>
                </Select.Item>
              ))}
            </Select.List>
          </Select.Popup>
        </Select.Positioner>
      </Select.Portal>
    </Select.Root>
  )
}
