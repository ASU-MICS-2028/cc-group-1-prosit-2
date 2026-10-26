import { useLiveQuery } from "dexie-react-hooks"
import { CalendarDays, CloudUpload, UserPlus, Users } from "lucide-react"
import type { ReactNode } from "react"
import { useTranslation } from "react-i18next"
import { Link } from "react-router-dom"
import { useSession } from "@/auth/session"
import { buttonVariants } from "@/components/ui/button"
import { db } from "@/db/local"
import { cn } from "@/lib/utils"
import { useOutbox } from "@/sync/sync"

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

  return (
    <div className="space-y-6">
      <header>
        <p className="text-sm text-muted-foreground">{t("home.greeting")}</p>
        <h1 className="text-2xl font-semibold text-foreground">
          {session?.user.name}
        </h1>
      </header>

      <Link
        to="/patients/new"
        className={cn(buttonVariants({ size: "xl" }), "w-full")}
      >
        <UserPlus aria-hidden />
        {t("home.registerPatient")}
      </Link>

      <div className="grid grid-cols-3 gap-3">
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
        <Stat
          to="/sync"
          icon={<CloudUpload />}
          value={waiting}
          label={t("home.waiting")}
        />
      </div>
    </div>
  )
}

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
    <Link
      to={to}
      className="space-y-1 rounded-2xl bg-card p-3 ring-1 ring-border [&_svg]:size-5 [&_svg]:text-primary"
    >
      {icon}
      <p className="text-2xl font-semibold text-foreground">{value ?? "–"}</p>
      <p className="text-xs text-muted-foreground">{label}</p>
    </Link>
  )
}
