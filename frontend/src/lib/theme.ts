import { useSyncExternalStore } from "react"

export type ThemePref = "system" | "light" | "dark"

const KEY = "healthconnect.theme"
const query = () =>
  typeof window.matchMedia === "function"
    ? window.matchMedia("(prefers-color-scheme: dark)")
    : null
const listeners = new Set<() => void>()

export function getThemePref(): ThemePref {
  try {
    const v = localStorage.getItem(KEY)
    return v === "light" || v === "dark" ? v : "system"
  } catch {
    return "system"
  }
}

/** Puts `.dark` on <html> for the choice (index.html does the same before the first paint). */
function apply(pref = getThemePref()) {
  const dark = pref === "dark" || (pref === "system" && !!query()?.matches)
  document.documentElement.classList.toggle("dark", dark)
}

/** Follow the phone's setting, or always light / always dark (bright sun vs night visits). */
export function setThemePref(pref: ThemePref) {
  try {
    localStorage.setItem(KEY, pref)
  } catch {
    // private mode: still applies for this visit
  }
  apply(pref)
  listeners.forEach((l) => l())
}

/** Re-applies when the phone switches between light and dark (sunset, battery saver). */
export function watchSystemTheme() {
  apply()
  query()?.addEventListener("change", () => apply())
}

export function useThemePref(): ThemePref {
  return useSyncExternalStore(
    (l) => (listeners.add(l), () => listeners.delete(l)),
    getThemePref,
    () => "system"
  )
}
