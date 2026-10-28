import { MapPin, Phone, Plus, Search } from "@/components/icons"
import { useState } from "react"
import { useTranslation } from "react-i18next"
import { Link } from "react-router-dom"
import { Card, EmptyState, Monogram, PageHeader } from "@/components/PageHeader"
import { ageFrom } from "@/lib/vitals"
import { fullName, usePatients } from "./usePatients"

/** Figma "Mis Contactos": title, search bar, outlined cards with a monogram, floating add button. */
export function Component() {
  const { t } = useTranslation()
  const [search, setSearch] = useState("")
  const patients = usePatients(search)

  return (
    <>
      <PageHeader title={t("patients.title")} />
      <label className="mb-4 flex h-12 items-center gap-2 rounded-xl bg-card px-4 shadow-card focus-within:ring-2 focus-within:ring-primary">
        <Search className="size-6 text-foreground" aria-hidden />
        <input
          type="search"
          aria-label={t("patients.search")}
          placeholder={t("patients.search")}
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          className="h-full min-w-0 flex-1 bg-transparent text-base outline-none placeholder:text-muted-foreground"
        />
      </label>
      {patients?.length === 0 ? (
        <EmptyState illustration={search ? "no-data" : "empty-patients"}>
          {search ? t("patients.noMatch") : t("patients.empty")}
        </EmptyState>
      ) : (
        <ul className="grid gap-4 md:grid-cols-2">
          {patients?.map((p) => (
            <li key={p.id}>
              <Link
                to={`/patients/${p.id}`}
                className="block rounded-xl focus-visible:ring-3 focus-visible:ring-ring/50 focus-visible:outline-none"
              >
                <Card className="transition-shadow hover:shadow-md">
                  <div className="flex items-center gap-4 px-4 py-4">
                    <Monogram name={fullName(p)} />
                    <div className="min-w-0 flex-1">
                      <p className="text-sm font-semibold break-words text-foreground">
                        {fullName(p)}
                      </p>
                      <p className="text-xs font-medium tracking-[0.04em] text-muted-foreground">
                        {t(`sex.${p.sex}`)} ·{" "}
                        {t("patients.age", { count: ageFrom(p.dateOfBirth) })}
                      </p>
                    </div>
                  </div>
                  <div className="space-y-2 px-4 pb-4 text-sm text-foreground">
                    <p className="flex items-center gap-3">
                      <MapPin className="size-5 shrink-0" aria-hidden />
                      {p.community}, {p.district}
                    </p>
                    {p.phone ? (
                      <p className="flex items-center gap-3">
                        <Phone className="size-5 shrink-0" aria-hidden />
                        {p.phone}
                      </p>
                    ) : null}
                  </div>
                </Card>
              </Link>
            </li>
          ))}
        </ul>
      )}
      {/* Figma floating button: register a patient */}
      <Link
        to="/patients/new"
        aria-label={t("home.registerPatient")}
        className="fixed right-4 bottom-28 z-10 grid size-14 place-items-center rounded-2xl bg-nav text-foreground shadow-floating md:right-8 md:bottom-8"
      >
        <Plus className="size-6" aria-hidden />
      </Link>
    </>
  )
}
