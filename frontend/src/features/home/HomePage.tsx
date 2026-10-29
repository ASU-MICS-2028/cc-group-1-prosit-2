import {
  CalendarDays,
  ChevronRight,
  CloudUpload,
  HeartPulse,
  Thermometer,
  TriangleAlert,
  UserPlus,
} from "@/components/icons"
import type { ReactNode } from "react"
import { useTranslation } from "react-i18next"
import { Link } from "react-router-dom"
import { useSession } from "@/auth/session"
import { BarTrend } from "@/components/BarTrend"
import { Card, Monogram } from "@/components/PageHeader"
import { ProfileCard } from "@/components/ProfileCard"
import { buttonVariants } from "@/components/ui/button"
import { fullName } from "@/features/patients/usePatients"
import { useCaseload, useHomeNumbers } from "@/lib/caseload"
import { formatRelative, formatTime } from "@/lib/dates"
import { cn } from "@/lib/utils"
import i18n from "@/i18n"

/**
 * Figma "Home Screen", for a health worker's day: the worker's card with their numbers, what
 * needs doing today (a ring like the kit's "Tus Pasos"), this week's readings (like its
 * "Ritmo Cardiaco" chart), the two conditions CHVs follow most (like its half-width
 * "Estrés / Sueño" cards), the next visits, and sync.
 */
export function Component() {
  const { t } = useTranslation()
  const session = useSession()
  const n = useHomeNumbers()
  const urgent = useCaseload()?.filter((s) => s.urgent).length

  if (!session) return null
  const { user } = session
  const today = new Intl.DateTimeFormat(i18n.language, {
    weekday: "long",
    day: "numeric",
    month: "long",
  }).format(new Date())

  return (
    <div className="space-y-4">
      <header>
        <h1 className="text-lg font-semibold text-heading">
          {t("home.greeting", { name: user.name.split(" ")[0] })}
        </h1>
        <p className="text-sm text-muted-foreground first-letter:uppercase">
          {today}
        </p>
      </header>

      <ProfileCard user={user}>
        {/* the kit's "28 Edad · 178 cm Estatura · 60 kg Peso" row */}
        <dl className="mt-4 grid grid-cols-3 gap-2 border-t border-border/60 pt-4">
          <Figure value={n?.patients} label={t("home.patients")} />
          <Figure value={n?.visitsToday.total} label={t("home.visitsToday")} />
          <Figure
            value={n?.toSend}
            label={t("home.toSend")}
            tone={n?.toSend ? "warning" : undefined}
          />
        </dl>
      </ProfileCard>

      <Link
        to="/patients/new"
        className={cn(buttonVariants({ size: "xl" }), "w-full")}
      >
        <UserPlus aria-hidden />
        {t("home.registerPatient")}
      </Link>

      {urgent ? (
        <Link
          to="/patients?filter=urgent"
          className="flex items-center gap-3 rounded-xl border border-destructive bg-destructive-soft p-4 text-destructive"
        >
          <TriangleAlert className="size-6 shrink-0" aria-hidden />
          <span className="flex-1 text-sm font-semibold">
            {t("home.urgentPatients", { count: urgent })}
          </span>
          <ChevronRight className="size-5" aria-hidden />
        </Link>
      ) : null}

      {n ? (
        <>
          <Link to="/appointments" className="block">
            <Card className="p-4">
              <h2 className="mb-3 text-base font-semibold text-heading">
                {t("home.today")}
              </h2>
              <div className="flex items-center gap-5">
                <Ring
                  done={n.visitsToday.done}
                  total={n.visitsToday.total}
                  label={t("home.visitsDone", {
                    done: n.visitsToday.done,
                    total: n.visitsToday.total,
                  })}
                />
                <dl className="min-w-0 flex-1 space-y-2 text-sm">
                  <Line
                    label={t("home.dueToday")}
                    value={n.visitsToday.total - n.visitsToday.done}
                  />
                  <Line
                    label={t("home.overdue")}
                    value={n.overdue}
                    tone={n.overdue ? "warning" : undefined}
                  />
                  <Line
                    label={t("home.openReferrals")}
                    value={n.openReferrals}
                  />
                </dl>
              </div>
            </Card>
          </Link>

          <BarTrend
            title={t("home.weekTitle")}
            color="var(--chart-sys)"
            suppressedLabel="–"
            readout={(b) =>
              `${b.long} · ${t("home.readings", { count: b.value ?? 0 })}` +
              (b.flagged ? ` · ${t("home.flagged", { count: b.flagged })}` : "")
            }
            bars={n.week.map((d) => ({
              label: new Intl.DateTimeFormat(i18n.language, {
                weekday: "narrow",
              }).format(d.day),
              long: new Intl.DateTimeFormat(i18n.language, {
                weekday: "long",
                day: "numeric",
                month: "short",
              }).format(d.day),
              flagged: d.flagged,
              value: d.readings,
            }))}
          />

          <div className="grid grid-cols-2 gap-4">
            <Condition
              icon={<HeartPulse />}
              title={t("home.highBp")}
              count={n.highBp.count}
              of={n.highBp.of}
              hint={t("home.ofPatients", { count: n.highBp.of })}
            />
            <Condition
              icon={<Thermometer />}
              title={t("home.feverWeek")}
              count={n.fever.count}
              of={n.fever.of}
              hint={t("home.ofPatients", { count: n.fever.of })}
            />
          </div>

          <Card className="p-4">
            <div className="mb-2 flex items-center justify-between">
              <h2 className="text-base font-semibold text-heading">
                {t("home.nextVisits")}
              </h2>
              <Link
                to="/appointments"
                className="-mr-2 inline-flex h-10 items-center rounded-lg px-2 text-sm font-semibold text-secondary-foreground"
              >
                {t("home.seeAll")}
              </Link>
            </div>
            {n.nextVisits.length === 0 ? (
              <p className="flex items-center gap-2 py-2 text-sm text-muted-foreground">
                <CalendarDays className="size-5" aria-hidden />
                {t("home.noNextVisits")}
              </p>
            ) : (
              <ul className="divide-y divide-border/60">
                {n.nextVisits.map((a) => (
                  <li key={a.id}>
                    <Link
                      to={`/patients/${a.patientId}`}
                      className="flex items-center gap-3 py-3"
                    >
                      <Monogram name={a.patient ? fullName(a.patient) : "?"} />
                      <span className="min-w-0 flex-1">
                        <span className="block text-sm font-semibold break-words text-foreground">
                          {a.patient ? fullName(a.patient) : "—"}
                        </span>
                        <span className="block text-xs text-muted-foreground">
                          {t(`appointmentType.${a.type}`)} · {a.facility}
                        </span>
                      </span>
                      <span className="shrink-0 text-right text-xs">
                        <span className="block font-semibold text-heading first-letter:uppercase">
                          {formatRelative(a.scheduledAt)}
                        </span>
                        <span className="block text-muted-foreground">
                          {formatTime(a.scheduledAt)}
                        </span>
                      </span>
                    </Link>
                  </li>
                ))}
              </ul>
            )}
          </Card>

          <Link to="/sync" className="block">
            <Card className="flex items-center gap-4 p-4">
              <CloudUpload
                className={cn(
                  "size-6 shrink-0",
                  n.toSend ? "text-warning" : "text-primary"
                )}
                aria-hidden
              />
              <div className="flex-1">
                <p className="text-sm font-semibold text-foreground">
                  {t("nav.sync")}
                </p>
                <p className="text-xs text-muted-foreground">
                  {n.toSend
                    ? t("sync.waiting", { count: n.toSend })
                    : t("sync.allSent")}
                </p>
              </div>
              <ChevronRight
                className="size-5 text-muted-foreground"
                aria-hidden
              />
            </Card>
          </Link>
        </>
      ) : null}
    </div>
  )
}

