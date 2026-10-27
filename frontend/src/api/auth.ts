import type { Session, User } from "@/auth/session"
import { api } from "./client"

/**
 * Sign-in with a phone number and a 6-digit SMS code (ADR 0004).
 *   POST /api/auth/code    { phone }        -> 204, an SMS is sent
 *   POST /api/auth/verify  { phone, code }  -> { token, expiresAt, user }
 *
 * Until the backend has these endpoints, demo mode (the default; VITE_AUTH_MODE=api turns it
 * off) answers locally for four synthetic health workers with the code 123456.
 */
export const DEMO_MODE = import.meta.env.VITE_AUTH_MODE !== "api"
export const DEMO_CODE = "123456"

export const DEMO_USERS: Record<string, User> = {
  "+233200000101": {
    id: "chv-042",
    name: "Akosua Mensah",
    role: "chv",
    district: "Shai-Osudoku",
  },
  "+233200000102": {
    id: "nurse-17",
    name: "Efua Asante",
    role: "nurse",
    district: "Shai-Osudoku",
  },
  "+233200000103": {
    id: "dr-eshun",
    name: "Kwesi Eshun",
    role: "doctor",
    district: "Shai-Osudoku",
  },
  "+233200000104": {
    id: "ghs-officer-07",
    name: "Yaw Darko",
    role: "district_officer",
    district: "Shai-Osudoku",
  },
}

export class AuthError extends Error {
  readonly reason: "unknown-number" | "wrong-code" | "network"
  constructor(reason: AuthError["reason"]) {
    super(reason)
    this.reason = reason
  }
}

/** 0241234567, 241234567 or +233241234567 -> +233241234567; null if it isn't a Ghana mobile number. */
export function normalisePhone(input: string): string | null {
  const digits = input.replace(/[\s-]/g, "")
  const m = digits.match(/^(?:\+233|233|0)?([2-9]\d{8})$/)
  return m ? `+233${m[1]}` : null
}

export async function requestCode(phone: string): Promise<void> {
  if (DEMO_MODE) {
    if (!DEMO_USERS[phone]) throw new AuthError("unknown-number")
    return
  }
  try {
    await api<void>("/auth/code", { method: "POST", body: { phone } })
  } catch (e) {
    throw new AuthError(
      (e as { status?: number }).status === 404 ? "unknown-number" : "network"
    )
  }
}

export async function verifyCode(
  phone: string,
  code: string
): Promise<Session> {
  if (DEMO_MODE) {
    const user = DEMO_USERS[phone]
    if (!user) throw new AuthError("unknown-number")
    if (code !== DEMO_CODE) throw new AuthError("wrong-code")
    return {
      token: `demo-${user.id}`,
      expiresAt: new Date(Date.now() + 7 * 86_400_000).toISOString(),
      user,
    }
  }
  try {
    return await api<Session>("/auth/verify", {
      method: "POST",
      body: { phone, code },
    })
  } catch (e) {
    const status = (e as { status?: number }).status
    throw new AuthError(
      status === 401 || status === 422 ? "wrong-code" : "network"
    )
  }
}
