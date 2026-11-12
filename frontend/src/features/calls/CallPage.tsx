import { useLiveQuery } from "dexie-react-hooks"
import { useEffect, useRef, useState } from "react"
import { useTranslation } from "react-i18next"
import { Link, useNavigate, useParams } from "react-router-dom"
import { isDemoSession } from "@/auth/session"
import { Mic, MicOff, PhoneOff, Video, VideoOff } from "@/components/icons"
import { PageHeader } from "@/components/PageHeader"
import { buttonVariants } from "@/components/ui/button"
import { db } from "@/db/local"
import { fullName } from "@/features/patients/usePatients"
import { formatDateTime } from "@/lib/dates"
import { cn } from "@/lib/utils"
import type { CallStats } from "./callStats"
import { MAX_VIDEO_BPS, useCall } from "./useCall"

/**
 * The teleconsult video call: the CHV with the patient on one side, the nurse or doctor on
 * the other. Small picture by default for 2G/3G, camera off for audio only, and the real
 * connection numbers one tap away (Lab 7 C.3).
 */
export function Component() {
  const { t } = useTranslation()
  const { appointmentId = "" } = useParams()
  const appointment = useLiveQuery(
    () => db.appointments.get(appointmentId),
    [appointmentId]
  )
  const patient = useLiveQuery(
    () => (appointment ? db.patients.get(appointment.patientId) : undefined),
    [appointment?.patientId]
  )
  const subtitle = appointment
    ? `${patient ? `${fullName(patient)} · ` : ""}${appointment.facility} · ${formatDateTime(appointment.scheduledAt)}`
    : undefined

  if (isDemoSession()) {
    return (
      <>
        <PageHeader
          title={t("call.title")}
          back="/appointments"
          subtitle={subtitle}
        />
        <p className="rounded-xl bg-card p-4 text-sm shadow-card">
          {t("call.demo")}
        </p>
      </>
    )
  }
  return <Call appointmentId={appointmentId} subtitle={subtitle} />
}

function Call({
  appointmentId,
  subtitle,
}: {
  appointmentId: string
  subtitle?: string
}) {
  const navigate = useNavigate()
  const [attempt, setAttempt] = useState(0)
  return (
    <CallSession
      key={attempt}
      appointmentId={appointmentId}
      subtitle={subtitle}
      onRejoin={() => setAttempt((n) => n + 1)}
      onLeave={() => navigate(-1)}
    />
  )
}

function VideoView({
  stream,
  muted,
  className,
  label,
}: {
  stream: MediaStream | null
  muted?: boolean
  className?: string
  label: string
}) {
  const ref = useRef<HTMLVideoElement>(null)
  useEffect(() => {
    if (ref.current) ref.current.srcObject = stream
  }, [stream])
  return (
    <video
      ref={ref}
      autoPlay
      playsInline
      muted={muted}
      aria-label={label}
      className={className}
    />
  )
}

/**
 * The other side's voice, in its own audio element. Phone browsers may refuse to start sound on
 * their own (autoplay rules) and do it silently: the picture plays and the voice doesn't. The
 * picture is muted so it always plays, and if the voice is refused the person gets a button.
 */
function RemoteSound({ stream }: { stream: MediaStream | null }) {
  const { t } = useTranslation()
  const ref = useRef<HTMLAudioElement>(null)
  const [blocked, setBlocked] = useState(false)
  const play = () =>
    ref.current
      ?.play()
      .then(() => setBlocked(false))
      .catch(() => setBlocked(true))
  useEffect(() => {
    const el = ref.current
    if (!el) return
    el.srcObject = stream
    if (!stream) return
    el.play()
      .then(() => setBlocked(false))
      .catch(() => setBlocked(true))
  }, [stream])
  return (
    <>
      <audio ref={ref} autoPlay data-testid="remote-sound" />
      {blocked ? (
        <button
          type="button"
          onClick={() => void play()}
          className="absolute inset-x-0 bottom-20 z-10 mx-auto w-fit rounded-full bg-white px-5 py-3 text-sm font-semibold text-neutral-900 shadow-lg"
        >
          {t("call.tapForSound")}
        </button>
      ) : null}
    </>
  )
}

