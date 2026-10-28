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

/** "2 days ago", "in 3 hours", "yesterday", in the app's language. */
export function formatRelative(iso: string, now = Date.now()): string {
  const diff = new Date(iso).getTime() - now
  const rtf = new Intl.RelativeTimeFormat(i18n.language, { numeric: "auto" })
  const minutes = Math.round(diff / 60_000)
  if (Math.abs(minutes) < 60) return rtf.format(minutes, "minute")
  const hours = Math.round(minutes / 60)
  if (Math.abs(hours) < 24) return rtf.format(hours, "hour")
  return rtf.format(Math.round(hours / 24), "day")
}

/** A time of day in the app's language, e.g. "14:30". */
export function formatTime(iso: string): string {
  return new Intl.DateTimeFormat(i18n.language, { timeStyle: "short" }).format(
    new Date(iso)
  )
}
