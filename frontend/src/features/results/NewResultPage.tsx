import { useLiveQuery } from "dexie-react-hooks"
import { type ReactNode, useState } from "react"
import { useTranslation } from "react-i18next"
import { useNavigate, useParams } from "react-router-dom"
import { PhotoStrip } from "@/components/capture/PhotoStrip"
import { Field, Input, Textarea } from "@/components/form/Field"
import { PageHeader } from "@/components/PageHeader"
import { Button } from "@/components/ui/button"
import { db, newId, saveAndQueue } from "@/db/local"
import type { TestCategory, TestResult } from "@/db/types"
import { fullName, usePatient } from "@/features/patients/usePatients"
import { saved, saveFailed } from "@/lib/feedback"
import { saveMedia } from "@/lib/media"
import { cn } from "@/lib/utils"
import { COMMON_TESTS } from "./tests"

const CATEGORIES: TestCategory[] = ["rapid", "lab", "imaging"]

/**
 * Add a test, or the answer to one that was sent out.
 *
 * Most villages have no laboratory or X-ray machine. So a lab test or an X-ray usually starts as
 * "send for the test": it names the facility that can do it, and the patient goes there. When the
 * patient comes back with the paper slip or the film, the CHV opens the test again ("Results are
 * back"), types what it says and photographs it (an X-ray film held up against a bright window or
 * the sky). A doctor anywhere then sees the photo with the record. A quick test the CHV does on the
 * spot (malaria RDT, blood sugar) is saved with its answer straight away.
 *
 *   /patients/:id/results/new          a new test
 *   /patients/:id/results/:resultId    the answer to a test that was sent out
 */
export function Component() {
  const { resultId } = useParams()
  // Answering a test that was sent out: the form starts from what was asked for.
  const existing = useLiveQuery(
    async () => (resultId ? ((await db.results.get(resultId)) ?? null) : null),
    [resultId]
  )
  if (existing === undefined) return null // still reading this phone's database
  return (
    <ResultForm key={existing?.id ?? "new"} existing={existing ?? undefined} />
  )
}

