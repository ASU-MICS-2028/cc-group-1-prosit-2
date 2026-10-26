import { redirect } from "react-router-dom"
import { getSession, homeFor, type Role } from "@/auth/session"

/** Route loader for the signed-in parts: one of these roles, or back to sign-in / their own home. */
export function requireRole(...roles: Role[]) {
  return () => {
    const session = getSession()
    if (!session) throw redirect("/login")
    if (!roles.includes(session.user.role))
      throw redirect(homeFor(session.user.role))
    return null
  }
}

/** Route loader for the sign-in screen: someone already signed in goes to their home. */
export function signedOutOnly() {
  const session = getSession()
  if (session) throw redirect(homeFor(session.user.role))
  return null
}
