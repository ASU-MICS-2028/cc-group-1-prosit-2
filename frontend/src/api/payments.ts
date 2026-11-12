import { isDemoSession } from "@/auth/session"
import { DEMO_MODE } from "./auth"
import { api } from "./client"

/** Where a visit's fee stands (GET /api/payments/{appointmentId}). Amounts are in pesewas. */
export interface PaymentStatus {
  appointmentId: string
  /** false while the environment has no Paystack keys: no fee is asked */
  enabled: boolean
  status:
    | "not_required"
    | "exempt"
    | "unpaid"
    | "pending"
    | "paid"
    | "failed"
    | "abandoned"
  amountPesewas: number
  currency: string
  reference: string | null
  channel: string | null
  paidAt: string | null
}

export interface Checkout {
  reference: string
  authorizationUrl: string
  amountPesewas: number
  currency: string
}

export interface PaymentListItem {
  reference: string
  appointmentId: string
  district: string
  amountPesewas: number
  status: PaymentStatus["status"]
  channel: string | null
  collectedBy: string
  createdAt: string
  paidAt: string | null
  facility: string
  scheduledAt: string
}

export interface PaymentList {
  district: string | null
  paidCount: number
  paidPesewas: number
  pendingCount: number
  items: PaymentListItem[]
}

/** Payments need the server: the offline demo has none. */
export const paymentsAvailable = () => !DEMO_MODE && !isDemoSession()

/** A fee still stands between the visit and its call. */
export const mustPay = (p: PaymentStatus | null | undefined) =>
  !!p &&
  p.enabled &&
  (p.status === "unpaid" ||
    p.status === "pending" ||
    p.status === "failed" ||
    p.status === "abandoned")

export function cedis(pesewas: number, language?: string) {
  return new Intl.NumberFormat(language, {
    style: "currency",
    currency: "GHS",
  }).format(pesewas / 100)
}

export const paymentStatus = (appointmentId: string) =>
  api<PaymentStatus>(`/payments/${appointmentId}`)

/** Starts a mobile-money payment; Paystack sends the person back to this visit's payment page. */
export const startCheckout = (appointmentId: string) =>
  api<Checkout>(`/payments/${appointmentId}/checkout`, {
    method: "POST",
    body: {
      callbackUrl: `${window.location.origin}/appointments/${appointmentId}/payment`,
    },
  })

export const paymentList = () => api<PaymentList>("/payments")
