import { describe, expect, it } from "vitest"
import type { Prescription } from "@/db/types"
import { codeFor, shareText } from "./prescriptions"
import { spokenText, TOPICS } from "@/features/education/topics"
import i18n from "@/i18n"

describe("prescription code", () => {
  it("is the first 8 hex digits of the id, as the server makes it", () => {
    expect(codeFor("1a2b3c4d-0000-4000-8000-000000000000")).toBe("RX-1A2B-3C4D")
  })
})

describe("sharing a prescription", () => {
  it("lists the code, the patient, each medicine and the doctor", () => {
    const p: Prescription = {
      id: "1a2b3c4d-0000-4000-8000-000000000000",
      patientId: "p",
      code: "RX-1A2B-3C4D",
      items: [
        {
          medicine: "Paracetamol 500 mg",
          dose: "2 tablets",
          frequency: "3 times a day",
          days: 3,
        },
      ],
      status: "active",
      prescriberName: "Kwesi Eshun",
      prescribedAt: "2026-11-12T10:00:00Z",
      createdAt: "2026-11-12T10:00:00Z",
      updatedAt: "2026-11-12T10:00:00Z",
      version: 1,
    }
    const text = shareText(p, "Ama Owusu", i18n.t)
    expect(text).toContain("RX-1A2B-3C4D")
    expect(text).toContain("Ama Owusu")
    expect(text).toContain(
      "Paracetamol 500 mg: 2 tablets, 3 times a day, for 3 days"
    )
    expect(text).toContain("Kwesi Eshun")
  })
})

describe("health education", () => {
  it("reads the title, the points and when to go now, without doubled stops", () => {
    const malaria = TOPICS.find((t) => t.id === "malaria")!
    const text = spokenText(malaria, "Go now if there is")
    expect(text.startsWith(malaria.title)).toBe(true)
    expect(text).toContain("Go now if there is")
    expect(text).not.toContain("..")
  })

  it("has unique topics with points and a source", () => {
    expect(new Set(TOPICS.map((t) => t.id)).size).toBe(TOPICS.length)
    for (const t of TOPICS) {
      expect(t.points.length).toBeGreaterThan(0)
      expect(t.source).not.toBe("")
    }
  })
})