/** One of the profile card's numbers. */
function Figure({
  value,
  label,
  tone,
}: {
  value: number | undefined
  label: string
  tone?: "warning"
}) {
  return (
    <div className="flex min-w-0 flex-col-reverse">
      <dt className="text-xs leading-tight text-muted-foreground">{label}</dt>
      <dd
        className={cn(
          "text-xl font-semibold tabular-nums",
          tone === "warning" ? "text-warning" : "text-heading"
        )}
      >
        {value ?? "–"}
      </dd>
    </div>
  )
}

function Line({
  label,
  value,
  tone,
}: {
  label: string
  value: number
  tone?: "warning"
}) {
  return (
    <div className="flex items-baseline justify-between gap-3">
      <dt className="text-muted-foreground">{label}</dt>
      <dd
        className={cn(
          "text-base font-semibold tabular-nums",
          tone === "warning" ? "text-warning" : "text-primary"
        )}
      >
        {value}
      </dd>
    </div>
  )
}

/** The kit's progress ring: visits done today out of today's visits. */
function Ring({
  done,
  total,
  label,
}: {
  done: number
  total: number
  label: string
}) {
  const r = 42
  const c = 2 * Math.PI * r
  const part = total ? done / total : 0
  return (
    <svg
      viewBox="0 0 100 100"
      className="size-28 shrink-0"
      role="img"
      aria-label={label}
    >
      <circle
        cx="50"
        cy="50"
        r={r}
        fill="none"
        stroke="var(--secondary)"
        strokeWidth="10"
      />
      {part > 0 ? (
        <circle
          cx="50"
          cy="50"
          r={r}
          fill="none"
          stroke="var(--primary)"
          strokeWidth="10"
          strokeLinecap="round"
          strokeDasharray={`${c * part} ${c}`}
          transform="rotate(-90 50 50)"
        />
      ) : null}
      <text
        x="50"
        y="50"
        textAnchor="middle"
        dominantBaseline="central"
        className="fill-heading text-[22px] font-semibold"
      >
        {done}/{total}
      </text>
    </svg>
  )
}

/** Half-width card: how many patients have a condition, with a bar for the share. */
function Condition({
  icon,
  title,
  count,
  of,
  hint,
}: {
  icon: ReactNode
  title: string
  count: number
  of: number
  hint: string
}) {
  const share = of ? count / of : 0
  return (
    <Card className="flex flex-col gap-2 p-4 [&>svg]:size-6 [&>svg]:text-warning">
      {icon}
      <h2 className="text-sm leading-tight font-semibold text-heading">
        {title}
      </h2>
      <p className="mt-auto text-[1.75rem] leading-8 font-semibold text-heading tabular-nums">
        {count}
      </p>
      <p className="text-xs text-muted-foreground">{hint}</p>
      <div
        className="h-2 overflow-hidden rounded-full bg-secondary"
        role="img"
        aria-label={`${count} / ${of}`}
      >
        <div
          className="h-full rounded-full bg-warning"
          style={{ width: `${Math.round(share * 100)}%` }}
        />
      </div>
    </Card>
  )
}
