import { useSyncExternalStore } from "react"

/**
 * Healthcare worker levels (Germain's role-permission matrix decides what each may see and do;
 * the API enforces it, the app only hides what a role cannot use).
 */
export type Role = "chv" | "nurse" | "doctor" | "district_officer" | "admin"

export interface User {
  id: string
  name: string
  role: Role
  /** The district the person works in; officers see this district's summary */
  district: string
}

/** Who is signed in on this phone. Kept on the phone so the app works offline until the token expires. */
export interface Session {
  token: string
  /** ISO time the token stops working */
  expiresAt: string
  user: User
}

const STORAGE_KEY = "healthconnect.session"
const listeners = new Set<() => void>()
let cached: { raw: string | null; session: Session | null } | undefined

function parse(raw: string | null): Session | null {
  try {
    return raw ? (JSON.parse(raw) as Session) : null
  } catch {
    return null
  }
}

function read(): Session | null {
  let raw: string | null
  try {
    raw = localStorage.getItem(STORAGE_KEY)
  } catch {
    return cached?.session ?? null // storage blocked: only this page knows
  }
  // Same text, same object: React needs a stable value between renders.
  if (cached && cached.raw === raw) return cached.session
  cached = { raw, session: parse(raw) }
  return cached.session
}

/** The signed-in person, or null when nobody is or the token has expired. */
export function getSession(): Session | null {
  const session = read()
  if (!session) return null
  return Date.parse(session.expiresAt) > Date.now() ? session : null
}

export function saveSession(session: Session) {
  try {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(session))
  } catch {
    // storage blocked: the person stays signed in until the page closes
    cached = { raw: JSON.stringify(session), session }
  }
  listeners.forEach((notify) => notify())
}

/** Sign out: removes the token from this phone. Unsynced records stay until they are sent. */
export function clearSession() {
  try {
    localStorage.removeItem(STORAGE_KEY)
  } catch {
    // nothing to remove
  }
  cached = undefined
  listeners.forEach((notify) => notify())
}

function subscribe(notify: () => void) {
  listeners.add(notify)
  return () => listeners.delete(notify)
}

export function useSession(): Session | null {
  return useSyncExternalStore(subscribe, getSession, () => null)
}

/** Where a signed-in person starts. */
export function homeFor(role: Role): string {
  if (role === "admin") return "/audit"
  return role === "district_officer" ? "/surveillance" : "/"
}

/** Roles that work with individual patients (the officer view is anonymised counts only). */
export const CLINICAL_ROLES: readonly Role[] = ["chv", "nurse", "doctor"]
