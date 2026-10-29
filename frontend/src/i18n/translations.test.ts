import { describe, expect, it } from "vitest"
import en from "./locales/en.json"
import ee from "./locales/ee.json"
import ha from "./locales/ha.json"
import tw from "./locales/tw.json"
import i18n, { LANGUAGES, setLanguage } from "."

type Tree = { [k: string]: string | Tree }
const flat = (o: Tree, p = ""): Record<string, string> =>
  Object.entries(o).reduce(
    (acc, [k, v]) => ({
      ...acc,
      ...(typeof v === "string" ? { [p + k]: v } : flat(v, `${p}${k}.`)),
    }),
    {} as Record<string, string>
  )
const vars = (s: string) =>
  [...s.matchAll(/\{\{(\w+)\}\}/g)].map((m) => m[1]).sort()

const english = flat(en as Tree)
describe.each([
  ["tw", tw],
  ["ee", ee],
  ["ha", ha],
])("%s translation", (code, file) => {
  const lang = flat(file as Tree)
  it("has every English string and nothing extra", () => {
    expect(Object.keys(lang).sort()).toEqual(Object.keys(english).sort())
  })
  it("keeps every {{placeholder}} of the English text", () => {
    for (const [key, text] of Object.entries(english))
      expect([key, vars(lang[key])]).toEqual([key, vars(text)])
  })
  it("is listed as ready and marked as a draft until native speakers check it", () => {
    expect(LANGUAGES.find((l) => l.code === code)).toMatchObject({
      ready: true,
      draft: true,
    })
  })
})

describe("switching language", () => {
  it("loads the language, translates, and falls back to English for anything missing", async () => {
    await setLanguage("ha")
    expect(i18n.t("nav.home")).toBe("Gida")
    expect(i18n.t("patients.age", { count: 3 })).toBe("Shekaru 3")
    expect(document.documentElement.lang).toBe("ha")
    expect(localStorage.getItem("healthconnect.lang")).toBe("ha")
    await setLanguage("tw")
    expect(i18n.t("vitals.referToday")).toBe(
      "Fa ɔyarefoɔ no kɔ ayaresabea ɛnnɛ."
    )
  })
})
