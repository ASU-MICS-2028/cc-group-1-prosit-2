import { LogOut } from "@/components/icons"
import { useTranslation } from "react-i18next"
import { useNavigate } from "react-router-dom"
import { clearSession, useSession } from "@/auth/session"
import { Dropdown } from "@/components/form/Dropdown"
import { Field } from "@/components/form/Field"
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
        <div className="rounded-xl bg-card shadow-card p-4">
          <p className="font-medium text-foreground">{session?.user.name}</p>
          <p className="text-sm text-muted-foreground">
            {session ? t(`roles.${session.user.role}`) : null} ·{" "}
            {session?.user.district}
          </p>
        </div>
        <Field id="language" label={t("profile.language")}>
          <Dropdown
            id="language"
            value={i18n.language as LanguageCode}
            onChange={(code) => void setLanguage(code)}
            options={LANGUAGES.map((l) => ({
              value: l.code,
              label: l.label,
              hint: l.ready ? l.sub : t("profile.comingSoon"),
              disabled: !l.ready,
            }))}
          />
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
