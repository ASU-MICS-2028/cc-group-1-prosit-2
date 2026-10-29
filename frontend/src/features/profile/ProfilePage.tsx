import { LogOut } from "lucide-react"
import { useTranslation } from "react-i18next"
import { useNavigate } from "react-router-dom"
import { clearSession, useSession } from "@/auth/session"
import { Field, Select } from "@/components/form/Field"
import { PageHeader } from "@/components/PageHeader"
import { Button } from "@/components/ui/button"
import { LANGUAGES, setLanguage, type LanguageCode } from "@/i18n"

export function Component() {
  const { t, i18n } = useTranslation()
  const session = useSession()
  const navigate = useNavigate()

  function signOut() {
    clearSession()
    navigate("/login", { replace: true })
  }

  return (
    <>
      <PageHeader title={t("profile.title")} />
      <div className="space-y-5">
        <div className="rounded-2xl bg-card p-4 ring-1 ring-border">
          <p className="font-medium text-foreground">{session?.user.name}</p>
          <p className="text-sm text-muted-foreground">
            {session ? t(`roles.${session.user.role}`) : null} ·{" "}
            {session?.user.district}
          </p>
        </div>
        <Field id="language" label={t("profile.language")}>
          <Select
            id="language"
            value={i18n.language}
            onChange={(e) => void setLanguage(e.target.value as LanguageCode)}
          >
            {LANGUAGES.map((l) => (
              <option key={l.code} value={l.code} disabled={!l.ready}>
                {l.label}
                {l.ready ? "" : ` (${t("profile.comingSoon")})`}
              </option>
            ))}
          </Select>
        </Field>
        <Button
          size="xl"
          variant="destructive"
          className="w-full"
          onClick={signOut}
        >
          <LogOut aria-hidden />
          {t("profile.signOut")}
        </Button>
      </div>
    </>
  )
}
