// The Web Speech API, on-device only. Chrome 139+ can recognise speech on the phone
// (processLocally); the older cloud mode sends the audio to Google, which we don't allow for
// health notes (Act 843 s.28: no third party without an agreement). No on-device model, no button.

interface Recognition extends EventTarget {
  lang: string
  interimResults: boolean
  continuous: boolean
  processLocally?: boolean
  start(): void
  stop(): void
  onresult:
    | ((e: {
        results: ArrayLike<
          ArrayLike<{ transcript: string }> & { isFinal: boolean }
        >
      }) => void)
    | null
  onend: (() => void) | null
  onerror: ((e: { error: string }) => void) | null
}
interface RecognitionClass {
  new (): Recognition
  available?: (o: {
    langs: string[]
    processLocally: boolean
  }) => Promise<string>
  install?: (o: {
    langs: string[]
    processLocally: boolean
  }) => Promise<boolean>
}

const Speech =
  (
    globalThis as unknown as {
      SpeechRecognition?: RecognitionClass
      webkitSpeechRecognition?: RecognitionClass
    }
  ).SpeechRecognition ??
  (globalThis as unknown as { webkitSpeechRecognition?: RecognitionClass })
    .webkitSpeechRecognition

/** Speech locales for the app's languages; Twi, Ewe, Dagbani and Hausa have no model yet. */
const LOCALE: Record<string, string> = { en: "en-GB" }

export type DictationState = "unavailable" | "downloadable" | "available"

/** Whether this phone can turn speech in this language into text without the internet. */
export async function dictationState(lang: string): Promise<DictationState> {
  const locale = LOCALE[lang]
  if (!Speech?.available || !locale) return "unavailable"
  try {
    const state = await Speech.available({
      langs: [locale],
      processLocally: true,
    })
    return state === "available" || state === "downloadable"
      ? state
      : "unavailable"
  } catch {
    return "unavailable"
  }
}

/** Starts listening; calls `onText` with each finished phrase. Returns a stop function. */
export async function dictate(
  lang: string,
  onText: (text: string) => void,
  onEnd: () => void
): Promise<() => void> {
  const locale = LOCALE[lang]!
  if ((await dictationState(lang)) === "downloadable")
    await Speech!.install?.({ langs: [locale], processLocally: true })
  const rec = new Speech!()
  rec.lang = locale
  rec.processLocally = true
  rec.interimResults = false
  rec.continuous = true
  rec.onresult = (e) => {
    const last = e.results[e.results.length - 1]
    if (last?.isFinal) onText(last[0]!.transcript.trim())
  }
  rec.onend = onEnd
  rec.onerror = onEnd
  rec.start()
  return () => rec.stop()
}
