import { toast } from "@/lib/toast"
import i18n from "@/i18n"

/**
 * The app's standard "it worked" and "it didn't" messages, so every screen says them the same way.
 * Saving is to this phone first; offline, the message adds that it will go to the server later.
 */
export function saved(title: string) {
  toast.success(title, navigator.onLine ? undefined : i18n.t("toast.willSend"))
}

/** The phone's own database refused the save (storage full, private mode). The form stays filled. */
export function saveFailed(error?: unknown) {
  if (error) console.error(error)
  toast.error(i18n.t("toast.saveFailed"), i18n.t("toast.saveFailedText"))
}
