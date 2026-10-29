import { useTranslation } from "react-i18next"
import { useNavigate } from "react-router-dom"
import { homeFor, saveSession, type Role, type Session } from "@/auth/session"
import { Button } from "@/components/ui/button"

const DEMO_ROLES: Role[] = ["chv", "nurse", "doctor", "district_officer"]

/** Synthetic demo people, one per role (not real health workers). */
const DEMO_NAMES: Record<Role, string> = {
  chv: "Akosua Mensah",
  nurse: "Efua Asante",
  doctor: "Kwesi Eshun",
  district_officer: "Yaw Darko",
  admin: "Admin",
}

function demoSession(role: Role): Session {
  return {
    token: "demo",
    expiresAt: new Date(Date.now() + 7 * 24 * 3600_000).toISOString(),
    user: {
      id: `demo-${role}`,
      name: DEMO_NAMES[role],
      role,
      district: "Shai-Osudoku",
    },
  }
}

/**
 * Demo sign-in, laid out like the Figma "Login" screen: pick a role and get a local session, so
 * the screens can be shown before Bernard's auth endpoint exists. Replaced by the real sign-in
 * (POST /api/auth/login) once it does.
 */
export function Component() {
  const { t } = useTranslation()
  const navigate = useNavigate()

  function signInAs(role: Role) {
    saveSession(demoSession(role))
    navigate(homeFor(role), { replace: true })
  }

  return (
    <main className="mx-auto flex min-h-svh max-w-md flex-col justify-center gap-8 bg-background px-6 py-10">
      <div className="space-y-4 text-center">
        <p className="text-[2rem] leading-9 font-semibold text-primary">
          HealthConnect
        </p>
        <h1 className="text-[1.75rem] leading-9 font-bold text-heading">
          {t("login.welcome")}
        </h1>
        <p className="text-base text-primary">“{t("login.motto")}”</p>
      </div>
      <div className="space-y-4">
        <p className="text-sm text-foreground">{t("login.demoIntro")}</p>
        <div className="space-y-3">
          {DEMO_ROLES.map((role, i) => (
            <Button
              key={role}
              size="xl"
              variant={i === 0 ? "default" : "secondary"}
              className="w-full"
              onClick={() => signInAs(role)}
            >
              {t(`roles.${role}`)}
            </Button>
          ))}
        </div>
      </div>
    </main>
  )
}
