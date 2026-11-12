import { useEffect, useState } from "react"
import { useTranslation } from "react-i18next"
import {
  cedis,
  paymentList,
  paymentsAvailable,
  type PaymentList,
  type PaymentListItem,
} from "@/api/payments"
import { useSession } from "@/auth/session"
import { Card, EmptyState, PageHeader } from "@/components/PageHeader"
import { formatDateTime } from "@/lib/dates"
import { cn } from "@/lib/utils"

const chip: Record<PaymentListItem["status"], string> = {
  paid: "bg-success-soft text-success",
  pending: "bg-warning-soft text-warning",
  failed: "bg-destructive-soft text-destructive",
  abandoned: "bg-muted text-muted-foreground",
  unpaid: "bg-muted text-muted-foreground",
  exempt: "bg-muted text-muted-foreground",
  not_required: "bg-muted text-muted-foreground",
}

/**
 * Consultation fees collected by mobile money: the district officer sees their district, the
 * auditor every district. Visits are shown by facility and time, never by patient.
 */
export function Component() {
  const { t, i18n } = useTranslation()
  const session = useSession()
  const [data, setData] = useState<PaymentList | "error" | "demo" | null>(() =>
    paymentsAvailable() ? null : "demo"
  )

  useEffect(() => {
    if (!paymentsAvailable()) return
    paymentList().then(setData, () => setData("error"))
  }, [])

  const title = t("payments.title")
  if (data === null) return <PageHeader title={title} />
  if (data === "error" || data === "demo")
    return (
      <>
        <PageHeader title={title} />
        <EmptyState illustration="offline">
          {data === "demo" ? t("payments.demo") : t("payments.offline")}
        </EmptyState>
      </>
    )

  const money = (p: number) => cedis(p, i18n.language)
  return (
    <>
      <PageHeader
        title={title}
        subtitle={
          data.district
            ? t("payments.forDistrict", { district: data.district })
            : t("payments.allDistricts")
        }
      />
      <div className="mb-4 grid grid-cols-2 gap-3">
        <Card className="p-4">
          <p className="text-xs text-muted-foreground">
            {t("payments.collected")}
          </p>
          <p className="text-2xl font-bold text-heading">
            {money(data.paidPesewas)}
          </p>
          <p className="text-xs text-muted-foreground">
            {t("payments.paidCount", { count: data.paidCount })}
          </p>
        </Card>
        <Card className="p-4">
          <p className="text-xs text-muted-foreground">
            {t("payments.waiting")}
          </p>
          <p className="text-2xl font-bold text-heading">{data.pendingCount}</p>
          <p className="text-xs text-muted-foreground">
            {t("payments.waitingHint")}
          </p>
        </Card>
      </div>
      {data.items.length === 0 ? (
        <EmptyState illustration="no-data">{t("payments.empty")}</EmptyState>
      ) : (
        <ul className="space-y-2">
          {data.items.map((p) => (
            <li key={p.reference}>
              <Card className="flex items-start justify-between gap-3 p-4 text-sm">
                <div className="min-w-0">
                  <p className="font-semibold text-heading">{p.facility}</p>
                  <p className="text-muted-foreground">
                    {t("payments.visitAt", {
                      when: formatDateTime(p.scheduledAt),
                    })}
                  </p>
                  <p className="text-muted-foreground">
                    {p.collectedBy === "patient"
                      ? t("payments.paidByPatient")
                      : t("payments.collectedBy", { name: p.collectedBy })}
                    {session?.user.role === "admin" ? ` · ${p.district}` : ""}
                  </p>
                  <p className="mt-1 font-mono text-xs break-all text-muted-foreground">
                    {p.reference}
                  </p>
                </div>
                <div className="shrink-0 text-right">
                  <p className="font-semibold text-heading">
                    {money(p.amountPesewas)}
                  </p>
                  <span
                    className={cn(
                      "mt-1 inline-block rounded-full px-2.5 py-0.5 text-xs font-semibold",
                      chip[p.status]
                    )}
                  >
                    {t(`payments.status_${p.status}` as const)}
                  </span>
                  {p.channel ? (
                    <p className="mt-1 text-xs text-muted-foreground">
                      {t(
                        `payments.channel_${p.channel === "card" ? "card" : "mobile_money"}` as const
                      )}
                    </p>
                  ) : null}
                </div>
              </Card>
            </li>
          ))}
        </ul>
      )}
    </>
  )
}
