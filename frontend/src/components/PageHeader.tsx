import { ArrowLeft } from "lucide-react"
import type { ReactNode } from "react"
import { useTranslation } from "react-i18next"
import { Link } from "react-router-dom"

/**
 * Figma "Top Navbar" + page title: on sub-pages an arrow and "Back" above the title; the title is
 * Poppins Bold 32/36 (28 on narrow phones). An optional subtitle and action sit with it.
 */
export function PageHeader({
  title,
  back,
  subtitle,
  action,
  children,
}: {
  title: string
  /** Where "Back" goes, or what it does (e.g. the previous step of a form) */
  back?: string | (() => void)
  subtitle?: string
  action?: ReactNode
  /** Shown between the back bar and the title, e.g. a stepper */
  children?: ReactNode
}) {
  const { t } = useTranslation()
  const backClass =
    "-ml-2 mb-4 inline-flex h-11 items-center gap-3 rounded-md px-2 text-sm font-semibold text-heading"
  const backContent = (
    <>
      <ArrowLeft className="size-6" aria-hidden />
      {t("common.back")}
    </>
  )
  return (
    <header className="mb-6">
      {typeof back === "string" ? (
        <Link to={back} className={backClass}>
          {backContent}
        </Link>
      ) : back ? (
        <button type="button" onClick={back} className={backClass}>
          {backContent}
        </button>
      ) : null}
      {children}
      <div className="flex items-end gap-3">
        <h1 className="flex-1 text-[1.75rem] leading-9 font-bold text-heading min-[400px]:text-[2rem]">
          {title}
        </h1>
        {action}
      </div>
      {subtitle ? (
        <p className="mt-2 text-sm text-muted-foreground">{subtitle}</p>
      ) : null}
    </header>
  )
}

/** What a list shows when it has nothing in it yet. */
export function EmptyState({ children }: { children: ReactNode }) {
  return (
    <div className="rounded-xl border border-dashed border-border bg-card/60 p-8 text-center text-sm text-muted-foreground">
      {children}
    </div>
  )
}

/** Figma "Cards / outlined": white, 1 px grey outline, 12 px corners. */
export function Card({
  className = "",
  children,
}: {
  className?: string
  children: ReactNode
}) {
  return (
    <div className={`rounded-xl border border-border bg-card ${className}`}>
      {children}
    </div>
  )
}

/** Figma "monogram": a 40 px blue circle with the person's initials. */
export function Monogram({ name }: { name: string }) {
  const initials = name
    .split(" ")
    .filter(Boolean)
    .slice(0, 2)
    .map((part) => part[0]!.toUpperCase())
    .join("")
  return (
    <span
      aria-hidden
      className="grid size-10 shrink-0 place-items-center rounded-full bg-primary text-sm font-medium text-primary-foreground"
    >
      {initials}
    </span>
  )
}
