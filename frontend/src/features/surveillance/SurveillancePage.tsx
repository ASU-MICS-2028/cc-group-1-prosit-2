import {
  Activity,
  HeartPulse,
  Thermometer,
  TrendingUp,
  Users,
} from "lucide-react"
import { useEffect, useState, type ReactNode } from "react"
import { useTranslation } from "react-i18next"
import { weeklyCounts, type WeeklyCount } from "@/api/surveillance"
import { useSession } from "@/auth/session"
import { BarTrend, type Bar } from "@/components/BarTrend"
import { Card, EmptyState, PageHeader } from "@/components/PageHeader"
import i18n from "@/i18n"
import { cn } from "@/lib/utils"

type Period = "week" | "month"

const fmt = (iso: string, o: Intl.DateTimeFormatOptions) =>
  new Intl.DateTimeFormat(i18n.language, { ...o, timeZone: "UTC" }).format(
    new Date(iso)
  )

/** Group weekly rows into months; suppressed weeks are left out of the sum and flagged. */
function byPeriod(rows: WeeklyCount[], period: Period) {
  const groups = new Map<
    string,
    {
      key: string
      seen: number
      bp: number
      fever: number
      partial: boolean
      all: boolean
    }
  >()
  for (const r of rows) {
    const key = period === "week" ? r.week : r.week.slice(0, 7) + "-01"
    const g = groups.get(key) ?? {
      key,
      seen: 0,
      bp: 0,
      fever: 0,
      partial: false,
      all: true,
    }
    if (r.suppressed) g.partial = true
    else {
      g.all = false
      g.seen += r.patients_seen ?? 0
      g.bp += r.patients_high_bp ?? 0
      g.fever += r.patients_fever ?? 0
    }
    groups.set(key, g)
  }
  return [...groups.values()].sort((a, b) => a.key.localeCompare(b.key))
}

/**
 * The district officer's summary (Figma "Activity / Month" Day/Week/Month tabs): anonymised
 * counts only, from Liza's surveillance_weekly view. Fever rising well above its recent level is
 * raised as an early outbreak signal. Counts under 5 are never shown, in line with Act 843 s.24(3).
 */
