import { describe, expect, it } from "vitest"
import {
  ageFrom,
  belowReadings,
  bmi,
  estimatedBirthDate,
  higherPriority,
  priorityFromReadings,
  vitalsFlags,
} from "./vitals"

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

describe("automatic referral priority", () => {
  const now = Date.parse("2026-11-12T10:00:00Z")
  const at = (daysAgo: number) =>
    new Date(now - daysAgo * 86_400_000).toISOString()

  it("is routine with no reading, an ordinary one, or one older than a week", () => {
    expect(priorityFromReadings(undefined, now)).toBe("routine")
    expect(
      priorityFromReadings(
        { systolic: 150, diastolic: 95, takenAt: at(1) },
        now
      )
    ).toBe("routine")
    expect(
      priorityFromReadings(
        { systolic: 185, diastolic: 121, takenAt: at(8) },
        now
      )
    ).toBe("routine")
  })

  it("is urgent with one danger sign", () => {
    expect(
      priorityFromReadings(
        { systolic: 185, diastolic: 121, takenAt: at(1) },
        now
      )
    ).toBe("urgent")
    expect(
      priorityFromReadings({ temperatureC: 39.8, takenAt: at(0) }, now)
    ).toBe("urgent")
  })

  it("is an emergency with two danger signs, or BP of 200/130 and above", () => {
    expect(
      priorityFromReadings(
        { systolic: 185, diastolic: 121, temperatureC: 39.8, takenAt: at(0) },
        now
      )
    ).toBe("emergency")
    expect(
      priorityFromReadings(
        { systolic: 205, diastolic: 110, takenAt: at(0) },
        now
      )
    ).toBe("emergency")
  })

  it("never lets the default go below the readings, and warns when lowered", () => {
    expect(higherPriority("routine", "urgent")).toBe("urgent")
    expect(higherPriority("emergency", "urgent")).toBe("emergency")
    expect(belowReadings("routine", "urgent")).toBe(true)
    expect(belowReadings("emergency", "urgent")).toBe(false)
  })
})
