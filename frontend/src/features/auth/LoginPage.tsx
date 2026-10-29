import { useTranslation } from "react-i18next"
import { useNavigate } from "react-router-dom"
import { homeFor, saveSession, type Role, type Session } from "@/auth/session"
import { Button } from "@/components/ui/button"

const DEMO_ROLES: Role[] = ["chv", "nurse", "doctor", "district_officer"]

function demoSession(role: Role, name: string): Session {
  return {
    token: "demo",
    expiresAt: new Date(Date.now() + 7 * 24 * 3600_000).toISOString(),
    user: { id: `demo-${role}`, name, role, district: "Shai-Osudoku" },
  }
}

/**
 * Demo sign-in: pick a role and get a local session, so the screens can be built and shown before
 * Bernard's auth endpoint exists. Replaced by the real sign-in (POST /api/auth/login) once it does.
 */
export function Component() {
  const { t } = useTranslation()
  const navigate = useNavigate()

  function signInAs(role: Role) {
    saveSession(demoSession(role, t(`roles.${role}`)))
    navigate(homeFor(role), { replace: true })
  }

  return (
    <main className="mx-auto flex min-h-svh max-w-md flex-col justify-center gap-6 p-6">
      <div className="space-y-2 text-center">
        <img src="/pwa-192x192.png" alt="" className="mx-auto size-16" />
        <h1 className="text-2xl font-semibold text-foreground">
          HealthConnect Ghana
        </h1>
        <p className="text-muted-foreground">{t("login.demoIntro")}</p>
      </div>
      <div className="space-y-3">
        {DEMO_ROLES.map((role) => (
          <Button
            key={role}
            size="xl"
            variant="secondary"
            className="w-full"
            onClick={() => signInAs(role)}
          >
            {t("login.continueAs", { role: t(`roles.${role}`) })}
          </Button>
        ))}
      </div>
    </main>
  )
}
