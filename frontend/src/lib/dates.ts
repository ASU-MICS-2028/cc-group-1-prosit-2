import i18n from "@/i18n"

/** A date and time in the app's language, e.g. "26 Oct 2026, 14:30". */
export function formatDateTime(iso: string): string {
  return new Intl.DateTimeFormat(i18n.language, {
    dateStyle: "medium",
    timeStyle: "short",
  }).format(new Date(iso))
}

/** Value for an <input type="datetime-local"> from a Date, in local time. */
export function toLocalInput(date: Date): string {
  const pad = (n: number) => String(n).padStart(2, "0")
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`
}

/** A short date in the app's language, e.g. "26 Oct". */
export function formatDate(iso: string): string {
  return new Intl.DateTimeFormat(i18n.language, {
    day: "numeric",
    month: "short",
  }).format(new Date(iso))
}

/**
 * "2 days ago", "in 3 hours", "yesterday", in the app's language. The words come from our own
 * translations: browsers have no Twi or Hausa for this, and would fall back to English.
 */
export function formatRelative(iso: string, now = Date.now()): string {
  const t = i18n.t.bind(i18n)
  const minutes = Math.round((new Date(iso).getTime() - now) / 60_000)
  const hours = Math.round(minutes / 60)
  const days = Math.round(hours / 24)
  if (Math.abs(minutes) < 1) return t("time.justNow")
  if (Math.abs(minutes) < 60)
    return minutes < 0
      ? t("time.minutesAgo", { count: -minutes })
      : t("time.inMinutes", { count: minutes })
  if (Math.abs(hours) < 24)
    return hours < 0
      ? t("time.hoursAgo", { count: -hours })
      : t("time.inHours", { count: hours })
  if (days === -1) return t("time.yesterday")
  if (days === 1) return t("time.tomorrow")
  return days < 0
    ? t("time.daysAgo", { count: -days })
    : t("time.inDays", { count: days })
}

/** A time of day in the app's language, e.g. "14:30". */
export function formatTime(iso: string): string {
  return new Intl.DateTimeFormat(i18n.language, { timeStyle: "short" }).format(
    new Date(iso)
  )
}
