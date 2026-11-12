/**
 * "Get the latest version" (Profile): asks the server for a newer app at once instead of waiting
 * for the browser's own check, switches to it if there is one, and reloads either way. Records
 * waiting to be sent are safe: they live in the phone's database, not in the page.
 */
export async function getLatestVersion(): Promise<void> {
  const registration = await navigator.serviceWorker
    ?.getRegistration()
    .catch(() => undefined)
  if (!registration) return window.location.reload()

  await registration.update().catch(() => undefined)
  const waiting =
    registration.waiting ??
    (await new Promise<ServiceWorker | null>((resolve) => {
      // A new version may still be downloading: give it a few seconds.
      const installing = registration.installing
      if (!installing) return resolve(null)
      installing.addEventListener("statechange", () => {
        if (
          installing.state === "installed" ||
          installing.state === "redundant"
        )
          resolve(registration.waiting)
      })
      setTimeout(() => resolve(registration.waiting), 8000)
    }))

  if (!waiting) return window.location.reload()
  navigator.serviceWorker.addEventListener(
    "controllerchange",
    () => window.location.reload(),
    { once: true }
  )
  waiting.postMessage({ type: "SKIP_WAITING" })
  // In case the switch doesn't announce itself.
  setTimeout(() => window.location.reload(), 4000)
}

/** When this copy of the app was built, shown on Profile so people can tell versions apart. */
export const BUILT_AT: string = __APP_BUILT_AT__
