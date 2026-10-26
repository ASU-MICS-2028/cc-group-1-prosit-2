import { Suspense, useEffect } from "react"
import { useTranslation } from "react-i18next"
import { NavLink, Outlet } from "react-router-dom"
import { useSession } from "@/auth/session"
import { OfflineBanner } from "@/components/OfflineBanner"
import { cn } from "@/lib/utils"
import { syncNow } from "@/sync/sync"
import { Loading } from "./Loading"
import { navFor } from "./navItems"
import { useHideBottomNav } from "./useHideBottomNav"

/** Sync on our own: when the app opens and whenever the network comes back. */
function useAutoSync(enabled: boolean) {
  useEffect(() => {
    if (!enabled) return
    // A failure is fine here: the queue stays and the Sync page shows what is waiting.
    const quietly = () => void syncNow().catch(() => {})
    quietly()
    window.addEventListener("online", quietly)
    return () => window.removeEventListener("online", quietly)
  }, [enabled])
}

/**
 * The signed-in frame, switched by width: under 768 px a bottom bar; from 768 px the same places
 * as a sidebar. The layout is a placeholder until the Figma design lands.
 */
export function AppLayout() {
  const { t } = useTranslation()
  const session = useSession()
  const hideBottomNav = useHideBottomNav()
  useAutoSync(session !== null)
  const items = session ? navFor(session.user.role) : []

  const link = ({ isActive }: { isActive: boolean }) =>
    cn(
      "flex items-center gap-3 rounded-xl px-3 py-2.5 text-sm font-medium",
      isActive
        ? "bg-secondary text-secondary-foreground"
        : "text-muted-foreground hover:bg-muted"
    )

  return (
    <div className="min-h-svh bg-background md:flex">
      {items.length > 0 ? (
        <nav
          aria-label={t("nav.label")}
          className="sticky top-0 hidden h-svh w-60 shrink-0 flex-col gap-1 border-r p-4 md:flex"
        >
          <p className="mb-4 px-3 text-lg font-semibold text-primary">
            HealthConnect
          </p>
          {items.map(({ to, key, icon: Icon, end }) => (
            <NavLink key={to} to={to} end={end} className={link}>
              <Icon className="size-5" aria-hidden />
              {t(key)}
            </NavLink>
          ))}
        </nav>
      ) : null}
      <div className="min-w-0 flex-1">
        <OfflineBanner />
        <main
          className={cn(
            "mx-auto w-full max-w-3xl px-4 pt-4 md:px-8 md:pt-7 md:pb-10",
            hideBottomNav ? "pb-8" : "pb-28"
          )}
        >
          <Suspense fallback={<Loading />}>
            <Outlet />
          </Suspense>
        </main>
      </div>
      {items.length > 0 && !hideBottomNav ? (
        <nav
          aria-label={t("nav.label")}
          className="fixed inset-x-3 bottom-3 z-20 flex justify-around rounded-3xl bg-card p-1.5 shadow-floating ring-1 ring-border md:hidden"
        >
          {items.map(({ to, key, icon: Icon, end }) => (
            <NavLink
              key={to}
              to={to}
              end={end}
              className={({ isActive }) =>
                cn(
                  "flex min-w-14 flex-col items-center gap-0.5 rounded-2xl px-2 py-1.5 text-[0.7rem] font-medium",
                  isActive
                    ? "bg-secondary text-secondary-foreground"
                    : "text-muted-foreground"
                )
              }
            >
              <Icon className="size-5" aria-hidden />
              {t(key)}
            </NavLink>
          ))}
        </nav>
      ) : null}
    </div>
  )
}