export function Component() {
  const { t } = useTranslation()
  const session = useSession()
  const [period, setPeriod] = useState<Period>("week")
  const [data, setData] = useState<
    { rows: WeeklyCount[]; demo: boolean } | "error" | null
  >(null)

  useEffect(() => {
    weeklyCounts().then(setData, () => setData("error"))
  }, [])

  const district = session?.user.district ?? ""
  if (data === null) return <PageHeader title={t("surveillance.title")} />
  if (data === "error")
    return (
      <>
        <PageHeader title={t("surveillance.title")} />
        <EmptyState>{t("surveillance.offline")}</EmptyState>
      </>
    )

  const mine = byPeriod(
    data.rows.filter((r) => r.district === district),
    period
  )
  // Headline the last complete week (this week has only just started); a month is shown so far.
  const latest = period === "week" ? mine.at(-2) : mine.at(-1)
  const previous = period === "week" ? mine.at(-3) : mine.at(-2)
  const label = (key: string) =>
    period === "week"
      ? fmt(key, { day: "numeric", month: "short" })
      : fmt(key, { month: "short" })
  const long = (key: string) =>
    period === "week"
      ? t("surveillance.weekOf", {
          date: fmt(key, { day: "numeric", month: "short", year: "numeric" }),
        })
      : fmt(key, { month: "long", year: "numeric" })
  const bars = (pick: "seen" | "bp" | "fever"): Bar[] =>
    mine.slice(period === "week" ? -8 : -6).map((g) => ({
      label: label(g.key),
      long: long(g.key),
      value: g.all ? null : g[pick],
    }))

  // Early warning: this week's fever cases at least double the average of the 4 weeks before.
  const weekly = byPeriod(data.rows, "week")
  const signals = [...new Set(data.rows.map((r) => r.district))].flatMap(
    (d) => {
      const w = data.rows
        .filter((r) => r.district === d && !r.suppressed)
        .sort((a, b) => a.week.localeCompare(b.week))
      const recent = w.slice(-2, -1)[0] // last complete week
      const before = w.slice(-6, -2)
      if (!recent || before.length < 3) return []
      const avg =
        before.reduce((s, r) => s + (r.patients_fever ?? 0), 0) / before.length
      return (recent.patients_fever ?? 0) >= Math.max(4, 2 * avg)
        ? [
            {
              district: d,
              now: recent.patients_fever ?? 0,
              avg: Math.round(avg * 10) / 10,
            },
          ]
        : []
    }
  )
  const thisWeek = weekly.at(-1)?.key
  const table = data.rows
    .filter((r) => r.week === thisWeek)
    .sort((a, b) => a.district.localeCompare(b.district))

  return (
    <>
      <PageHeader
        title={t("surveillance.title")}
        subtitle={t("surveillance.district", { district })}
      />
      {data.demo ? (
        <p className="-mt-3 mb-4 inline-flex rounded-full bg-warning-soft px-3 py-1 text-xs font-semibold text-warning">
          {t("surveillance.demo")}
        </p>
      ) : null}

      {signals.map((s) => (
        <div
          key={s.district}
          role="alert"
          className="mb-4 flex gap-3 rounded-xl border-2 border-destructive bg-destructive-soft p-4 text-destructive"
        >
          <TrendingUp className="size-6 shrink-0" aria-hidden />
          <div>
            <p className="font-bold">
              {t("surveillance.signalTitle", { district: s.district })}
            </p>
            <p className="text-sm text-foreground">
              {t("surveillance.signalText", { now: s.now, avg: s.avg })}
            </p>
          </div>
        </div>
      ))}

      <div
        role="tablist"
        className="mb-4 grid grid-cols-2 gap-1 rounded-md bg-card p-1 ring-1 ring-border"
      >
        {(["week", "month"] as const).map((k) => (
          <button
            key={k}
            role="tab"
            aria-selected={period === k}
            onClick={() => setPeriod(k)}
            className={cn(
              "h-11 rounded-md text-sm font-semibold",
              period === k
                ? "bg-primary text-primary-foreground"
                : "text-foreground hover:bg-muted"
            )}
          >
            {t(`surveillance.${k}`)}
          </button>
        ))}
      </div>

      {latest ? (
        <>
          <p className="mb-2 text-sm font-semibold text-heading">
            {period === "week"
              ? t("surveillance.lastFullWeek", { label: long(latest.key) })
              : t("surveillance.soFar", { label: long(latest.key) })}
          </p>
          <div className="mb-4 grid grid-cols-3 gap-3">
            <Stat
              icon={<Users />}
              label={t("surveillance.seen")}
              value={latest.all ? null : latest.seen}
              before={previous?.all ? null : previous?.seen}
            />
            <Stat
              icon={<HeartPulse />}
              label={t("surveillance.highBp")}
              value={latest.all ? null : latest.bp}
              before={previous?.all ? null : previous?.bp}
            />
            <Stat
              icon={<Thermometer />}
              label={t("surveillance.fever")}
              value={latest.all ? null : latest.fever}
              before={previous?.all ? null : previous?.fever}
            />
          </div>
          {latest.partial && !latest.all ? (
            <p className="-mt-2 mb-4 text-xs text-muted-foreground">
              {t("surveillance.partial")}
            </p>
          ) : null}
          <div className="space-y-4">
            <BarTrend
              title={t("surveillance.seenChart")}
              bars={bars("seen")}
              color="var(--chart-sys)"
              suppressedLabel={t("surveillance.fewer")}
            />
            <BarTrend
              title={t("surveillance.feverChart")}
              bars={bars("fever")}
              color="var(--chart-dia)"
              suppressedLabel={t("surveillance.fewer")}
            />
          </div>
        </>
      ) : (
        <EmptyState>{t("surveillance.empty")}</EmptyState>
      )}

      <section className="mt-6 space-y-2">
        <h2 className="flex items-center gap-2 text-base font-semibold text-heading">
          <Activity className="size-5 text-primary" aria-hidden />
          {t("surveillance.allDistricts")}
        </h2>
        <div className="overflow-x-auto rounded-xl border border-border bg-card">
          <table className="w-full text-sm">
            <thead className="text-left text-xs text-muted-foreground">
              <tr className="border-b border-border">
                <th className="p-3 font-medium">
                  {t("surveillance.colDistrict")}
                </th>
                <th className="p-3 text-right font-medium">
                  {t("surveillance.seen")}
                </th>
                <th className="p-3 text-right font-medium">
                  {t("surveillance.highBp")}
                </th>
                <th className="p-3 text-right font-medium">
                  {t("surveillance.fever")}
                </th>
              </tr>
            </thead>
            <tbody>
              {table.map((r) => (
                <tr
                  key={r.district}
                  className={cn(
                    "border-b border-border last:border-0",
                    r.district === district && "font-semibold"
                  )}
                >
                  <td className="p-3 text-foreground">{r.district}</td>
                  {[r.patients_seen, r.patients_high_bp, r.patients_fever].map(
                    (v, i) => (
                      <td
                        key={i}
                        className="p-3 text-right text-foreground tabular-nums"
                      >
                        {r.suppressed ? (
                          <span className="text-muted-foreground">
                            {t("surveillance.fewer")}
                          </span>
                        ) : (
                          v
                        )}
                      </td>
                    )
                  )}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        <p className="text-xs text-muted-foreground">
          {t("surveillance.privacy")}
        </p>
      </section>
    </>
  )
}

function Stat({
  icon,
  label,
  value,
  before,
}: {
  icon: ReactNode
  label: string
  value: number | null
  before: number | null | undefined
}) {
  const { t } = useTranslation()
  const diff = value != null && before != null ? value - before : null
  return (
    <Card className="space-y-1 p-3 [&>svg]:size-5 [&>svg]:text-primary">
      {icon}
      <p className="text-2xl leading-8 font-semibold text-foreground tabular-nums">
        {value ?? t("surveillance.fewer")}
      </p>
      <p className="text-xs font-medium text-muted-foreground">{label}</p>
      {diff != null ? (
        <p className="text-xs text-muted-foreground tabular-nums">
          {t("surveillance.vsLast", {
            diff: (diff > 0 ? "+" : diff < 0 ? "−" : "±") + Math.abs(diff),
          })}
        </p>
      ) : null}
    </Card>
  )
}
