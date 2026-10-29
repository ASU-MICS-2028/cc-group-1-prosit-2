import { useTranslation } from "react-i18next"
import { Link } from "react-router-dom"
import { EmptyState } from "@/components/PageHeader"

export function Component() {
  const { t } = useTranslation()
  return (
    <EmptyState illustration="not-found">
      <p className="mb-3">{t("notFound.text")}</p>
      <Link to="/" className="font-medium text-primary underline">
        {t("notFound.home")}
      </Link>
    </EmptyState>
  )
}
