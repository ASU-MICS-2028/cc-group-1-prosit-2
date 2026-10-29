import { DEMO_MODE } from "./auth"
import { api } from "./client"

/** One row of Liza's surveillance_weekly view (db/rbac.sql): counts only, cells under 5 suppressed. */
export interface WeeklyCount {
  district: string
  week: string // ISO date of the Monday
  patients_seen: number | null
  patients_high_bp: number | null
  patients_fever: number | null
  suppressed: boolean
}

/** GET /api/surveillance/weekly (hc_ghs_official, purpose PUBHLTH). Demo data until the API is live. */
export async function weeklyCounts(): Promise<{
  rows: WeeklyCount[]
  demo: boolean
}> {
  if (DEMO_MODE) return { rows: demoRows(), demo: true }
  const rows = await api<WeeklyCount[]>("/surveillance/weekly")
  // numeric() comes over JSON as strings from node-postgres
  return {
    demo: false,
    rows: rows.map((r) => ({
      ...r,
      week: r.week.slice(0, 10),
      patients_seen: r.patients_seen == null ? null : Number(r.patients_seen),
      patients_high_bp:
        r.patients_high_bp == null ? null : Number(r.patients_high_bp),
      patients_fever:
        r.patients_fever == null ? null : Number(r.patients_fever),
    })),
  }
}

/** Synthetic weekly counts for 8 weeks, with a fever rise in Kintampo North to show an outbreak signal. */
function demoRows(): WeeklyCount[] {
  const monday = new Date()
  monday.setUTCHours(0, 0, 0, 0)
  monday.setUTCDate(monday.getUTCDate() - ((monday.getUTCDay() + 6) % 7))
  const districts: Record<
    string,
    { seen: number[]; bp: number[]; fever: number[] }
  > = {
    "Shai-Osudoku": {
      seen: [31, 28, 35, 33, 37, 34, 39, 22],
      bp: [7, 6, 8, 7, 9, 8, 9, 5],
      fever: [3, 2, 4, 3, 2, 3, 4, 2],
    },
    "Kintampo North": {
      seen: [18, 20, 17, 22, 26, 31, 38, 24],
      bp: [3, 4, 3, 4, 5, 5, 6, 4],
      fever: [2, 2, 3, 4, 7, 11, 16, 10],
    },
    "Tamale Metropolitan": {
      seen: [24, 26, 23, 27, 25, 29, 28, 15],
      bp: [5, 6, 5, 6, 5, 7, 6, 3],
      fever: [3, 4, 3, 3, 4, 3, 5, 2],
    },
    "Sawla-Tuna-Kalba": {
      seen: [6, 4, 7, 3, 5, 6, 4, 2],
      bp: [1, 0, 2, 0, 1, 1, 0, 0],
      fever: [1, 1, 0, 1, 1, 0, 1, 0],
    },
  }
  const rows: WeeklyCount[] = []
  for (const [district, d] of Object.entries(districts)) {
    d.seen.forEach((seen, i) => {
      const week = new Date(monday)
      week.setUTCDate(week.getUTCDate() - 7 * (d.seen.length - 1 - i))
      const suppressed = seen < 5
      rows.push({
        district,
        week: week.toISOString().slice(0, 10),
        patients_seen: suppressed ? null : seen,
        patients_high_bp: suppressed ? null : d.bp[i],
        patients_fever: suppressed ? null : d.fever[i],
        suppressed,
      })
    })
  }
  return rows
}
