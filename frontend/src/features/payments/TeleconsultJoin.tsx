import { useState } from "react"
import { useTranslation } from "react-i18next"
import { Link } from "react-router-dom"
import { cedis, mustPay, startCheckout } from "@/api/payments"
import { useOnline } from "@/lib/useOnline"
import { CircleCheckBig, Video } from "@/components/icons"
import type { Appointment } from "@/db/types"
import { cn } from "@/lib/utils"
import { usePayment } from "./usePayment"

type PayError = "sync" | "off" | "provider" | "offline" | "other"

/**
 * A teleconsult's fee and its call button, together: until the fee is paid the button is a
 * "Pay with mobile money" button (the CHV, who is with the patient, collects it), and the call
 * opens once it's paid. Emergencies never wait for money. Paystack's own page takes the payment,
 * so the app never sees a MoMo PIN or a card.
 */
export function TeleconsultJoin({
  a,
  canCollect,
  className,
}: {
  a: Appointment
  /** The CHV collects the fee; a nurse or doctor only sees whether it's paid. */
  canCollect: boolean
  className: string
}) {
  const { t, i18n } = useTranslation()
  const online = useOnline()
  const { payment } = usePayment(a)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<PayError | null>(null)

  async function pay() {
    if (!online) return setError("offline")
    setBusy(true)
    setError(null)
    try {
      const checkout = await startCheckout(a.id)
      window.location.assign(checkout.authorizationUrl)
    } catch (e) {
      const status = (e as { status?: number }).status
      setError(
        status === 404
          ? "sync"
          : status === 503
            ? "off"
            : status === 502
              ? "provider"
              : "other"
      )
      setBusy(false)
    }
  }

  const amount = payment ? cedis(payment.amountPesewas, i18n.language) : ""
  const join = (
    <Link to={`/appointments/${a.id}/call`} className={className}>
      <Video className="size-5" aria-hidden />
      {t("call.join")}
    </Link>
  )

  if (!mustPay(payment)) {
    return (
      <div className="space-y-2">
        {payment?.status === "paid" ? (
          <p className="flex items-center gap-1.5 text-sm font-medium text-success">
            <CircleCheckBig className="size-4" aria-hidden />
            {t("payments.paidChip", {
              amount: cedis(payment.amountPesewas, i18n.language),
            })}
          </p>
        ) : payment?.status === "exempt" ? (
          <p className="text-sm text-muted-foreground">
            {t("payments.exempt")}
          </p>
        ) : null}
        {join}
      </div>
    )
  }

  return (
    <div className="space-y-2 rounded-lg bg-warning-soft p-3">
      <p className="text-sm font-semibold text-warning">
        {payment!.status === "pending"
          ? t("payments.pending", { amount })
          : t("payments.due", { amount })}
      </p>
      {canCollect ? (
        <>
          <button
            type="button"
            onClick={() => void pay()}
            disabled={busy}
            className={cn(className, busy && "opacity-60")}
          >
            {busy ? t("payments.opening") : t("payments.pay", { amount })}
          </button>
          <p className="text-xs text-muted-foreground">{t("payments.how")}</p>
        </>
      ) : (
        <p className="text-xs text-muted-foreground">
          {t("payments.waitForChv")}
        </p>
      )}
      {error ? (
        <p role="alert" className="text-sm text-destructive">
          {t(`payments.error_${error}` as const)}
        </p>
      ) : null}
    </div>
  )
}
