import { useLiveQuery } from "dexie-react-hooks"
import {
  CalendarDays,
  ChevronRight,
  CloudUpload,
  MapPin,
  TriangleAlert,
  UserPlus,
  Users,
} from "@/components/icons"
import type { ReactNode } from "react"
import { useTranslation } from "react-i18next"
import { Link } from "react-router-dom"
import { useSession } from "@/auth/session"
import { Card, Monogram } from "@/components/PageHeader"
import { buttonVariants } from "@/components/ui/button"
import { db } from "@/db/local"
import { cn } from "@/lib/utils"
import { URGENT_FLAGS, vitalsFlags } from "@/lib/vitals"
import { useOutbox } from "@/sync/sync"

/** Figma "Home Screen": a welcome line, the person's card, then one card per number. */
export function Component() {
  const { t } = useTranslation()
  const session = useSession()
  const patients = useLiveQuery(() => db.patients.count(), [])
  const waiting = useOutbox()?.length
  const upcoming = useLiveQuery(
    () =>
      db.appointments
        .where("scheduledAt")
        .aboveOrEqual(new Date().toISOString())
        .filter((a) => a.status === "scheduled")
        .count(),
    []
  )
  // Readings from the last 7 days that said "refer today"
  const urgent = useLiveQuery(async () => {
    const since = new Date(Date.now() - 7 * 86_400_000).toISOString()
    const recent = await db.vitals
      .where("takenAt")
      .aboveOrEqual(since)
      .toArray()
    return recent.filter((v) =>
      vitalsFlags(v).some((f) => URGENT_FLAGS.includes(f))
    ).length
  }, [])

  if (!session) return null
  const { user } = session

  return (
    <div className="space-y-4">
      <h1 className="mb-2 text-lg font-semibold text-heading">
        {t("home.greeting", { name: user.name.split(" ")[0] })}
      </h1>

      <Card className="flex items-center gap-4 p-4">
        <Monogram name={user.name} />
        <div className="min-w-0 flex-1">
          <p className="truncate text-base font-semibold text-foreground">
            {user.name}
          </p>
          <p className="text-xs font-medium text-muted-foreground">
            {t(`roles.${user.role}`)}
          </p>
          <p className="mt-1 flex items-center gap-1 text-xs text-muted-foreground">
            <MapPin className="size-4 text-primary" aria-hidden />
            {user.district}
          </p>
        </div>
      </Card>

      <Link
        to="/patients/new"
        className={cn(buttonVariants({ size: "xl" }), "w-full")}
      >
        <UserPlus aria-hidden />
        {t("home.registerPatient")}
      </Link>

      {urgent ? (
        <Link
          to="/patients"
          className="flex items-center gap-3 rounded-xl border border-destructive bg-destructive-soft p-4 text-destructive"
        >
          <TriangleAlert className="size-6 shrink-0" aria-hidden />
          <span className="flex-1 text-sm font-semibold">
            {t("home.urgent", { count: urgent })}
          </span>
          <ChevronRight className="size-5" aria-hidden />
        </Link>
      ) : null}

      <div className="grid grid-cols-2 gap-4">
        <Stat
          to="/patients"
          icon={<Users />}
          value={patients}
          label={t("home.patients")}
        />
        <Stat
          to="/appointments"
          icon={<CalendarDays />}
          value={upcoming}
          label={t("home.upcoming")}
        />
      </div>

      <Link to="/sync" className="block">
        <Card className="flex items-center gap-4 p-4">
          <CloudUpload
            className={cn(
              "size-6 shrink-0",
              waiting ? "text-warning" : "text-primary"
            )}
            aria-hidden
          />
          <div className="flex-1">
            <p className="text-sm font-semibold text-foreground">
              {t("nav.sync")}
            </p>
            <p className="text-xs text-muted-foreground">
              {waiting
                ? t("sync.waiting", { count: waiting })
                : t("sync.allSent")}
            </p>
          </div>
          <ChevronRight className="size-5 text-muted-foreground" aria-hidden />
        </Card>
      </Link>
    </div>
  )
}

/** A number card, in the style of the design's "Tus Pasos" figures. */
function Stat({
  to,
  icon,
  value,
  label,
}: {
  to: string
  icon: ReactNode
  value: number | undefined
  label: string
}) {
  return (
    <Link to={to} className="block">
      <Card className="h-full space-y-2 p-4 [&_svg]:size-6 [&_svg]:text-primary">
        {icon}
        <p className="text-[1.75rem] leading-8 font-semibold text-primary">
          {value ?? "–"}
        </p>
        <p className="text-xs font-medium text-muted-foreground">{label}</p>
      </Card>
    </Link>
  )
}