function CallSession({
  appointmentId,
  subtitle,
  onRejoin,
  onLeave,
}: {
  appointmentId: string
  subtitle?: string
  onRejoin: () => void
  onLeave: () => void
}) {
  const { t } = useTranslation()
  const call = useCall(appointmentId)
  const name = call.peer?.name ?? ""
  const status: string =
    call.phase === "failed" && call.error
      ? t(`call.error_${call.error}` as const)
      : call.phase === "connecting"
        ? t("call.connecting", { name })
        : call.phase === "connected"
          ? t("call.connected", { name })
          : t(
              `call.${call.phase as "starting" | "waiting" | "reconnecting" | "ended" | "full"}` as const
            )
  const live =
    call.phase !== "ended" && call.phase !== "failed" && call.phase !== "full"

  return (
    <>
      <PageHeader title={t("call.title")} back={onLeave} subtitle={subtitle} />
      <div className="relative overflow-hidden rounded-2xl bg-neutral-900 text-white aspect-[3/4] sm:aspect-video">
        <VideoView
          stream={call.remote}
          muted
          label={t("call.title")}
          className={cn(
            "absolute inset-0 size-full object-cover",
            !call.remote && "hidden"
          )}
        />
        <RemoteSound stream={call.remote} />
        {!call.remote ? (
          <div className="absolute inset-0 grid place-items-center p-6 text-center">
            <div>
              <p
                className="text-base font-semibold"
                role="status"
                aria-live="polite"
              >
                {status}
              </p>
              {call.phase === "waiting" ? (
                <p className="mt-2 text-sm text-white/70">
                  {t("call.waitingHint")}
                </p>
              ) : null}
            </div>
          </div>
        ) : (
          <p
            className="absolute top-3 left-3 rounded-full bg-black/55 px-3 py-1 text-xs font-semibold"
            role="status"
            aria-live="polite"
          >
            {status}
          </p>
        )}
        {call.local && live ? (
          <VideoView
            stream={call.local}
            muted
            label={t("call.cameraOn")}
            className={cn(
              "absolute right-3 bottom-3 aspect-[3/4] w-24 rounded-lg border-2 border-white/80 bg-black object-cover shadow-lg [transform:scaleX(-1)] sm:w-32",
              !call.cameraOn && "opacity-30"
            )}
          />
        ) : null}
      </div>

      {live ? (
        <div className="mt-4 flex items-center justify-center gap-4">
          <RoundButton
            label={call.micOn ? t("call.mute") : t("call.unmute")}
            onClick={call.toggleMic}
            active={!call.micOn}
          >
            {call.micOn ? (
              <Mic className="size-6" />
            ) : (
              <MicOff className="size-6" />
            )}
          </RoundButton>
          <RoundButton
            label={call.cameraOn ? t("call.cameraOff") : t("call.cameraOn")}
            onClick={call.toggleCamera}
            active={!call.cameraOn}
          >
            {call.cameraOn ? (
              <Video className="size-6" />
            ) : (
              <VideoOff className="size-6" />
            )}
          </RoundButton>
          <RoundButton label={t("call.hangUp")} onClick={call.hangUp} danger>
            <PhoneOff className="size-7" />
          </RoundButton>
        </div>
      ) : (
        <div className="mt-4 grid grid-cols-2 gap-2">
          <button
            type="button"
            onClick={onRejoin}
            className={buttonVariants({ size: "xl" })}
          >
            {t("call.rejoin")}
          </button>
          <Link
            to="/appointments"
            className={buttonVariants({ size: "xl", variant: "secondary" })}
          >
            {t("common.back")}
          </Link>
        </div>
      )}

      <p className="mt-4 text-xs text-muted-foreground">
        {t("call.lowBandwidth", { kbps: MAX_VIDEO_BPS / 1000 })}
      </p>
      <Details stats={call.stats} />
    </>
  )
}

function RoundButton({
  label,
  onClick,
  active,
  danger,
  children,
}: {
  label: string
  onClick: () => void
  active?: boolean
  danger?: boolean
  children: React.ReactNode
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      aria-label={label}
      title={label}
      aria-pressed={danger ? undefined : active}
      className={cn(
        "grid size-14 place-items-center rounded-full shadow-card transition-colors",
        danger
          ? "bg-destructive text-white"
          : active
            ? "bg-heading text-background"
            : "bg-card text-heading ring-1 ring-border"
      )}
    >
      {children}
    </button>
  )
}

function Details({ stats }: { stats: CallStats | null }) {
  const { t } = useTranslation()
  const v = (n: number | undefined, unit: string) =>
    n === undefined ? "–" : `${n} ${unit}`
  const rows: [string, string][] = stats
    ? [
        [
          t("call.path"),
          `${t(`call.path_${stats.path}`)}${stats.localCandidate ? ` · ${stats.localCandidate}/${stats.remoteCandidate ?? "?"}${stats.protocol ? ` ${stats.protocol}` : ""}` : ""}`,
        ],
        [t("call.rtt"), v(stats.rttMs, "ms")],
        [
          t("call.loss"),
          stats.packetsLost === undefined
            ? "–"
            : `${stats.packetsLost} (${stats.packetLossPct ?? 0}%)`,
        ],
        [t("call.jitter"), v(stats.jitterMs, "ms")],
        [
          t("call.video"),
          `${v(stats.videoKbps, "kbps")}${stats.width ? ` · ${stats.width}×${stats.height} @ ${stats.framesPerSecond ?? "?"} fps` : ""}`,
        ],
        [t("call.audio"), v(stats.audioKbps, "kbps")],
        [
          t("call.sending"),
          `${v(stats.sendKbps, "kbps")}${stats.availableOutgoingKbps ? ` / ${stats.availableOutgoingKbps} kbps` : ""}`,
        ],
      ]
    : []
  return (
    <details className="mt-4 rounded-xl bg-card p-4 text-sm shadow-card" open>
      <summary className="cursor-pointer font-semibold text-heading">
        {t("call.details")}
      </summary>
      {rows.length ? (
        <dl className="mt-3 grid grid-cols-[auto_1fr] gap-x-4 gap-y-1.5 tabular-nums">
          {rows.map(([k, value]) => (
            <div key={k} className="contents">
              <dt className="text-muted-foreground">{k}</dt>
              <dd className="text-foreground">{value}</dd>
            </div>
          ))}
        </dl>
      ) : (
        <p className="mt-2 text-muted-foreground">{t("call.path_unknown")}</p>
      )}
    </details>
  )
}