function ResultForm({ existing }: { existing?: TestResult }) {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const { id = "" } = useParams()
  const patient = usePatient(id)

  const [category, setCategory] = useState<TestCategory>(
    existing?.category ?? "rapid"
  )
  const [sendOut, setSendOut] = useState(false)
  const [test, setTest] = useState(existing?.test ?? "")
  const [facility, setFacility] = useState(existing?.facility ?? "")
  const [result, setResult] = useState("")
  const [flag, setFlag] = useState<TestResult["flag"]>()
  const [notes, setNotes] = useState(existing?.notes ?? "")
  const [photos, setPhotos] = useState<Blob[]>([])
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string>()

  const answering = !!existing
  // A quick test is done here; anything else can be sent out first.
  const requesting = !answering && category !== "rapid" && sendOut

  async function save() {
    if (!test.trim()) return setError(t("errors.required"))
    if (requesting && !facility.trim())
      return setError(t("results.facilityNeeded"))
    setBusy(true)
    const now = new Date().toISOString()
    const record: TestResult = existing
      ? {
          ...existing,
          status: "resulted",
          facility: facility.trim() || undefined,
          result: result.trim() || undefined,
          flag,
          notes: notes.trim() || undefined,
          resultedAt: now,
          updatedAt: now,
        }
      : {
          id: newId(),
          patientId: id,
          category,
          test: test.trim(),
          status: requesting ? "requested" : "resulted",
          facility: facility.trim() || undefined,
          result: requesting ? undefined : result.trim() || undefined,
          flag: requesting ? undefined : flag,
          notes: notes.trim() || undefined,
          requestedAt: now,
          resultedAt: requesting ? undefined : now,
          createdAt: now,
          updatedAt: now,
          version: 0,
        }
    try {
      await saveAndQueue("result", record, existing ? "update" : "create")
      for (const blob of requesting ? [] : photos)
        await saveMedia({
          blob,
          kind: "result-photo",
          patientId: id,
          ownerEntity: "result",
          ownerId: record.id,
        })
    } catch (e) {
      setBusy(false)
      return saveFailed(e)
    }
    saved(requesting ? t("results.requestSaved") : t("results.resultSaved"))
    navigate(`/patients/${id}#results`, { replace: true })
  }

  return (
    <div className="mx-auto flex min-h-[calc(100svh-5rem)] max-w-lg flex-col">
      <PageHeader
        title={answering ? t("results.answerTitle") : t("results.newTitle")}
        back={`/patients/${id}`}
      />
      {patient ? (
        <p className="-mt-3 mb-5 text-sm text-muted-foreground">
          {fullName(patient)}
        </p>
      ) : null}

      <div className="flex flex-1 flex-col gap-5">
        {answering ? (
          <div className="rounded-xl bg-secondary p-4 text-sm text-secondary-foreground">
            <p className="font-semibold">{existing.test}</p>
            <p>
              {t(`results.category.${existing.category}`)}
              {existing.facility ? ` · ${existing.facility}` : ""}
            </p>
          </div>
        ) : (
          <>
            <Choice
              legend={t("results.whatKind")}
              options={CATEGORIES.map((c) => ({
                value: c,
                label: t(`results.category.${c}`),
                hint: t(`results.categoryHint.${c}`),
              }))}
              value={category}
              onChange={(c) => {
                setCategory(c)
                setSendOut(c !== "rapid")
                setTest("")
              }}
            />
            {category !== "rapid" ? (
              <Choice
                legend={t("results.whereNow")}
                options={[
                  {
                    value: "send",
                    label: t("results.sendOut"),
                    hint: t("results.sendOutHint"),
                  },
                  {
                    value: "back",
                    label: t("results.backNow"),
                    hint: t("results.backNowHint"),
                  },
                ]}
                value={sendOut ? "send" : "back"}
                onChange={(v) => setSendOut(v === "send")}
              />
            ) : null}
            <Field id="test" label={t("results.test")} error={error}>
              <Input
                id="test"
                list="common-tests"
                value={test}
                onChange={(e) => {
                  setTest(e.target.value)
                  setError(undefined)
                }}
                placeholder={COMMON_TESTS[category][0]}
                autoComplete="off"
              />
              <datalist id="common-tests">
                {COMMON_TESTS[category].map((name) => (
                  <option key={name} value={name} />
                ))}
              </datalist>
            </Field>
            <div className="-mt-3 flex flex-wrap gap-2">
              {COMMON_TESTS[category].slice(0, 4).map((name) => (
                <button
                  key={name}
                  type="button"
                  onClick={() => setTest(name)}
                  className={cn(
                    "min-h-9 rounded-full border px-3 text-xs font-semibold",
                    test === name
                      ? "border-primary bg-primary text-primary-foreground"
                      : "border-border bg-card text-foreground"
                  )}
                >
                  {name}
                </button>
              ))}
            </div>
          </>
        )}

        {requesting || (!answering && category !== "rapid") ? (
          <Field
            id="facility"
            label={t("results.facility")}
            hint={
              requesting
                ? t("results.facilityHintSend")
                : t("results.facilityHintDone")
            }
          >
            <Input
              id="facility"
              value={facility}
              onChange={(e) => {
                setFacility(e.target.value)
                setError(undefined)
              }}
            />
          </Field>
        ) : null}

        {requesting ? (
          <p className="rounded-xl border border-border bg-card p-4 text-sm text-muted-foreground">
            {t("results.sendOutNext")}
          </p>
        ) : (
          <>
            <Field
              id="result"
              label={t("results.result")}
              hint={t(`results.resultHint.${category}`)}
            >
              <Textarea
                id="result"
                value={result}
                onChange={(e) => setResult(e.target.value)}
              />
            </Field>
            <Choice
              legend={t("results.flagLegend")}
              options={[
                { value: "normal", label: t("results.flag.normal") },
                { value: "abnormal", label: t("results.flag.abnormal") },
              ]}
              value={flag ?? ""}
              onChange={(v) => setFlag(v as TestResult["flag"])}
            />
            <section className="space-y-2">
              <h2 className="text-sm font-medium tracking-[0.01em] text-foreground">
                {t("results.photos")}
              </h2>
              <p className="text-xs text-muted-foreground">
                {t(
                  category === "imaging"
                    ? "results.photosHintImaging"
                    : "results.photosHint"
                )}
              </p>
              {/* A slip's small print and a film's detail need more pixels than a wound photo. */}
              <PhotoStrip
                photos={photos}
                onChange={setPhotos}
                longestSide={2000}
                maxSizeMB={0.8}
              />
            </section>
          </>
        )}

        <Field id="notes" label={t("vitals.notes")} hint={t("common.optional")}>
          <Textarea
            id="notes"
            value={notes}
            onChange={(e) => setNotes(e.target.value)}
          />
        </Field>

        <Button
          type="button"
          size="xl"
          className="mt-auto w-full"
          disabled={busy}
          onClick={() => void save()}
        >
          {requesting ? t("results.saveRequest") : t("results.saveResult")}
        </Button>
      </div>
    </div>
  )
}

/** A short list of big choices, one of which is picked (radio buttons that look like cards). */
function Choice<T extends string>({
  legend,
  options,
  value,
  onChange,
}: {
  legend: string
  options: { value: T; label: string; hint?: ReactNode }[]
  value: T | ""
  onChange: (value: T) => void
}) {
  return (
    <fieldset className="space-y-2">
      <legend className="mb-1.5 text-sm font-medium tracking-[0.01em] text-foreground">
        {legend}
      </legend>
      <div
        className={cn(
          "grid gap-2",
          options.length === 2 ? "grid-cols-2" : "grid-cols-1"
        )}
      >
        {options.map((o) => (
          <label
            key={o.value}
            className={cn(
              "flex min-h-12 cursor-pointer flex-col justify-center rounded-xl border-2 bg-card px-4 py-2.5 text-sm",
              value === o.value
                ? "border-primary text-heading"
                : "border-transparent shadow-card"
            )}
          >
            <input
              type="radio"
              className="sr-only"
              checked={value === o.value}
              onChange={() => onChange(o.value)}
            />
            <span className="font-semibold">{o.label}</span>
            {o.hint ? (
              <span className="text-xs text-muted-foreground">{o.hint}</span>
            ) : null}
          </label>
        ))}
      </div>
    </fieldset>
  )
}
