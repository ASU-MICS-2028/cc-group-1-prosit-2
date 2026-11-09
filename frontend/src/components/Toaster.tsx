import { useEffect, useSyncExternalStore } from "react"
import { useTranslation } from "react-i18next"
import { CircleX, X } from "@/components/icons"
import { dismiss, snapshot, subscribe, type Kind } from "@/lib/toast"
import { cn } from "@/lib/utils"

/** The messages, top centre on phones (clear of the keyboard and the bottom bar), top right on computers. */
export function Toaster() {
  const { t } = useTranslation()
  const items = useSyncExternalStore(subscribe, snapshot, snapshot)

  // Escape closes the newest message, as with any pop-up.
  useEffect(() => {
    if (items.length === 0) return
    const onKey = (e: KeyboardEvent) => {
      if (e.key !== "Escape") return
      const last = items.filter((i) => !i.leaving).at(-1)
      if (last) dismiss(last.id)
    }
    window.addEventListener("keydown", onKey)
    return () => window.removeEventListener("keydown", onKey)
  }, [items])

  return (
    <section
      aria-label={t("toast.label")}
      aria-live="polite"
      className="pointer-events-none fixed inset-x-0 top-0 z-50 flex flex-col items-center gap-2 px-4 pt-[max(1rem,env(safe-area-inset-top))] md:items-end md:px-6 md:pt-6"
    >
      {items.map((item) => (
        <div
          key={item.id}
          role={item.kind === "error" ? "alert" : "status"}
          className={cn(
            "pointer-events-auto flex w-full max-w-sm items-start gap-3 rounded-2xl border-l-4 bg-card py-3 pr-2 pl-3 text-card-foreground shadow-floating",
            item.kind === "success" && "border-success",
            item.kind === "error" && "border-destructive",
            item.kind === "info" && "border-primary",
            item.leaving
              ? "animate-out fill-mode-forwards fade-out-0 zoom-out-95 slide-out-to-top-2 duration-200"
              : "animate-in fade-in-0 zoom-in-95 slide-in-from-top-4 duration-300 ease-out",
            "motion-reduce:animate-none"
          )}
        >
          <ToastIcon kind={item.kind} />
          <div className="min-w-0 flex-1 py-0.5">
            <p className="text-sm font-semibold">{item.title}</p>
            {item.description ? (
              <p className="mt-0.5 text-sm text-muted-foreground">
                {item.description}
              </p>
            ) : null}
          </div>
          <button
            type="button"
            onClick={() => dismiss(item.id)}
            aria-label={t("toast.dismiss")}
            className="-my-1 grid size-9 shrink-0 place-items-center rounded-full text-muted-foreground hover:bg-muted focus-visible:ring-3 focus-visible:ring-ring/50 focus-visible:outline-none"
          >
            <X className="size-5" aria-hidden />
          </button>
        </div>
      ))}
    </section>
  )
}

function ToastIcon({ kind }: { kind: Kind }) {
  if (kind === "error")
    return (
      <span className="grid size-8 shrink-0 place-items-center rounded-full bg-destructive-soft text-destructive">
        <CircleX
          className="size-5 animate-shake motion-reduce:animate-none"
          aria-hidden
        />
      </span>
    )
  if (kind === "info")
    return (
      <span className="grid size-8 shrink-0 place-items-center rounded-full bg-info-soft text-primary">
        <svg
          viewBox="0 0 24 24"
          className="size-5"
          fill="none"
          stroke="currentColor"
          strokeWidth={2.4}
          strokeLinecap="round"
          aria-hidden
        >
          <path d="M12 11v6M12 7.5v.01" />
        </svg>
      </span>
    )
  // A tick that draws itself inside a circle that pops in.
  return (
    <span className="grid size-8 shrink-0 animate-pop place-items-center rounded-full bg-success text-success-foreground motion-reduce:animate-none">
      <svg
        viewBox="0 0 24 24"
        className="size-5"
        fill="none"
        stroke="currentColor"
        strokeWidth={3}
        strokeLinecap="round"
        strokeLinejoin="round"
        aria-hidden
      >
        <path
          d="M5 12.5l4.5 4.5L19 7.5"
          pathLength={1}
          className="animate-draw [stroke-dasharray:1] motion-reduce:animate-none"
        />
      </svg>
    </span>
  )
}
