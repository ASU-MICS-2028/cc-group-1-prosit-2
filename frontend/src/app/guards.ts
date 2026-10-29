import { redirect } from "react-router-dom"
import { getSession, homeFor, type Role } from "@/auth/session"
import { hasChosenLanguage } from "@/i18n"

/** Where someone who is not signed in starts: the welcome screen the first time, else sign-in. */
const start = () => (hasChosenLanguage() ? "/login" : "/welcome")

/** Route loader for the signed-in parts: one of these roles, or back to sign-in / their own home. */
export function requireRole(...roles: Role[]) {
  return () => {
    const session = getSession()
    if (!session) throw redirect(start())
    if (!roles.includes(session.user.role))
      throw redirect(homeFor(session.user.role))
    return null
  }
}

/** Route loader for the sign-in screens: someone already signed in goes to their home. */
export function signedOutOnly() {
  const session = getSession()
  if (session) throw redirect(homeFor(session.user.role))
  return null
}

/** Sign-in needs a language first: a brand-new phone goes to the welcome screen. */
export function needsLanguage() {
  signedOutOnly()
  if (!hasChosenLanguage()) throw redirect("/welcome")
  return null
}
