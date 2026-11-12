import { useCallback, useEffect, useState } from "react"
import {
  paymentsAvailable,
  paymentStatus,
  type PaymentStatus,
} from "@/api/payments"
import type { Appointment } from "@/db/types"
import { useOnline } from "@/lib/useOnline"

/** Why the fee isn't known: the visit hasn't reached the server yet, there's no signal, or the server didn't answer. */
export type PaymentProblem = "unsynced" | "offline" | "error"

/**
 * The fee for one visit, asked of the server. Until the answer is in, nobody may assume there's no
 * fee: the call button waits for it (an offline-booked visit the server doesn't have yet would
 * otherwise look free).
 */
export function usePayment(a: Pick<Appointment, "id" | "type" | "status">) {
  const online = useOnline()
  const [payment, setPayment] = useState<PaymentStatus | null>(null)
  const [failure, setFailure] = useState<"unsynced" | "error" | null>(null)
  const [version, setVersion] = useState(0)
  const wanted = a.type === "teleconsult" && a.status === "scheduled"
  const available = paymentsAvailable()

  useEffect(() => {
    if (!wanted || !available || !online) return
    let current = true
    paymentStatus(a.id).then(
      (p) => {
        if (!current) return
        setPayment(p)
        setFailure(null)
      },
      (e: { status?: number }) => {
        if (current) setFailure(e.status === 404 ? "unsynced" : "error")
      }
    )
    return () => {
      current = false
    }
  }, [a.id, wanted, available, online, version])

  const refresh = useCallback(() => setVersion((v) => v + 1), [])
  // The offline demo has no payments at all: treat it as known and free.
  const problem: PaymentProblem | null = !available
    ? null
    : !online && !payment
      ? "offline"
      : failure
  const known = !available || payment !== null
  return { payment, known, problem, refresh }
}
