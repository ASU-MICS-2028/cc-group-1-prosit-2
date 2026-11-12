import { useEffect, useRef, useState } from "react"
import { useTranslation } from "react-i18next"
import { TriangleAlert } from "@/components/icons"
import { Card, PageHeader } from "@/components/PageHeader"
import { cn } from "@/lib/utils"
import { spokenText, TOPICS, type Topic } from "./topics"

/**
 * Health education for patients and families: a few topics in plain words, each with a play
 * button so a CHV can play it to someone who cannot read, or a patient can listen at home. A
 * topic with a recorded clip plays the clip; otherwise the phone reads the text aloud with its
 * own voice (Web Speech API, works offline on most Android phones).
 */
export function Component() {
  const { t } = useTranslation()
  const [playing, setPlaying] = useState<string | null>(null)
  const [open, setOpen] = useState<string | null>(TOPICS[0]!.id)
  const canSpeak = typeof window !== "undefined" && "speechSynthesis" in window

  // Stop reading when leaving the page.
  useEffect(() => () => void (canSpeak && speechSynthesis.cancel()), [canSpeak])

  function speak(topic: Topic) {
    if (!canSpeak) return
    speechSynthesis.cancel()
    if (playing === topic.id) return setPlaying(null)
    const u = new SpeechSynthesisUtterance(
      spokenText(topic, t("education.goNow"))
    )
    // The text is English for now (topics.ts); a Ghanaian English voice when the phone has one.
    const voices = speechSynthesis.getVoices()
    u.voice =
      voices.find((v) => v.lang === "en-GH") ??
      voices.find((v) => v.lang.startsWith("en")) ??
      null
    u.lang = u.voice?.lang ?? "en"
    u.rate = 0.9
    u.onend = () => setPlaying((p) => (p === topic.id ? null : p))
    u.onerror = u.onend
    setPlaying(topic.id)
    setOpen(topic.id)
    speechSynthesis.speak(u)
  }

  return (
    <>
      <PageHeader
        title={t("education.title")}
        subtitle={t("education.intro")}
      />
      <ul className="space-y-3">
        {TOPICS.map((topic) => (
          <li key={topic.id}>
            <TopicCard
              topic={topic}
              open={open === topic.id}
              onToggle={() => setOpen(open === topic.id ? null : topic.id)}
              playing={playing === topic.id}
              onPlay={
                canSpeak || topic.audio || topic.video
                  ? () => speak(topic)
                  : undefined
              }
            />
          </li>
        ))}
      </ul>
      {!canSpeak ? (
        <p className="mt-4 text-xs text-muted-foreground">
          {t("education.noVoice")}
        </p>
      ) : null}
      <p className="mt-4 text-xs text-muted-foreground">
        {t("education.englishOnly")}
      </p>
    </>
  )
}

function TopicCard({
  topic,
  open,
  onToggle,
  playing,
  onPlay,
}: {
  topic: Topic
  open: boolean
  onToggle: () => void
  playing: boolean
  onPlay?: () => void
}) {
  const { t } = useTranslation()
  const media = useRef<HTMLMediaElement>(null)
  const recorded = topic.video ?? topic.audio
  const [mediaPlaying, setMediaPlaying] = useState(false)

  function play() {
    const el = media.current
    if (!recorded || !el) return onPlay?.()
    if (el.paused) void el.play()
    else el.pause()
  }

  const isPlaying = recorded ? mediaPlaying : playing
  return (
    <Card className="p-4">
      <div className="flex items-start gap-3">
        <button
          type="button"
          onClick={onToggle}
          aria-expanded={open}
          className="min-w-0 flex-1 text-left"
        >
          <p className="font-semibold text-heading">{topic.title}</p>
          <p className="text-sm text-muted-foreground">{topic.summary}</p>
        </button>
        {recorded || onPlay ? (
          <button
            type="button"
            onClick={play}
            aria-label={
              isPlaying
                ? t("education.stop", { title: topic.title })
                : t("education.play", { title: topic.title })
            }
            className={cn(
              "grid size-12 shrink-0 place-items-center rounded-full",
              isPlaying
                ? "bg-destructive/10 text-destructive"
                : "bg-primary text-primary-foreground"
            )}
          >
            {isPlaying ? (
              <svg
                viewBox="0 0 24 24"
                className="size-6"
                fill="currentColor"
                aria-hidden
              >
                <rect x="6" y="6" width="12" height="12" rx="1.5" />
              </svg>
            ) : (
              <svg
                viewBox="0 0 24 24"
                className="size-6"
                fill="currentColor"
                aria-hidden
              >
                <path d="M8 5.14v13.72a1 1 0 0 0 1.5.86l11-6.86a1 1 0 0 0 0-1.72l-11-6.86A1 1 0 0 0 8 5.14z" />
              </svg>
            )}
          </button>
        ) : null}
      </div>

      {topic.video ? (
        <video
          ref={media as React.RefObject<HTMLVideoElement>}
          src={topic.video}
          controls
          preload="none"
          onPlay={() => setMediaPlaying(true)}
          onPause={() => setMediaPlaying(false)}
          className={cn("mt-3 w-full rounded-lg", !open && "hidden")}
        />
      ) : topic.audio ? (
        <audio
          ref={media as React.RefObject<HTMLAudioElement>}
          src={topic.audio}
          preload="none"
          onPlay={() => setMediaPlaying(true)}
          onPause={() => setMediaPlaying(false)}
          onEnded={() => setMediaPlaying(false)}
        />
      ) : null}

      {open ? (
        <div className="mt-3 space-y-3 text-sm">
          <ul className="list-disc space-y-1.5 pl-5 text-foreground">
            {topic.points.map((p) => (
              <li key={p}>{p}</li>
            ))}
          </ul>
          {topic.danger?.length ? (
            <div className="rounded-lg border border-destructive bg-destructive-soft p-3">
              <p className="flex items-center gap-1.5 font-semibold text-destructive">
                <TriangleAlert className="size-4" aria-hidden />
                {t("education.goNow")}
              </p>
              <ul className="mt-1 list-disc space-y-1 pl-5 text-foreground">
                {topic.danger.map((d) => (
                  <li key={d}>{d}</li>
                ))}
              </ul>
            </div>
          ) : null}
          <p className="text-xs text-muted-foreground">
            {t("education.source", { source: topic.source })}
          </p>
        </div>
      ) : null}
    </Card>
  )
}
