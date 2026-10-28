import { HeartPulse, MapPin, Phone, Plus, Search } from "@/components/icons"
import { useTranslation } from "react-i18next"
import { Link, useSearchParams } from "react-router-dom"
import { Card, EmptyState, Monogram, PageHeader } from "@/components/PageHeader"
import { type PatientStatus, useCaseload } from "@/lib/caseload"
import { formatRelative } from "@/lib/dates"
import { cn } from "@/lib/utils"
import { ageFrom } from "@/lib/vitals"
import { fullName } from "./usePatients"

const FILTERS = {
  all: () => true,
  urgent: (s: PatientStatus) => s.urgent,
  due: (s: PatientStatus) => s.overdue || s.dueToday,
  unsynced: (s: PatientStatus) => s.unsynced,
} as const
type Filter = keyof typeof FILTERS

/**
 * Figma "Mis Contactos": search, then one card per patient with a monogram, name and contact
 * lines. Each card also says what the health worker should act on (refer today, a visit due,
 * not sent yet) and the last reading, and chips above the list narrow it to those.
 */
export function Component() {
  const { t } = useTranslation()
  const [params, setParams] = useSearchParams()
  const search = params.get("q") ?? ""
  const filter: Filter =
    (params.get("filter") as Filter) in FILTERS
      ? (params.get("filter") as Filter)
      : "all"
  const caseload = useCaseload()

  const q = search.trim().toLowerCase()
  const matches = caseload?.filter(
    ({ patient: p }) =>
      !q ||
      [p.givenName, p.familyName, p.community, p.phone ?? ""].some((v) =>
        v.toLowerCase().includes(q)
      )
  )
  const shown = matches?.filter(FILTERS[filter])
  // Patients to act on first: refer today, then overdue, then due today.
  const rank = (s: PatientStatus) =>
    s.urgent ? 0 : s.overdue ? 1 : s.dueToday ? 2 : 3
  const sorted = shown?.slice().sort((a, b) => rank(a) - rank(b))

  function set(key: string, value: string) {
    const next = new URLSearchParams(params)
    if (value && value !== "all") next.set(key, value)
    else next.delete(key)
    setParams(next, { replace: true })
  }

  return (
    <>
      <PageHeader
        title={t("patients.title")}
        subtitle={
          caseload ? t("patients.count", { count: caseload.length }) : undefined
        }
      />
      <label className="mb-3 flex h-12 items-center gap-2 rounded-xl bg-card px-4 shadow-card focus-within:ring-2 focus-within:ring-primary">
        <Search className="size-6 text-foreground" aria-hidden />
        <input
          type="search"
          aria-label={t("patients.search")}
          placeholder={t("patients.search")}
          value={search}
          onChange={(e) => set("q", e.target.value)}
          className="h-full min-w-0 flex-1 bg-transparent text-base outline-none placeholder:text-muted-foreground"
        />
      </label>

      <div
        role="radiogroup"
        aria-label={t("patients.filterLabel")}
        className="mb-4 flex flex-wrap gap-2"
      >
        {(Object.keys(FILTERS) as Filter[]).map((f) => {
          const count = matches?.filter(FILTERS[f]).length
          return (
            <button
              key={f}
              type="button"
              role="radio"
              aria-checked={filter === f}
              onClick={() => set("filter", f)}
              className={cn(
                "inline-flex h-10 shrink-0 items-center gap-1.5 rounded-full px-4 text-sm font-semibold whitespace-nowrap transition-colors",
                filter === f
                  ? "bg-primary text-primary-foreground"
                  : "bg-card text-foreground shadow-card"
              )}
            >
              {t(`patients.filter_${f}`)}
              {f !== "all" && count ? (
                <span
                  className={cn(
                    "rounded-full px-1.5 text-xs tabular-nums",
                    filter === f
                      ? "bg-primary-foreground/20"
                      : f === "urgent"
                        ? "bg-destructive-soft text-destructive"
                        : "bg-secondary text-secondary-foreground"
                  )}
                >
                  {count}
                </span>
              ) : null}
            </button>
          )
        })}
      </div>

      {sorted?.length === 0 ? (
        <EmptyState
          illustration={
            search || filter !== "all" ? "no-data" : "empty-patients"
          }
        >
          {search || filter !== "all"
            ? t("patients.noMatch")
            : t("patients.empty")}
        </EmptyState>
      ) : (
        <ul className="grid gap-3 md:grid-cols-2">
          {sorted?.map((s) => (
            <li key={s.patient.id}>
              <PatientCard s={s} />
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

function PatientCard({ s }: { s: PatientStatus }) {
  const { t } = useTranslation()
  const p = s.patient
  const status = s.urgent
    ? {
        label: t("patients.status_urgent"),
        tone: "bg-destructive-soft text-destructive",
      }
    : s.overdue
      ? {
          label: t("patients.status_overdue"),
          tone: "bg-warning-soft text-warning",
        }
      : s.dueToday
        ? {
            label: t("patients.status_today"),
            tone: "bg-secondary text-secondary-foreground",
          }
        : s.unsynced
          ? {
              label: t("patients.status_unsynced"),
              tone: "bg-muted text-muted-foreground",
            }
          : null
  const bp =
    s.latest?.systolic != null
      ? `${t("vitals.bp")} ${s.latest.systolic}/${s.latest.diastolic}`
      : null
  const temp =
    s.latest?.temperatureC != null ? `${s.latest.temperatureC} °C` : null

  return (
    <Link
      to={`/patients/${p.id}`}
      className="block rounded-xl focus-visible:ring-3 focus-visible:ring-ring/50 focus-visible:outline-none"
    >
      <Card className="p-4 transition-shadow hover:shadow-md">
        <div className="flex items-start gap-3">
          <Monogram name={fullName(p)} />
          <div className="min-w-0 flex-1">
            <p className="text-sm font-semibold break-words text-foreground">
              {fullName(p)}
            </p>
            <p className="text-xs font-medium text-muted-foreground">
              {t(`sex.${p.sex}`)} ·{" "}
              {t("patients.age", { count: ageFrom(p.dateOfBirth) })}
            </p>
          </div>
          {status ? (
            <span
              className={cn(
                "shrink-0 rounded-full px-2.5 py-1 text-xs font-semibold",
                status.tone
              )}
            >
              {status.label}
            </span>
          ) : null}
        </div>
        <div className="mt-3 space-y-1.5 text-sm text-foreground">
          <p className="flex items-start gap-2.5">
            <HeartPulse
              className={cn(
                "mt-0.5 size-4 shrink-0",
                s.urgent
                  ? "text-destructive"
                  : s.flags.length
                    ? "text-warning"
                    : "text-primary"
              )}
              aria-hidden
            />
            {s.latest ? (
              <span>
                {[bp, temp].filter(Boolean).join(" · ") ||
                  t("patients.reading")}{" "}
                <span className="text-muted-foreground">
                  · {formatRelative(s.latest.takenAt)}
                </span>
              </span>
            ) : (
              <span className="text-muted-foreground">
                {t("patients.noReading")}
              </span>
            )}
          </p>
          <p className="flex items-start gap-2.5">
            <MapPin
              className="mt-0.5 size-4 shrink-0 text-primary"
              aria-hidden
            />
            {p.community}, {p.district}
          </p>
          {p.phone ? (
            <p className="flex items-center gap-2.5">
              <Phone className="size-4 shrink-0 text-primary" aria-hidden />
              {p.phone}
            </p>
          ) : null}
        </div>
      </Card>
    </Link>
  )
}
