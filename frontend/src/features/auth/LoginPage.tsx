import { useState } from "react"
import { useTranslation } from "react-i18next"
import { Link, useNavigate } from "react-router-dom"
import {
  AuthError,
  DEMO_CODE,
  DEMO_MODE,
  DEMO_USERS,
  normalisePhone,
  requestCode,
} from "@/api/auth"
import { Field, Input } from "@/components/form/Field"
import { describe } from "@/components/form/describe"
import { Button } from "@/components/ui/button"

/**
 * Sign-in step 1 (Figma "Completa tu Registro"): the health worker's phone number. An SMS code
 * follows (step 2). No password to forget, and it works on any phone with SMS.
 */
export function Component() {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const [phone, setPhone] = useState("")
  const [error, setError] = useState<string>()
  const [busy, setBusy] = useState(false)

  async function submit(e: React.FormEvent) {
    e.preventDefault()
    const normalised = normalisePhone(phone)
    if (!normalised) return setError(t("errors.phone"))
    setBusy(true)
    setError(undefined)
    try {
      await requestCode(normalised)
      navigate("/login/code", { state: { phone: normalised } })
    } catch (err) {
      setError(
        err instanceof AuthError && err.reason === "unknown-number"
          ? t("login.unknownNumber")
          : t("login.network")
      )
    } finally {
      setBusy(false)
    }
  }

  return (
    <main className="mx-auto flex min-h-svh max-w-md flex-col gap-6 bg-background px-6 py-10">
      <p className="text-[1.75rem] leading-9 font-semibold text-primary">
        HealthConnect
      </p>
      <div className="space-y-3">
        <h1 className="text-[1.75rem] leading-9 font-bold text-heading">
          {t("login.title")}
        </h1>
        <p className="text-base text-foreground">{t("login.text")}</p>
      </div>
      <form noValidate onSubmit={submit} className="space-y-5">
        <Field
          id="phone"
          label={t("login.phone")}
          hint={t("login.phoneHint")}
          error={error}
        >
          <div className="flex gap-2">
            <span className="flex h-14 items-center gap-1.5 rounded-sm border border-input bg-card px-3 text-base text-foreground">
              <span aria-hidden>🇬🇭</span> +233
            </span>
            <Input
              type="tel"
              inputMode="tel"
              autoComplete="tel-national"
              placeholder="024 000 0000"
              value={phone}
              onChange={(e) => setPhone(e.target.value)}
              {...describe("phone", error)}
            />
          </div>
        </Field>
        <Button type="submit" size="xl" className="w-full" disabled={busy}>
          {t("login.sendCode")}
        </Button>
      </form>

      {DEMO_MODE ? (
        <section className="space-y-2 rounded-xl bg-card/70 p-4 text-sm">
          <p className="font-semibold text-foreground">
            {t("login.demoTitle")}
          </p>
          <p className="text-muted-foreground">
            {t("login.demoText", { code: DEMO_CODE })}
          </p>
          <ul className="space-y-1">
            {Object.entries(DEMO_USERS).map(([number, user]) => (
              <li key={number}>
                <button
                  type="button"
                  onClick={() => setPhone(number.replace("+233", "0"))}
                  className="flex min-h-11 w-full items-center justify-between gap-2 rounded-md px-2 text-left hover:bg-muted"
                >
                  <span className="text-foreground">
                    {t(`roles.${user.role}`)}
                  </span>
                  <span className="font-medium text-primary tabular-nums">
                    {number.replace("+233", "0")}
                  </span>
                </button>
              </li>
            ))}
          </ul>
        </section>
      ) : null}

      <Link
        to="/welcome"
        className="mt-auto text-center text-sm font-semibold text-primary"
      >
        {t("login.changeLanguage")}
      </Link>
    </main>
  )
}
