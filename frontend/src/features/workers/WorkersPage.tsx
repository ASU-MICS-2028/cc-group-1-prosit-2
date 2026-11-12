import { useEffect, useState, type FormEvent } from "react"
import { useTranslation } from "react-i18next"
import { addWorker, changeWorker, workers, type Worker } from "@/api/workers"
import { useSession } from "@/auth/session"
import { Card, EmptyState, PageHeader } from "@/components/PageHeader"
import { Button } from "@/components/ui/button"
import { cn } from "@/lib/utils"

type Problem = "number_in_use" | "number_is_a_patients" | "invalid" | "other"

/** The error code in an API refusal ({ "error": "..." }), if there is one. */
function codeOf(e: unknown): string | null {
  try {
    return (
      (JSON.parse((e as Error).message) as { error?: string }).error ?? null
    )
  } catch {
    return null
  }
}

/**
 * Who works in the district: the officer adds CHVs, nurses and doctors here (they then sign in
 * with a code sent to that phone) and switches off those who leave. The admin does the same for
 * every district and every role. Nobody changes their own account here.
 */
export function Component() {
  const { t } = useTranslation()
  const session = useSession()
  const isAdmin = session?.user.role === "admin"
  const roles: Worker["role"][] = isAdmin
    ? ["chv", "nurse", "doctor", "district_officer", "admin"]
    : ["chv", "nurse", "doctor"]

  const [list, setList] = useState<Worker[] | "error" | null>(null)
  const [name, setName] = useState("")
  const [phone, setPhone] = useState("")
  const [role, setRole] = useState<Worker["role"]>("chv")
  const [district, setDistrict] = useState("")
  const [busy, setBusy] = useState(false)
  const [problem, setProblem] = useState<Problem | null>(null)
  const [added, setAdded] = useState<string | null>(null)

  useEffect(() => {
    workers().then(setList, () => setList("error"))
  }, [])

  async function submit(e: FormEvent) {
    e.preventDefault()
    setBusy(true)
    setProblem(null)
    setAdded(null)
    try {
      const w = await addWorker({
        name,
        phone,
        role,
        ...(isAdmin ? { district } : {}),
      })
      setList((l) => (Array.isArray(l) ? [w, ...l] : [w]))
      setAdded(w.name)
      setName("")
      setPhone("")
    } catch (err) {
      const status = (err as { status?: number }).status
      const code = codeOf(err)
      setProblem(
        code === "number_in_use" || code === "number_is_a_patients"
          ? code
          : status === 422
            ? "invalid"
            : "other"
      )
    } finally {
      setBusy(false)
    }
  }

  async function toggle(w: Worker) {
    const updated = await changeWorker(w.id, { active: !w.active }).catch(
      () => null
    )
    if (updated)
      setList((l) =>
        Array.isArray(l) ? l.map((x) => (x.id === w.id ? updated : x)) : l
      )
  }

  const title = t("workers.title")
  return (
    <>
      <PageHeader
        title={title}
        subtitle={
          isAdmin
            ? t("workers.allDistricts")
            : t("workers.forDistrict", { district: session?.user.district })
        }
      />

      <Card className="mb-6 p-4">
        <form onSubmit={(e) => void submit(e)} className="space-y-3 text-sm">
          <h2 className="text-base font-semibold text-heading">
            {t("workers.add")}
          </h2>
          <label className="block">
            <span className="mb-1 block font-medium">{t("workers.name")}</span>
            <input
              required
              minLength={2}
              value={name}
              onChange={(e) => setName(e.target.value)}
              className="h-11 w-full rounded-lg border border-input bg-background px-3"
              autoComplete="off"
            />
          </label>
          <label className="block">
            <span className="mb-1 block font-medium">{t("workers.phone")}</span>
            <input
              required
              inputMode="tel"
              value={phone}
              onChange={(e) => setPhone(e.target.value)}
              placeholder="024 123 4567"
              className="h-11 w-full rounded-lg border border-input bg-background px-3"
              autoComplete="off"
            />
          </label>
          <label className="block">
            <span className="mb-1 block font-medium">{t("workers.role")}</span>
            <select
              value={role}
              onChange={(e) => setRole(e.target.value as Worker["role"])}
              className="h-11 w-full rounded-lg border border-input bg-background px-3"
            >
              {roles.map((r) => (
                <option key={r} value={r}>
                  {t(`workers.role_${r}` as const)}
                </option>
              ))}
            </select>
          </label>
          {isAdmin ? (
            <label className="block">
              <span className="mb-1 block font-medium">
                {t("workers.district")}
              </span>
              <input
                required
                value={district}
                onChange={(e) => setDistrict(e.target.value)}
                placeholder="Shai-Osudoku"
                className="h-11 w-full rounded-lg border border-input bg-background px-3"
              />
            </label>
          ) : null}
          <p className="text-xs text-muted-foreground">
            {t("workers.addHint")}
          </p>
          {problem ? (
            <p role="alert" className="text-destructive">
              {t(`workers.error_${problem}` as const)}
            </p>
          ) : null}
          {added ? (
            <p role="status" className="text-success">
              {t("workers.added", { name: added })}
            </p>
          ) : null}
          <Button type="submit" size="lg" className="w-full" disabled={busy}>
            {t("workers.addButton")}
          </Button>
        </form>
      </Card>

      {list === null ? null : list === "error" ? (
        <EmptyState illustration="offline">{t("workers.offline")}</EmptyState>
      ) : (
        <ul className="space-y-2">
          {list.map((w) => (
            <li key={w.id}>
              <Card
                className={cn(
                  "flex items-center justify-between gap-3 p-4 text-sm",
                  !w.active && "opacity-60"
                )}
              >
                <div className="min-w-0">
                  <p className="font-semibold text-heading">{w.name}</p>
                  <p className="text-muted-foreground">
                    {t(`workers.role_${w.role}` as const)} · {w.phone}
                    {isAdmin ? ` · ${w.district}` : ""}
                  </p>
                  {!w.active ? (
                    <p className="text-xs font-semibold text-destructive">
                      {t("workers.off")}
                    </p>
                  ) : null}
                </div>
                {w.id !== session?.user.id && roles.includes(w.role) ? (
                  <Button
                    size="lg"
                    variant="secondary"
                    onClick={() => void toggle(w)}
                  >
                    {w.active ? t("workers.switchOff") : t("workers.switchOn")}
                  </Button>
                ) : null}
              </Card>
            </li>
          ))}
        </ul>
      )}
    </>
  )
}
