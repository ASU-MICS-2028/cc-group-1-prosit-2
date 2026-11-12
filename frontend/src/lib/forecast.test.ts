import { describe, expect, it } from "vitest"
import { forecastByDistrict, forecastNext } from "./forecast"

describe("forecastNext", () => {
  it("needs at least 4 weeks", () => {
    expect(forecastNext([1, 2, 3])).toBeNull()
  })

  it("expects a flat series to stay where it is, with a tight range", () => {
    const f = forecastNext([5, 5, 5, 5, 5, 5])!
    expect(f).toMatchObject({ expected: 5, low: 5, high: 5, trend: "steady" })
  })

  it("follows a steady rise and calls it rising", () => {
    const f = forecastNext([2, 3, 4, 5, 6, 7, 8, 9])!
    expect(f.expected).toBeGreaterThanOrEqual(9)
    expect(f.trend).toBe("rising")
    expect(f.low).toBeLessThanOrEqual(f.expected)
    expect(f.high).toBeGreaterThanOrEqual(f.expected)
  })

  it("never forecasts fewer than 0 cases", () => {
    const f = forecastNext([9, 7, 5, 3, 1, 0])!
    expect(f.expected).toBeGreaterThanOrEqual(0)
    expect(f.low).toBe(0)
    expect(f.trend).toBe("falling")
  })

  it("does not call one extra case a rise when counts are tiny", () => {
    expect(forecastNext([0, 0, 1, 0, 0, 1, 1])!.trend).toBe("steady")
  })

  it("widens the range when the weeks jump about", () => {
    const calm = forecastNext([4, 5, 4, 5, 4, 5])!
    const wild = forecastNext([0, 9, 1, 8, 0, 9])!
    expect(wild.high - wild.low).toBeGreaterThan(calm.high - calm.low)
  })
})

describe("forecastByDistrict", () => {
  const week = (n: number) => `2026-09-${String(n + 1).padStart(2, "0")}`
  const rows = [
    ...[1, 1, 2, 3, 5, 8].map((f, i) => ({
      district: "Kintampo North",
      week: week(i),
      patients_fever: f,
      suppressed: false,
    })),
    ...[3, 3, 3, 3, 3, 3].map((f, i) => ({
      district: "Shai-Osudoku",
      week: week(i),
      patients_fever: f,
      suppressed: false,
    })),
    // too few counted weeks: left out
    ...[0, 1, 2, 3, 4, 5].map((i) => ({
      district: "Sawla-Tuna-Kalba",
      week: week(i),
      patients_fever: null,
      suppressed: true,
    })),
    // the current week has only just started: not used
    {
      district: "Shai-Osudoku",
      week: week(6),
      patients_fever: 40,
      suppressed: false,
    },
  ]

  it("forecasts each district with enough complete weeks, rising first", () => {
    const list = forecastByDistrict(rows, week(6))
    expect(list.map((d) => d.district)).toEqual([
      "Kintampo North",
      "Shai-Osudoku",
    ])
    expect(list[0]!.trend).toBe("rising")
    expect(list[1]).toMatchObject({ expected: 3, trend: "steady" })
  })
})
