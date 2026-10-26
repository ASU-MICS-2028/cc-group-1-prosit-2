import { ArrowLeft } from "lucide-react"
import type { ReactNode } from "react"
import { useTranslation } from "react-i18next"
import { Link } from "react-router-dom"

/** A page title, with a back link on sub-pages and an optional action on the right. */
export function PageHeader({
  title,
  back,
  action,
}: {
  title: string
  back?: string
  action?: ReactNode
}) {
  const { t } = useTranslation()
  return (
    <header className="mb-5 flex items-center gap-3">
      {back ? (
        <Link
          to={back}
          aria-label={t("common.back")}
          className="grid size-10 place-items-center rounded-full bg-muted"
        >
          <ArrowLeft className="size-5" aria-hidden />
        </Link>
      ) : null}
      <h1 className="flex-1 text-xl font-semibold text-foreground">{title}</h1>
      {action}
    </header>
  )
}

/** What a list shows when it has nothing in it yet. */
export function EmptyState({ children }: { children: ReactNode }) {
  return (
    <div className="rounded-2xl border border-dashed p-8 text-center text-sm text-muted-foreground">
      {children}
    </div>
  )
}
