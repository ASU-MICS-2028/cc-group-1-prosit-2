import type { ReactNode } from "react"
import { cn } from "@/lib/utils"

/**
 * The one look for an error under a field or a control: a red "!" and the message, sliding in
 * gently so the eye catches it. `id` ties it to the control (`aria-describedby`); role="alert"
 * reads it out as soon as it appears.
 */
export function ErrorText({
  id,
  children,
  className,
}: {
  id?: string
  children: ReactNode
  className?: string
}) {
  return (
    <p
      id={id}
      role="alert"
      className={cn(
        "flex items-start gap-1.5 text-sm font-medium text-destructive",
        "animate-in fade-in-0 slide-in-from-top-1 duration-200 motion-reduce:animate-none",
        className
      )}
    >
      <svg
        viewBox="0 0 24 24"
        className="mt-px size-4 shrink-0"
        fill="currentColor"
        aria-hidden
      >
        {/* Material "error" (Apache-2.0) */}
        <path d="M12 2a10 10 0 1 0 0 20 10 10 0 0 0 0-20zm1 15h-2v-2h2v2zm0-4h-2V7h2v6z" />
      </svg>
      <span>{children}</span>
    </p>
  )
}
