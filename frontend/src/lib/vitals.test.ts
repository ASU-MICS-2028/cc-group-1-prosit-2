import { describe, expect, it } from "vitest"
import { ageFrom, bmi, estimatedBirthDate, vitalsFlags } from "./vitals"

describe("vitalsFlags", () => {
  it("flags nothing for normal adult readings", () => {
    expect(
      vitalsFlags({
        systolic: 118,
        diastolic: 76,
        temperatureC: 36.8,
        pulse: 72,
      })
    ).toEqual([])
  })

  it("flags blood pressure bands, worst band only", () => {
    expect(vitalsFlags({ systolic: 145, diastolic: 85 })).toEqual(["bpHigh"])
    expect(vitalsFlags({ systolic: 130, diastolic: 92 })).toEqual(["bpHigh"])
    expect(vitalsFlags({ systolic: 185, diastolic: 95 })).toEqual(["bpCrisis"])
    expect(vitalsFlags({ systolic: 150, diastolic: 121 })).toEqual(["bpCrisis"])
    expect(vitalsFlags({ systolic: 85, diastolic: 55 })).toEqual(["bpLow"])
  })

  it("flags temperature and pulse", () => {
    expect(vitalsFlags({ temperatureC: 38.2 })).toEqual(["fever"])
    expect(vitalsFlags({ temperatureC: 39.8 })).toEqual(["highFever"])
    expect(vitalsFlags({ temperatureC: 34.5 })).toEqual(["lowTemp"])
    expect(vitalsFlags({ pulse: 120 })).toEqual(["pulseHigh"])
    expect(vitalsFlags({ pulse: 45 })).toEqual(["pulseLow"])
  })
})

describe("bmi", () => {
  it("needs both weight and height", () => {
    expect(bmi(70, 175)).toBe(22.9)
    expect(bmi(70)).toBeUndefined()
  })
})

describe("ages", () => {
  const today = new Date(2026, 9, 26)
  it("counts whole years, before and after the birthday", () => {
    expect(ageFrom("1990-10-26", today)).toBe(36)
    expect(ageFrom("1990-10-27", today)).toBe(35)
  })
  it("estimates a birth date from an age", () => {
    expect(estimatedBirthDate(40, today)).toBe("1986-07-01")
  })
})
