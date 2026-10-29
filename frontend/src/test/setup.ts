import "@testing-library/jest-dom/vitest"
// IndexedDB does not exist in Node: an in-memory copy stands in for the phone's database.
import "fake-indexeddb/auto"
import { cleanup, configure } from "@testing-library/react"
import { afterEach } from "vitest"
import { db } from "@/db/local"
import i18n from "@/i18n"

// Pages are lazy chunks; the first one in a run is compiled on demand.
configure({ asyncUtilTimeout: 10_000 })

window.scrollTo = () => {}

afterEach(async () => {
  cleanup()
  localStorage.clear()
  await Promise.all(db.tables.map((table) => table.clear()))
  await i18n.changeLanguage("en")
})
