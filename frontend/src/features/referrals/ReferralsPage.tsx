import { useLiveQuery } from "dexie-react-hooks"
import { Check, CircleCheckBig, Siren, X } from "@/components/icons"
import { useState } from "react"
import { useTranslation } from "react-i18next"
import { Link } from "react-router-dom"
import { useSession } from "@/auth/session"
import { Card, EmptyState, Monogram, PageHeader } from "@/components/PageHeader"
import { Button } from "@/components/ui/button"
import { db, saveAndQueue } from "@/db/local"
import type { Appointment, Priority, ReferralStatus } from "@/db/types"
import { fullName } from "@/features/patients/usePatients"
import { formatDateTime } from "@/lib/dates"
import { cn } from "@/lib/utils"
import { ReferralStatusChip } from "./ReferralStatusChip"

const PRIORITY_ORDER: Record<Priority, number> = {
  emergency: 0,
  urgent: 1,
  routine: 2,
}
type Tab = "open" | "done"

/**
 * The nurse's or doctor's referral inbox: requests from CHVs, emergencies first, then urgent,
 * then by appointment time. Accept or decline a new request; mark an accepted one as seen.
 * Changes go through the outbox like any other record, so this works offline too.
 */
export function Component() {
  const { t } = useTranslation()
  const session = useSession()
  const [tab, setTab] = useState<Tab>("open")
  const rows = useLiveQuery(async () => {
    const list = (await db.appointments.toArray()).filter(
      (a) => a.referralStatus
    )
    const patients = await db.patients.bulkGet(list.map((a) => a.patientId))
    return list
      .map((a, i) => ({ ...a, patient: patients[i] }))
      .sort(
        (a, b) =>
          PRIORITY_ORDER[a.priority] - PRIORITY_ORDER[b.priority] ||
          a.scheduledAt.localeCompare(b.scheduledAt)
      )
  }, [])
  const open =
    rows?.filter(
      (r) => r.referralStatus === "requested" || r.referralStatus === "accepted"
    ) ?? []
  const done =
    rows?.filter(
      (r) => r.referralStatus === "seen" || r.referralStatus === "declined"
    ) ?? []
  const shown = tab === "open" ? open : done

  async function move(a: Appointment, referralStatus: ReferralStatus) {
    const now = new Date().toISOString()
    await saveAndQueue(
      "appointment",
      {
        ...a,
        referralStatus,
        status:
          referralStatus === "seen"
            ? "completed"
            : referralStatus === "declined"
              ? "cancelled"
              : a.status,
        handledBy: session?.user.id,
        handledAt: now,
        updatedAt: now,
      },
      "update"
    )
  }

  return (
    <>
      <PageHeader title={t("referrals.title")} />
      {/* Figma "Día / Semana / Mes" segmented control */}
      <div
        role="tablist"
        className="mb-4 grid grid-cols-2 gap-1 rounded-xl bg-card p-1 shadow-card"
      >
        {(["open", "done"] as const).map((k) => (
          <button
            key={k}
            role="tab"
            aria-selected={tab === k}
            onClick={() => setTab(k)}
            className={cn(
              "h-11 rounded-md text-sm font-semibold",
              tab === k
                ? "bg-primary text-primary-foreground"
                : "text-foreground hover:bg-muted"
            )}
          >
            {t(`referrals.${k}`, {
              count: (k === "open" ? open : done).length,
            })}
          </button>
        ))}
      </div>
      {rows && shown.length === 0 ? (
        <EmptyState
          illustration={tab === "open" ? "empty-referrals" : "no-data"}
        >
          {tab === "open" ? t("referrals.emptyOpen") : t("referrals.emptyDone")}
        </EmptyState>
      ) : (
        <ul className="space-y-3">
          {shown.map((a) => (
            <li key={a.id}>
              <Card
                className={cn(
                  "space-y-3 p-4",
                  a.priority === "emergency" && "border-2 border-destructive"
                )}
              >
                <div className="flex items-start gap-3">
                  <Monogram name={a.patient ? fullName(a.patient) : "?"} />
                  <div className="min-w-0 flex-1">
                    <Link
                      to={`/patients/${a.patientId}`}
                      className="block truncate text-sm font-semibold text-foreground underline-offset-2 hover:underline"
                    >
                      {a.patient ? fullName(a.patient) : "—"}
                    </Link>
                    <p className="text-xs text-muted-foreground">
                      {t(`appointmentType.${a.type}`)} · {a.facility} ·{" "}
                      {formatDateTime(a.scheduledAt)}
                    </p>
                  </div>
                  <ReferralStatusChip status={a.referralStatus!} />
                </div>
                {a.priority !== "routine" ? (
                  <p
                    className={cn(
                      "flex items-center gap-1.5 text-sm font-semibold",
                      a.priority === "emergency"
                        ? "text-destructive"
                        : "text-warning"
                    )}
                  >
                    <Siren className="size-4" aria-hidden />
                    {t(`priority.${a.priority}`)}
                  </p>
                ) : null}
                <p className="text-sm text-foreground">{a.reason}</p>
                {a.referralStatus === "requested" ? (
                  <div className="grid grid-cols-2 gap-2">
                    <Button size="lg" onClick={() => void move(a, "accepted")}>
                      <Check aria-hidden /> {t("referrals.accept")}
                    </Button>
                    <Button
                      size="lg"
                      variant="secondary"
                      onClick={() => void move(a, "declined")}
                    >
                      <X aria-hidden /> {t("referrals.decline")}
                    </Button>
                  </div>
                ) : a.referralStatus === "accepted" ? (
                  <Button
                    size="lg"
                    variant="secondary"
                    className="w-full"
                    onClick={() => void move(a, "seen")}
                  >
                    <CircleCheckBig aria-hidden /> {t("referrals.markSeen")}
                  </Button>
                ) : null}
              </Card>
            </li>
          ))}
        </ul>
      )}
    </>
  )
}
