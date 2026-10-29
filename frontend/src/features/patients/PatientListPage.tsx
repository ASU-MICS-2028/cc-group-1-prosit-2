import { Plus, Search } from "lucide-react"
import { useState } from "react"
import { useTranslation } from "react-i18next"
import { Link } from "react-router-dom"
import { Input } from "@/components/form/Field"
import { EmptyState, PageHeader } from "@/components/PageHeader"
import { buttonVariants } from "@/components/ui/button"
import { ageFrom } from "@/lib/vitals"
import { fullName, usePatients } from "./usePatients"

export function Component() {
  const { t } = useTranslation()
  const [search, setSearch] = useState("")
  const patients = usePatients(search)

  return (
    <>
      <PageHeader
        title={t("patients.title")}
        action={
          <Link
            to="/patients/new"
            className={buttonVariants({ size: "lg" })}
            aria-label={t("home.registerPatient")}
          >
            <Plus aria-hidden />
            {t("patients.new")}
          </Link>
        }
      />
      <div className="relative mb-4">
        <Search
          className="absolute top-3.5 left-3 size-5 text-muted-foreground"
          aria-hidden
        />
        <Input
          type="search"
          aria-label={t("patients.search")}
          placeholder={t("patients.search")}
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          className="pl-10"
        />
      </div>
      {patients?.length === 0 ? (
        <EmptyState>
          {search ? t("patients.noMatch") : t("patients.empty")}
        </EmptyState>
      ) : (
        <ul className="divide-y rounded-2xl bg-card ring-1 ring-border">
          {patients?.map((p) => (
            <li key={p.id}>
              <Link
                to={`/patients/${p.id}`}
                className="flex items-center gap-3 p-3"
              >
                <span
                  aria-hidden
                  className="grid size-11 place-items-center rounded-full bg-secondary font-semibold text-secondary-foreground"
                >
                  {p.givenName[0]}
                  {p.familyName[0]}
                </span>
                <span className="min-w-0 flex-1">
                  <span className="block font-medium text-foreground">
                    {fullName(p)}
                  </span>
                  <span className="block text-sm text-muted-foreground">
                    {t(`sex.${p.sex}`)} ·{" "}
                    {t("patients.age", { count: ageFrom(p.dateOfBirth) })} ·{" "}
                    {p.community}
                  </span>
                </span>
              </Link>
            </li>
          ))}
        </ul>
      )}
    </>
  )
}
