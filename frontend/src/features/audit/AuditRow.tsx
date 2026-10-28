import { useTranslation } from "react-i18next"
import { CircleX, Fingerprint, PenLine, Plus } from "@/components/icons"
import type { AuditEvent } from "@/db/types"
import { formatRelative, formatTime } from "@/lib/dates"
import { cn } from "@/lib/utils"

const ICON = {
  view: Fingerprint,
  create: Plus,
  update: PenLine,
  denied: CircleX,
}

/**
 * One line of the audit trail: who, what, which record, when. Refused attempts in red, so they
 * stand out. `patientName` is resolved by the caller (the log itself only stores ids).
 */
export function AuditRow({
  e,
  patientName,
  showPatient = true,
}: {
  e: AuditEvent
  patientName?: string
  showPatient?: boolean
}) {
  const { t } = useTranslation()
  const Icon = ICON[e.action]
  const denied = e.outcome === "denied"
  const what =
    e.entity === "page"
      ? t("audit.what_page", { path: e.detail ?? "" })
      : t(`audit.what_${e.entity}`)
  return (
    <li className="flex items-start gap-3 py-3">
      <span
        className={cn(
          "grid size-9 shrink-0 place-items-center rounded-full",
          denied
            ? "bg-destructive-soft text-destructive"
            : "bg-secondary text-secondary-foreground"
        )}
      >
        <Icon className="size-4" aria-hidden />
      </span>
      <div className="min-w-0 flex-1 text-sm">
        <p
          className={cn(
            "break-words",
            denied ? "text-destructive" : "text-foreground"
          )}
        >
          <span className="font-semibold">{e.actorName}</span>{" "}
          <span className="text-muted-foreground">
            ({t(`roles.${e.role}`, { defaultValue: e.role })})
          </span>{" "}
          {t(`audit.action_${e.action}`)} {what}
          {showPatient && patientName ? (
            <>
              {" · "}
              <span className="font-medium">{patientName}</span>
            </>
          ) : null}
        </p>
        <p className="text-xs text-muted-foreground">
          <span className="first-letter:uppercase">{formatRelative(e.at)}</span>{" "}
          · {formatTime(e.at)}
          {e.syncedAt ? null : ` · ${t("audit.onPhone")}`}
        </p>
      </div>
    </li>
  )
}
