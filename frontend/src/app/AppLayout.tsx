import { Suspense, useEffect } from "react"
import { useTranslation } from "react-i18next"
import { NavLink, Outlet } from "react-router-dom"
import { useSession } from "@/auth/session"
import { OfflineBanner } from "@/components/OfflineBanner"
import { cn } from "@/lib/utils"
import { syncNow } from "@/sync/sync"
import { Loading } from "./Loading"
import { navFor, type NavItem } from "./navItems"
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

/** The pill behind a tab's icon (Figma "Bottom Navigation"): blue when active, orange for Add. */
function pill(item: NavItem, isActive: boolean) {
  return cn(
    "grid h-8 w-16 place-items-center rounded-full transition-colors",
    isActive
      ? item.add
        ? "bg-accent-orange text-white"
        : "bg-primary text-primary-foreground"
      : "text-foreground"
  )
}

/**
 * The signed-in frame, switched by width: under 768 px the Figma bottom bar; from 768 px the same
 * places as a sidebar, since the design has no desktop frames.
 */
export function AppLayout() {
  const { t } = useTranslation()
  const session = useSession()
  const hideBottomNav = useHideBottomNav()
  useAutoSync(session !== null)
  const items = session ? navFor(session.user.role) : []

  return (
    <div className="min-h-svh bg-background md:flex">
      {items.length > 0 ? (
        <nav
          aria-label={t("nav.label")}
          className="sticky top-0 hidden h-svh w-64 shrink-0 flex-col gap-1 bg-nav p-4 md:flex"
        >
          <p className="mb-6 px-3 pt-2 text-2xl font-bold text-primary">
            HealthConnect
          </p>
          {items.map((item) => (
            <NavLink
              key={item.to}
              to={item.to}
              end={item.end}
              className="flex items-center gap-3 rounded-full py-1 pr-3 text-sm font-medium text-foreground"
            >
              {({ isActive }) => (
                <>
                  <span className={pill(item, isActive)}>
                    <item.icon className="size-6" filled={isActive} />
                  </span>
                  <span className={isActive ? "font-semibold" : undefined}>
                    {t(item.key)}
                  </span>
                </>
              )}
            </NavLink>
          ))}
        </nav>
      ) : null}
      <div className="min-w-0 flex-1">
        <OfflineBanner />
        <main
          className={cn(
            "mx-auto w-full max-w-3xl px-5 pt-[max(2.5rem,env(safe-area-inset-top))] md:px-8 md:pt-12 md:pb-10",
            hideBottomNav ? "pb-10" : "pb-28"
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
          className="fixed inset-x-0 bottom-0 z-20 flex justify-around bg-nav px-1 pt-3 pb-[max(0.75rem,env(safe-area-inset-bottom))] md:hidden"
        >
          {items.map((item) => (
            <NavLink
              key={item.to}
              to={item.to}
              end={item.end}
              className="flex min-w-16 flex-col items-center gap-1 text-xs font-medium tracking-[0.03em] text-muted-foreground aria-[current=page]:font-semibold aria-[current=page]:text-foreground"
            >
              {({ isActive }) => (
                <>
                  <span className={pill(item, isActive)}>
                    <item.icon className="size-6" filled={isActive} />
                  </span>
                  {t(item.key)}
                </>
              )}
            </NavLink>
          ))}
        </nav>
      ) : null}
    </div>
  )
}
