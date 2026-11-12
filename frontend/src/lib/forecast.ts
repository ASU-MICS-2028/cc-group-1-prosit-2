/**
 * Next week's fever cases per district, from the weekly counts the officer already sees.
 *
 * Holt's linear exponential smoothing: a level and a trend, each updated every week, recent weeks
 * weighing more. It is small enough to explain in a sentence and to run on a phone, needs no
 * training data beyond the district's own weeks, and its error band comes from how far its own
 * one-week-ahead guesses were off in the past. It is a planning aid (how many malaria tests and
 * how much paracetamol to send where), not a diagnosis and not machine learning.
 */

export interface Forecast {
  /** the best guess, rounded */
  expected: number
  /** a likely range (about 95%), never below 0 */
  low: number
  high: number
  /** expected compared with the average of the weeks used */
  trend: "rising" | "steady" | "falling"
  /** how many weeks it is based on */
  weeks: number
}

export const MIN_WEEKS = 4
const ALPHA = 0.5 // how fast the level follows new weeks
const BETA = 0.3 // how fast the trend follows changes in the level

/** Forecast the week after the last one in `series` (oldest first), or null with too few weeks. */
export function forecastNext(series: readonly number[]): Forecast | null {
  if (series.length < MIN_WEEKS) return null
  let level = series[0]!
  let trend = series[1]! - series[0]!
  const errors: number[] = []
  for (let i = 1; i < series.length; i++) {
    const guess = level + trend
    errors.push(series[i]! - guess)
    const previous = level
    level = ALPHA * series[i]! + (1 - ALPHA) * (level + trend)
    trend = BETA * (level - previous) + (1 - BETA) * trend
  }
  const raw = Math.max(0, level + trend)
  const sd = Math.sqrt(errors.reduce((s, e) => s + e * e, 0) / errors.length)
  const average = series.reduce((s, v) => s + v, 0) / series.length
  const expected = Math.round(raw)
  return {
    expected,
    low: Math.max(0, Math.floor(raw - 1.96 * sd)),
    high: Math.ceil(raw + 1.96 * sd),
    // A change must be both relative (25%) and real (2 patients) to count: with one or two cases
    // a week, a single extra patient is noise, not a trend.
    trend:
      raw >= average * 1.25 && raw - average >= 2
        ? "rising"
        : raw <= average * 0.75 && average - raw >= 2
          ? "falling"
          : "steady",
    weeks: series.length,
  }
}

export interface DistrictForecast extends Forecast {
  district: string
}

/**
 * One forecast per district from complete weeks only (the current week has just started, and a
 * suppressed week has no counts). Districts with too little history are left out. Rising first.
 */
export function forecastByDistrict(
  rows: readonly {
    district: string
    week: string
    patients_fever: number | null
    suppressed: boolean
  }[],
  currentWeek: string | undefined,
  lookback = 8
): DistrictForecast[] {
  const districts = [...new Set(rows.map((r) => r.district))]
  const order = { rising: 0, steady: 1, falling: 2 }
  return districts
    .flatMap((district) => {
      const series = rows
        .filter(
          (r) =>
            r.district === district &&
            r.week !== currentWeek &&
            !r.suppressed &&
            r.patients_fever != null
        )
        .sort((a, b) => a.week.localeCompare(b.week))
        .slice(-lookback)
        .map((r) => r.patients_fever!)
      const f = forecastNext(series)
      return f ? [{ district, ...f }] : []
    })
    .sort(
      (a, b) =>
        order[a.trend] - order[b.trend] ||
        b.expected - a.expected ||
        a.district.localeCompare(b.district)
    )
}
