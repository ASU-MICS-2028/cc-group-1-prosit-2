import { render } from "@testing-library/react"
import { RouterProvider, createMemoryRouter } from "react-router-dom"
import { routes } from "@/app/router"
import { saveSession, type Role } from "@/auth/session"
import "@/i18n"

/** Puts a signed-in session on the "phone" (localStorage), valid for a week. */
export function signIn(role: Role = "chv") {
  saveSession({
    token: "test-token",
    expiresAt: new Date(Date.now() + 7 * 86_400_000).toISOString(),
    user: {
      id: `test-${role}`,
      name: "Akosua Mensah",
      role,
      district: "Shai-Osudoku",
    },
  })
}

/**
 * Renders the app at a path; a CHV is signed in unless `as: null`. The language is chosen unless
 * `firstRun: true` (a brand-new phone).
 */
export function renderRoute(
  path: string,
  { as = "chv" as Role | null, firstRun = false } = {}
) {
  if (!firstRun) localStorage.setItem("healthconnect.lang", "en")
  if (as) signIn(as)
  const router = createMemoryRouter(routes, { initialEntries: [path] })
  return { router, ...render(<RouterProvider router={router} />) }
}
