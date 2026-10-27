import i18n from "i18next"
import { initReactI18next } from "react-i18next"
import en from "./locales/en.json"

// The five languages in the brief. Each is written in itself; `sub` is the name an English speaker
// knows it by. `ready: false` lists a language but stops it being chosen; `draft: true` marks a
// translation that native speakers have not checked yet, and the app says so. Any text missing from
// a file shows in English.
export const LANGUAGES = [
  { code: "en", label: "English", sub: "English", ready: true, draft: false },
  { code: "tw", label: "Twi", sub: "Akan", ready: true, draft: true },
  { code: "ee", label: "Eʋegbe", sub: "Ewe", ready: true, draft: true },
  { code: "ha", label: "Hausa", sub: "Hausa", ready: true, draft: true },
  // Not machine-drafted: health instructions in Dagbani need a native speaker first.
  { code: "dag", label: "Dagbanli", sub: "Dagbani", ready: false, draft: true },
] as const

export type LanguageCode = (typeof LANGUAGES)[number]["code"]

/** Whether the app can be used in this language yet. */
export function isReady(code: string): boolean {
  return LANGUAGES.some((l) => l.code === code && l.ready)
}

// English ships in the main bundle (it is the fallback); every other language
// is its own chunk, downloaded only when the user picks it.
const loaders: Record<
  Exclude<LanguageCode, "en">,
  () => Promise<{ default: Record<string, unknown> }>
> = {
  tw: () => import("./locales/tw.json"),
  ee: () => import("./locales/ee.json"),
  dag: () => import("./locales/dag.json"),
  ha: () => import("./locales/ha.json"),
}

const STORAGE_KEY = "healthconnect.lang"

function isLanguage(value: string | null): value is LanguageCode {
  return LANGUAGES.some((l) => l.code === value)
}

function readSavedLanguage(): LanguageCode {
  try {
    const saved = localStorage.getItem(STORAGE_KEY)
    if (isLanguage(saved) && isReady(saved)) return saved
  } catch {
    // storage can be blocked; fall back to English
  }
  return "en"
}

/** True once a language has been chosen on this phone (the welcome screen is shown until then). */
export function hasChosenLanguage(): boolean {
  try {
    return isLanguage(localStorage.getItem(STORAGE_KEY))
  } catch {
    return true // storage blocked: don't trap the person on the welcome screen
  }
}

/** Switches the app language and remembers it on this phone. */
export async function setLanguage(code: LanguageCode) {
  // Remember the choice first, so it sticks even if the person moves on before the file loads.
  try {
    localStorage.setItem(STORAGE_KEY, code)
  } catch {
    // ignore: the choice just won't be remembered
  }
  if (code !== "en" && !i18n.hasResourceBundle(code, "translation")) {
    const bundle = await loaders[code]()
    i18n.addResourceBundle(code, "translation", bundle.default)
  }
  await i18n.changeLanguage(code)
  document.documentElement.lang = code
}

void i18n.use(initReactI18next).init({
  lng: "en",
  fallbackLng: "en",
  resources: { en: { translation: en } },
  interpolation: { escapeValue: false }, // React already escapes
})

const saved = readSavedLanguage()
if (saved !== "en") void setLanguage(saved)

export default i18n
