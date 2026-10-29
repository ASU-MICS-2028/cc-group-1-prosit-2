import type { ComponentProps, ReactNode } from "react"
import { cn } from "@/lib/utils"

// Figma "Text Fields" (Material 3 outlined): 56 px, 4 px corners, grey outline; 2 px outline in
// the brand blue when focused and in red with an error.
const control =
  "h-14 w-full rounded-sm border border-input bg-transparent px-4 text-base text-foreground outline-none placeholder:text-muted-foreground focus-visible:border-2 focus-visible:border-primary focus-visible:px-[15px] aria-invalid:border-2 aria-invalid:border-destructive aria-invalid:px-[15px]"

/** A labelled control with its error under it; the label and error are tied to the control for screen readers. */
export function Field({
  id,
  label,
  hint,
  error,
  children,
}: {
  id: string
  label: string
  hint?: string
  error?: string
  children: ReactNode
}) {
  return (
    <div className="space-y-1.5">
      <label
        htmlFor={id}
        className="block text-sm font-medium tracking-[0.01em] text-foreground"
      >
        {label}
      </label>
      {children}
      {hint && !error ? (
        <p id={`${id}-hint`} className="text-xs text-muted-foreground">
          {hint}
        </p>
      ) : null}
      {error ? (
        <p
          id={`${id}-error`}
          role="alert"
          className="text-xs font-medium text-destructive"
        >
          {error}
        </p>
      ) : null}
    </div>
  )
}

export function Input({ className, ...props }: ComponentProps<"input">) {
  return <input className={cn(control, className)} {...props} />
}

export function Select({ className, ...props }: ComponentProps<"select">) {
  return <select className={cn(control, className)} {...props} />
}

export function Textarea({ className, ...props }: ComponentProps<"textarea">) {
  return (
    <textarea
      className={cn(control, "h-auto min-h-24 py-3", className)}
      {...props}
    />
  )
}
