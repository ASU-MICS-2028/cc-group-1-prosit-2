import { zodResolver } from "@hookform/resolvers/zod"
import {
  Fingerprint,
  MessageCircle,
  PenLine,
  ShieldCheck,
} from "@/components/icons"
import { useState } from "react"
import { Controller, FormProvider, useForm, useWatch } from "react-hook-form"
import { useTranslation } from "react-i18next"
import { useNavigate } from "react-router-dom"
import { z } from "zod"
import { ChoiceTiles } from "@/components/form/ChoiceTiles"
import { AudioRecorder } from "@/components/capture/AudioRecorder"
import { canRecordAudio } from "@/lib/audio"
import { PhotoCapture } from "@/components/capture/PhotoCapture"
import { SignaturePad } from "@/components/capture/SignaturePad"
import { useSession } from "@/auth/session"
import { saveMedia } from "@/lib/media"
import { Field, Input } from "@/components/form/Field"
import { describe } from "@/components/form/describe"
import { Stepper } from "@/components/form/Stepper"
import { Card, PageHeader } from "@/components/PageHeader"
import { Button } from "@/components/ui/button"
import { newId, saveAndQueue } from "@/db/local"
import {
  CONSENT_STATEMENT_VERSION,
  type ConsentMethod,
  type MediaKind,
  type Patient,
} from "@/db/types"
import { estimatedBirthDate } from "@/lib/vitals"
import { birthIssue, personStep, placeStep } from "./patientForm"
import { PersonFields, PlaceFields } from "./PatientFields"

// Three steps, as in the Figma "Añadir Contacto" forms. Messages are translation keys, so errors
// show in the app's language. Each step is checked on its own before Next.
// Consent needs proof: a voice clip, a signature or a photo of the inked thumbprint. A phone
// that can't record sound falls back to a named witness for spoken consent.
const consentStep = z
  .object({
    consentMethod: z.enum(["spoken", "thumbprint", "signature"]),
    consentEvidence: z.custom<Blob>((v) => v instanceof Blob).optional(),
    noRecorder: z.boolean(),
    witness: z.string().trim(),
    consent: z.literal(true, { error: "errors.consent" }),
  })
  .superRefine((v, ctx) => {
    const fallback = v.consentMethod === "spoken" && v.noRecorder
    if (fallback && !v.witness)
      ctx.addIssue({
        code: "custom",
        path: ["witness"],
        message: "capture.witnessNeeded",
      })
    if (!fallback && !v.consentEvidence)
      ctx.addIssue({
        code: "custom",
        path: ["consentEvidence"],
        message: "errors.evidence",
      })
  })

const STEPS = [consentStep, personStep, placeStep] as const
const schema = consentStep
  .and(personStep)
  .and(placeStep)
  .refine((v) => birthIssue(v) === null, {
    path: ["dateOfBirth"],
    message: "errors.dateOfBirth",
  })

type Form = z.input<typeof schema>

const EVIDENCE_KIND: Record<ConsentMethod, MediaKind> = {
  spoken: "consent-audio",
  signature: "consent-signature",
  thumbprint: "consent-photo",
}

