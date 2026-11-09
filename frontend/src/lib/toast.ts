/**
 * Short pop-up messages ("Vitals saved", "Couldn't save on this phone"): one place, one look, for
 * every screen. Call `toast.success(...)` or `toast.error(...)` from anywhere, even right before a
 * `navigate()`; the message lives outside the router so it survives the change of screen.
 */

export type Kind = "success" | "error" | "info"
export type Toast = {
  id: number
  kind: Kind
  title: string
  description?: string
  leaving: boolean
}

/** How long each kind stays up. Errors stay longer: they usually need reading. */
const DURATION: Record<Kind, number> = {
  success: 3500,
  info: 4500,
  error: 7000,
}
const EXIT_MS = 200
const MAX_VISIBLE = 3

let toasts: Toast[] = []
let nextId = 1
const listeners = new Set<() => void>()
const timers = new Map<number, number>()

function emit() {
  listeners.forEach((l) => l())
}

function patch(id: number, change: Partial<Toast>) {
  toasts = toasts.map((t) => (t.id === id ? { ...t, ...change } : t))
}

export function dismiss(id: number) {
  const item = toasts.find((t) => t.id === id)
  if (!item || item.leaving) return
  window.clearTimeout(timers.get(id))
  patch(id, { leaving: true })
  emit()
  // Let the exit animation play before the message is removed.
  timers.set(
    id,
    window.setTimeout(() => {
      timers.delete(id)
      toasts = toasts.filter((t) => t.id !== id)
      emit()
    }, EXIT_MS)
  )
}

function show(kind: Kind, title: string, description?: string) {
  // The same message again (a double tap) restarts the one on screen instead of stacking a copy.
  const same = toasts.find(
    (t) => !t.leaving && t.kind === kind && t.title === title
  )
  const id = same?.id ?? nextId++
  if (same) {
    patch(id, { description })
    window.clearTimeout(timers.get(id))
  } else {
    toasts = [...toasts, { id, kind, title, description, leaving: false }]
    toasts
      .filter((t) => !t.leaving)
      .slice(0, -MAX_VISIBLE)
      .forEach((t) => dismiss(t.id))
  }
  timers.set(
    id,
    window.setTimeout(() => dismiss(id), DURATION[kind])
  )
  emit()
  return id
}

export const toast = {
  success: (title: string, description?: string) =>
    show("success", title, description),
  error: (title: string, description?: string) =>
    show("error", title, description),
  info: (title: string, description?: string) =>
    show("info", title, description),
  dismiss,
  /** Tests: start each one with no messages. */
  clear() {
    timers.forEach((t) => window.clearTimeout(t))
    timers.clear()
    toasts = []
    emit()
  },
}

export function subscribe(listener: () => void) {
  listeners.add(listener)
  return () => listeners.delete(listener)
}
export const snapshot = () => toasts
