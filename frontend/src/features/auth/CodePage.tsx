import { ArrowLeft } from "@/components/icons"
import { useEffect, useState } from "react"
import { useTranslation } from "react-i18next"
import { Link, Navigate, useLocation, useNavigate } from "react-router-dom"
import { AuthError, requestCode, verifyCode } from "@/api/auth"
import { homeFor, saveSession } from "@/auth/session"
import { CodeInput } from "@/components/form/CodeInput"
import { Button } from "@/components/ui/button"

const RESEND_AFTER_S = 60

/**
 * Sign-in step 2 (Figma "Validar Registro"): the 6-digit code from the SMS. It submits by itself
 * once six digits are in; "Send again" unlocks after a minute.
 */
export function Component() {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const phone = (useLocation().state as { phone?: string } | null)?.phone
  const [code, setCode] = useState("")
  const [error, setError] = useState<string>()
  const [busy, setBusy] = useState(false)
  const [wait, setWait] = useState(RESEND_AFTER_S)

  useEffect(() => {
    if (wait <= 0) return
    const timer = window.setTimeout(() => setWait(wait - 1), 1000)
    return () => window.clearTimeout(timer)
  }, [wait])

  if (!phone) return <Navigate to="/login" replace />

  async function verify(value: string) {
    setBusy(true)
    setError(undefined)
    try {
      const session = await verifyCode(phone!, value)
      saveSession(session)
      navigate(homeFor(session.user.role), { replace: true })
    } catch (err) {
      setError(
        err instanceof AuthError && err.reason === "wrong-code"
          ? t("code.wrong")
          : t("login.network")
      )
      setCode("")
    } finally {
      setBusy(false)
    }
  }

  function change(value: string) {
    setCode(value)
    if (value.length === 6 && !busy) void verify(value)
  }

  async function resend() {
    setWait(RESEND_AFTER_S)
    setError(undefined)
    await requestCode(phone!).catch(() => setError(t("login.network")))
  }

  return (
    <main className="mx-auto flex min-h-svh max-w-md flex-col gap-6 bg-background px-6 py-8">
      <Link
        to="/login"
        className="-ml-2 inline-flex h-11 w-fit items-center gap-3 rounded-md px-2 text-sm font-semibold text-heading"
      >
        <ArrowLeft className="size-6" aria-hidden />
        {t("common.back")}
      </Link>
      <div className="space-y-3">
        <h1 className="text-[1.75rem] leading-9 font-bold text-heading">
          {t("code.title")}
        </h1>
        <p className="text-base text-foreground">
          {t("code.text", { phone: phone.replace("+233", "0") })}
        </p>
      </div>
      <form
        noValidate
        onSubmit={(e) => {
          e.preventDefault()
          if (code.length === 6) void verify(code)
        }}
        className="flex flex-1 flex-col gap-5"
      >
        <CodeInput
          value={code}
          onChange={change}
          label={t("code.label")}
          invalid={!!error}
        />
        {error ? (
          <p
            role="alert"
            className="text-center text-sm font-medium text-destructive"
          >
            {error}
          </p>
        ) : null}
        <Button
          type="submit"
          size="xl"
          className="mt-auto w-full"
          disabled={busy || code.length < 6}
        >
          {t("code.verify")}
        </Button>
      </form>
      <p className="text-center text-sm text-foreground">
        {t("code.noCode")}{" "}
        {wait > 0 ? (
          <span className="text-muted-foreground">
            {t("code.resendIn", { count: wait })}
          </span>
        ) : (
          <button
            type="button"
            onClick={() => void resend()}
            className="min-h-11 font-semibold text-primary"
          >
            {t("code.resend")}
          </button>
        )}
      </p>
    </main>
  )
}
