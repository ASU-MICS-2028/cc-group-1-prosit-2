import { useLiveQuery } from "dexie-react-hooks"
import { useEffect, useState } from "react"
import { useTranslation } from "react-i18next"
import { Link, useParams } from "react-router-dom"
import { cedis, paymentStatus, type PaymentStatus } from "@/api/payments"
import { useSession, visitsPathFor } from "@/auth/session"
import { buttonVariants } from "@/components/ui/button"
import { CircleCheckBig, CircleX, Video } from "@/components/icons"
import { Card, PageHeader } from "@/components/PageHeader"
import { db } from "@/db/local"
import { fullName } from "@/features/patients/usePatients"
import { formatDateTime } from "@/lib/dates"
import { cn } from "@/lib/utils"

/** How long to keep asking: a MoMo approval on the patient's phone can take a minute. */
const TRIES = 30
const EVERY_MS = 3000

/**
 * Where Paystack sends the CHV back after the patient pays. The API checks the payment with
 * Paystack itself; this page asks the API every few seconds until it's settled.
 */
export function Component() {
  const { t, i18n } = useTranslation()
  const session = useSession()
  const { appointmentId = "" } = useParams()
  const appointment = useLiveQuery(
    () => db.appointments.get(appointmentId),
    [appointmentId]
  )
  const patient = useLiveQuery(
    () => (appointment ? db.patients.get(appointment.patientId) : undefined),
    [appointment?.patientId]
  )
  const [payment, setPayment] = useState<PaymentStatus | null>(null)
  const [tries, setTries] = useState(0)
  const [error, setError] = useState(false)

  useEffect(() => {
    let stop = false
    let timer: ReturnType<typeof setTimeout>
    let n = 0
    async function ask() {
      try {
        const p = await paymentStatus(appointmentId)
        if (stop) return
        setPayment(p)
        setError(false)
        if (p.status !== "pending" || ++n >= TRIES) return
      } catch {
        if (stop) return
        setError(true)
        if (++n >= TRIES) return
      }
      setTries(n)
      timer = setTimeout(() => void ask(), EVERY_MS)
    }
    void ask()
    return () => {
      stop = true
      clearTimeout(timer)
    }
  }, [appointmentId])

  const status = payment?.status
  const done = status === "paid" || status === "exempt"
  const settled = done || status === "failed" || status === "abandoned"
  const waiting = !settled && tries < TRIES

  return (
    <>
      <PageHeader
        title={t("payments.returnTitle")}
        back={visitsPathFor(session?.user.role)}
        subtitle={
          appointment
            ? `${patient ? fullName(patient) : ""} · ${formatDateTime(appointment.scheduledAt)}`
            : undefined
        }
      />
      <Card className="space-y-4 p-5 text-center">
        {done ? (
          <>
            <CircleCheckBig
              className="mx-auto size-14 text-success"
              aria-hidden
            />
            <p className="text-lg font-semibold text-heading" role="status">
              {status === "paid"
                ? t("payments.received", {
                    amount: cedis(payment!.amountPesewas, i18n.language),
                  })
                : t("payments.exempt")}
            </p>
            {payment?.paidAt ? (
              <p className="text-sm text-muted-foreground">
                {t("payments.receipt", {
                  reference: payment.reference,
                  when: formatDateTime(payment.paidAt),
                  channel: t(
                    `payments.channel_${payment.channel === "card" ? "card" : "mobile_money"}` as const
                  ),
                })}
              </p>
            ) : null}
            <Link
              to={`/appointments/${appointmentId}/call`}
              className={cn(buttonVariants({ size: "lg" }), "w-full")}
            >
              <Video aria-hidden /> {t("call.join")}
            </Link>
          </>
        ) : waiting ? (
          <>
            <div
              className="mx-auto size-12 animate-spin rounded-full border-4 border-muted border-t-primary"
              aria-hidden
            />
            <p className="text-lg font-semibold text-heading" role="status">
              {t("payments.checking")}
            </p>
            <p className="text-sm text-muted-foreground">
              {t("payments.checkingHint")}
            </p>
          </>
        ) : (
          <>
            <CircleX className="mx-auto size-14 text-destructive" aria-hidden />
            <p className="text-lg font-semibold text-heading" role="status">
              {error && !payment
                ? t("payments.cantCheck")
                : status === "pending"
                  ? t("payments.stillPending")
                  : t("payments.notPaid")}
            </p>
            <Link
              to={visitsPathFor(session?.user.role)}
              className={cn(buttonVariants({ size: "lg" }), "w-full")}
            >
              {t("payments.backToVisits")}
            </Link>
          </>
        )}
      </Card>
    </>
  )
}