export function Component() {
  const { t, i18n } = useTranslation()
  const navigate = useNavigate()
  const session = useSession()
  const [step, setStep] = useState(0)
  const form = useForm<Form>({
    resolver: zodResolver(schema),
    shouldUnregister: false,
    defaultValues: {
      knowsBirthDate: "yes",
      phone: "",
      nationalId: "",
      preferredLanguage: i18n.language,
      consentMethod: "spoken",
      noRecorder: !canRecordAudio(),
      witness: "",
    },
  })
  const {
    register,
    handleSubmit,
    control,
    getValues,
    setValue,
    setError,
    clearErrors,
    setFocus,
    formState: { errors, isSubmitting },
  } = form
  const knowsBirthDate = useWatch({ control, name: "knowsBirthDate" }) === "yes"
  const consentMethod = useWatch({ control, name: "consentMethod" })
  const noRecorder = useWatch({ control, name: "noRecorder" })
  const err = (key: keyof Form) => {
    const message = errors[key]?.message
    return message ? t(message as "errors.required") : undefined
  }

  function next() {
    clearErrors()
    const values = getValues()
    const result = STEPS[step].safeParse(values)
    const issues = (result.success ? [] : result.error.issues).map((i) => ({
      path: String(i.path[0]),
      message: i.message,
    }))
    const birth = step === 1 ? birthIssue(values) : null
    if (birth) issues.push(birth)
    if (issues.length === 0) {
      setStep(step + 1)
      window.scrollTo(0, 0)
      return
    }
    for (const issue of issues) {
      setError(issue.path as keyof Form, { message: issue.message })
    }
    setFocus(issues[0].path as keyof Form)
  }

  async function onSubmit(v: Form) {
    const now = new Date().toISOString()
    const id = newId()
    const evidence = v.consentEvidence
      ? await saveMedia({
          blob: v.consentEvidence,
          kind: EVIDENCE_KIND[v.consentMethod],
          patientId: id,
          ownerEntity: "patient",
          ownerId: id,
        })
      : null
    const patient: Patient = {
      id,
      givenName: v.givenName.trim(),
      familyName: v.familyName.trim(),
      sex: v.sex,
      dateOfBirth: knowsBirthDate
        ? v.dateOfBirth!
        : estimatedBirthDate(Number(v.ageYears)),
      birthDateEstimated: !knowsBirthDate,
      phone: v.phone || undefined,
      nationalId: v.nationalId || undefined,
      community: v.community.trim(),
      district: v.district.trim(),
      preferredLanguage: v.preferredLanguage,
      consent: {
        given: true,
        method: v.consentMethod,
        at: now,
        statementVersion: CONSENT_STATEMENT_VERSION,
        language: i18n.language,
        obtainedBy: session?.user.id,
        witness: v.witness.trim() || undefined,
        evidence: evidence
          ? {
              mediaId: evidence.id,
              sha256: evidence.sha256,
              mime: evidence.mime,
            }
          : undefined,
      },
      createdAt: now,
      updatedAt: now,
      version: 0,
    }
    await saveAndQueue("patient", patient)
    navigate(`/patients/${patient.id}/registered`, { replace: true })
  }

  const titles = [
    t("newPatient.consentTitle"),
    t("newPatient.personTitle"),
    t("newPatient.placeTitle"),
  ]
  const subtitles = [
    t("newPatient.consentText"),
    t("newPatient.personText"),
    t("newPatient.placeText"),
  ]
  const last = step === STEPS.length - 1

  return (
    <div className="mx-auto flex min-h-[calc(100svh-5rem)] flex-col max-w-lg">
      <PageHeader
        title={titles[step]}
        subtitle={subtitles[step]}
        back={step === 0 ? "/patients" : () => setStep(step - 1)}
      >
        <Stepper step={step} total={STEPS.length} />
      </PageHeader>

      <FormProvider {...form}>
        <form
          noValidate
          onSubmit={
            last
              ? handleSubmit(onSubmit)
              : (e) => {
                  e.preventDefault()
                  next()
                }
          }
          className="flex flex-1 flex-col gap-5"
        >
          {step === 0 ? (
            <>
              <img
                src="/illustrations/consent.svg"
                alt=""
                className="mx-auto h-28 w-auto"
              />
              {/* the exact words the evidence proves the patient agreed to */}
              <Card className="space-y-2 p-4">
                <p className="flex items-center gap-2 text-sm font-semibold text-heading">
                  <ShieldCheck
                    className="size-5 shrink-0 text-primary"
                    aria-hidden
                  />
                  {t("newPatient.statementTitle")}
                </p>
                <p className="text-base leading-relaxed text-foreground">
                  {t("newPatient.statement")}
                </p>
                <p className="text-xs text-muted-foreground">
                  {t("newPatient.statementVersion", {
                    version: CONSENT_STATEMENT_VERSION,
                  })}
                </p>
              </Card>
              <Controller
                control={control}
                name="consentMethod"
                render={({ field }) => (
                  <ChoiceTiles
                    name="consentMethod"
                    legend={t("newPatient.consentMethod")}
                    value={field.value}
                    onChange={(method) => {
                      field.onChange(method)
                      setValue("consentEvidence", undefined)
                      clearErrors(["consentEvidence", "witness"])
                    }}
                    choices={[
                      {
                        value: "spoken",
                        label: t("consent.spoken"),
                        icon: MessageCircle,
                      },
                      {
                        value: "thumbprint",
                        label: t("consent.thumbprint"),
                        icon: Fingerprint,
                      },
                      {
                        value: "signature",
                        label: t("consent.signature"),
                        icon: PenLine,
                      },
                    ]}
                  />
                )}
              />
              <Controller
                control={control}
                name="consentEvidence"
                render={({ field }) => (
                  <div className="space-y-1.5">
                    {consentMethod === "spoken" && !noRecorder ? (
                      <AudioRecorder
                        value={field.value}
                        onChange={field.onChange}
                        onUnavailable={() => setValue("noRecorder", true)}
                      />
                    ) : consentMethod === "spoken" ? (
                      <p className="rounded-xl bg-card p-4 text-sm text-muted-foreground shadow-card">
                        {t("capture.noMic")}
                      </p>
                    ) : consentMethod === "signature" ? (
                      <SignaturePad
                        label={t("capture.signatureLabel")}
                        onChange={field.onChange}
                      />
                    ) : (
                      <PhotoCapture
                        value={field.value}
                        onChange={field.onChange}
                        label={t("capture.takePhoto")}
                        hint={t("capture.thumbHint")}
                        alt={t("capture.photoAlt")}
                      />
                    )}
                    {errors.consentEvidence ? (
                      <p
                        role="alert"
                        className="text-xs font-medium text-destructive"
                      >
                        {err("consentEvidence")}
                      </p>
                    ) : null}
                  </div>
                )}
              />
              <Field
                id="witness"
                label={t("capture.witness")}
                hint={
                  consentMethod === "spoken" && noRecorder
                    ? t("capture.witnessNeeded")
                    : t("capture.witnessHint")
                }
                error={err("witness")}
              >
                <Input
                  autoComplete="off"
                  {...register("witness")}
                  {...describe("witness", err("witness"))}
                />
              </Field>
              <label className="flex min-h-11 items-start gap-3 text-sm text-foreground">
                <input
                  type="checkbox"
                  className="mt-0.5 size-5 shrink-0 accent-primary"
                  {...register("consent")}
                  aria-invalid={errors.consent ? true : undefined}
                />
                {t("newPatient.consentGiven")}
              </label>
              {errors.consent ? (
                <p
                  role="alert"
                  className="text-xs font-medium text-destructive"
                >
                  {err("consent")}
                </p>
              ) : null}
            </>
          ) : null}

          {step === 1 ? <PersonFields /> : null}

          {step === 2 ? <PlaceFields /> : null}

          <Button
            type="submit"
            size="xl"
            className="mt-auto w-full"
            disabled={isSubmitting}
          >
            {last ? t("newPatient.save") : t("common.next")}
          </Button>
        </form>
      </FormProvider>
    </div>
  )
}
