import { useEffect, useState } from "react"
import { useTranslation } from "react-i18next"
import { myHealth, type MyHealth } from "@/api/my"
import { useSession } from "@/auth/session"
import { Clock, HeartPulse, Thermometer, Weight } from "@/components/icons"
import { Card, EmptyState, PageHeader } from "@/components/PageHeader"
import type { Appointment } from "@/db/types"
import { TeleconsultJoin } from "@/features/payments/TeleconsultJoin"
import { formatDate, formatDateTime } from "@/lib/dates"
import { cn } from "@/lib/utils"

const joinButton =
  "inline-flex h-11 w-full items-center justify-center gap-2 rounded-lg bg-primary px-3 text-sm font-semibold text-primary-foreground"

/**
 * A patient's own page: their upcoming and past visits, their latest readings, and for a
 * teleconsult the fee and the call, from their own phone. Read-only: the CHV records visits and
 * vitals. A household sharing one phone sees everyone registered with it, named on each card.
 */
export function Component() {
  const { t } = useTranslation()
  const session = useSession()
  const [data, setData] = useState<MyHealth | "error" | null>(null)

  useEffect(() => {
    myHealth().then(setData, () => setData("error"))
  }, [])

  const title = t("my.title")
  if (data === null) return <PageHeader title={title} />
  if (data === "error")
    return (
      <>
        <PageHeader title={title} />
        <EmptyState illustration="offline">{t("my.offline")}</EmptyState>
      </>
    )

  const nameOf = (patientId: string) =>
    data.people.find((p) => p.id === patientId)?.name ?? ""
  const household = data.people.length > 1
  const now = new Date().toISOString()
  const upcoming = data.appointments
    .filter(
      (a) => a.status === "scheduled" && a.scheduledAt >= now.slice(0, 10)
    )
    .sort((a, b) => a.scheduledAt.localeCompare(b.scheduledAt))
  const past = data.appointments.filter((a) => !upcoming.includes(a))

  return (
    <>
      <PageHeader
        title={title}
        subtitle={t("my.hello", { name: session?.user.name ?? "" })}
      />
      <p className="mb-4 text-sm text-muted-foreground">{t("my.readOnly")}</p>

      <h2 className="mb-2 text-base font-semibold text-heading">
        {t("my.upcoming")}
      </h2>
      {upcoming.length === 0 ? (
        <p className="mb-6 text-sm text-muted-foreground">
          {t("my.noUpcoming")}
        </p>
      ) : (
        <ul className="mb-6 space-y-3">
          {upcoming.map((a) => (
            <Visit
              key={a.id}
              a={a}
              who={household ? nameOf(a.patientId) : ""}
            />
          ))}
        </ul>
      )}

      <h2 className="mb-2 text-base font-semibold text-heading">
        {t("my.readings")}
      </h2>
      {data.vitals.length === 0 ? (
        <p className="mb-6 text-sm text-muted-foreground">
          {t("my.noReadings")}
        </p>
      ) : (
        <ul className="mb-6 space-y-2">
          {data.vitals.slice(0, 10).map((v) => (
            <li key={v.id}>
              <Card className="space-y-1 p-4 text-sm">
                <p className="font-semibold text-heading">
                  {formatDate(v.takenAt)}
                  {household ? ` · ${nameOf(v.patientId)}` : ""}
                </p>
                <p className="flex flex-wrap gap-x-4 gap-y-1 text-foreground">
                  {v.systolic && v.diastolic ? (
                    <span className="inline-flex items-center gap-1">
                      <HeartPulse className="size-4 text-primary" aria-hidden />
                      {t("my.bp", { sys: v.systolic, dia: v.diastolic })}
                    </span>
                  ) : null}
                  {v.temperatureC ? (
                    <span className="inline-flex items-center gap-1">
                      <Thermometer
                        className="size-4 text-primary"
                        aria-hidden
                      />
                      {t("my.temp", { value: v.temperatureC })}
                    </span>
                  ) : null}
                  {v.weightKg ? (
                    <span className="inline-flex items-center gap-1">
                      <Weight className="size-4 text-primary" aria-hidden />
                      {t("my.weight", { value: v.weightKg })}
                    </span>
                  ) : null}
                </p>
              </Card>
            </li>
          ))}
        </ul>
      )}

      {past.length > 0 ? (
        <>
          <h2 className="mb-2 text-base font-semibold text-heading">
            {t("my.past")}
          </h2>
          <ul className="space-y-2">
            {past.slice(0, 20).map((a) => (
              <li key={a.id}>
                <Card className="p-4 text-sm">
                  <p className="font-semibold text-heading">
                    {t(`appointmentType.${a.type}`)} · {a.facility}
                  </p>
                  <p className="text-muted-foreground">
                    {formatDateTime(a.scheduledAt)} · {t(`status.${a.status}`)}
                    {household ? ` · ${nameOf(a.patientId)}` : ""}
                  </p>
                </Card>
              </li>
            ))}
          </ul>
        </>
      ) : null}
    </>
  )
}

function Visit({ a, who }: { a: Appointment; who: string }) {
  const { t } = useTranslation()
  return (
    <li>
      <Card
        className={cn(
          "space-y-3 p-4 text-sm",
          a.priority === "emergency" && "border-2 border-destructive"
        )}
      >
        <div>
          <p className="font-semibold text-heading">
            {t(`appointmentType.${a.type}`)}
            {who ? ` · ${who}` : ""}
          </p>
          <p className="text-muted-foreground">{a.facility}</p>
          <p className="mt-1 flex items-center gap-1.5 text-muted-foreground">
            <Clock className="size-4" aria-hidden />
            {formatDateTime(a.scheduledAt)}
          </p>
        </div>
        {a.type === "teleconsult" && a.referralStatus !== "declined" ? (
          <TeleconsultJoin a={a} canCollect className={joinButton} />
        ) : null}
      </Card>
    </li>
  )
}
