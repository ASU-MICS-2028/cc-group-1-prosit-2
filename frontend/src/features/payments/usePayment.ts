import { useCallback, useEffect, useState } from "react"
import {
  paymentsAvailable,
  paymentStatus,
  type PaymentStatus,
} from "@/api/payments"
import type { Appointment } from "@/db/types"
import { useOnline } from "@/lib/useOnline"

/** The fee for one visit, asked of the server when there is a signal. null: not asked (offline, demo, or no fee). */
export function usePayment(a: Pick<Appointment, "id" | "type" | "status">) {
  const online = useOnline()
  const [payment, setPayment] = useState<PaymentStatus | null>(null)
  const [version, setVersion] = useState(0)
  const active =
    a.type === "teleconsult" &&
    a.status === "scheduled" &&
    online &&
    paymentsAvailable()

  useEffect(() => {
    if (!active) return
    let current = true
    // Not on the server yet (offline booking) or no signal: leave it unknown; the call page says why.
    paymentStatus(a.id).then(
      (p) => current && setPayment(p),
      () => undefined
    )
    return () => {
      current = false
    }
  }, [a.id, active, version])

  const refresh = useCallback(() => setVersion((v) => v + 1), [])
  return { payment, refresh }
}
