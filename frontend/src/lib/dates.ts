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
