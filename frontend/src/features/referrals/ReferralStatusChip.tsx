import {
  CircleCheckBig,
  CircleDashed,
  CircleX,
  Clock,
} from "@/components/icons"
import { useTranslation } from "react-i18next"
import type { ReferralStatus } from "@/db/types"
import { cn } from "@/lib/utils"

const STYLE: Record<ReferralStatus, { icon: typeof Clock; className: string }> =
  {
    requested: { icon: Clock, className: "bg-warning-soft text-warning" },
    accepted: {
      icon: CircleDashed,
      className: "bg-secondary text-secondary-foreground",
    },
    seen: {
      icon: CircleCheckBig,
      className: "bg-secondary text-secondary-foreground",
    },
    declined: {
      icon: CircleX,
      className: "bg-destructive-soft text-destructive",
    },
  }

/** Where a referral stands, as an icon and a word (never colour alone). */
export function ReferralStatusChip({ status }: { status: ReferralStatus }) {
  const { t } = useTranslation()
  const { icon: Icon, className } = STYLE[status]
  return (
    <span
      className={cn(
        "inline-flex shrink-0 items-center gap-1 rounded-full px-2 py-0.5 text-xs font-semibold",
        className
      )}
    >
      <Icon className="size-3.5" aria-hidden />
      {t(`referralStatus.${status}`)}
    </span>
  )
}
