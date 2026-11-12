import { useRef, useState } from "react"
import { useTranslation } from "react-i18next"
import { toast } from "@/lib/toast"
import { saveFailed } from "@/lib/feedback"
import { ErrorText } from "@/components/form/ErrorText"
import { Link, useNavigate } from "react-router-dom"
import { clearSession, useSession } from "@/auth/session"
import { Dropdown } from "@/components/form/Dropdown"
import { Field } from "@/components/form/Field"
import { Segmented } from "@/components/form/Segmented"
import {
  ChevronRight,
  HeartPulse,
  LogOut,
  PenLine,
  RefreshCw,
  X,
} from "@/components/icons"
import { BUILT_AT, getLatestVersion } from "@/lib/appUpdate"
import { formatDateTime } from "@/lib/dates"
import { cn } from "@/lib/utils"
import { PageHeader } from "@/components/PageHeader"
import { ProfileCard } from "@/components/ProfileCard"
import { Button } from "@/components/ui/button"
import { LANGUAGES, setLanguage, type LanguageCode } from "@/i18n"
import { setThemePref, useThemePref } from "@/lib/theme"
import {
  removeProfilePhoto,
  saveProfilePhoto,
  useProfilePhoto,
} from "./profilePhoto"

export function Component() {
  const { t, i18n } = useTranslation()
  const session = useSession()
  const navigate = useNavigate()
  const picker = useRef<HTMLInputElement>(null)
  const [photoError, setPhotoError] = useState(false)
  const [updating, setUpdating] = useState(false)
  const theme = useThemePref()
  const hasPhoto = useProfilePhoto(session?.user.id) !== null

  function signOut() {
    clearSession()
    navigate("/login", { replace: true })
  }

  async function onPick(e: React.ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0]
    e.target.value = "" // picking the same file again should still work
    if (!file || !session) return
    setPhotoError(false)
    try {
      await saveProfilePhoto(session.user.id, file)
      toast.success(t("toast.photoUpdated"))
    } catch {
      setPhotoError(true)
    }
  }

  if (!session) return null
  return (
    <>
      <PageHeader title={t("profile.title")} />
      <div className="space-y-5">
        <ProfileCard user={session.user}>
          <div className="mt-4 flex flex-wrap gap-2">
            {/* On phones this offers the camera (front-facing) or the gallery */}
            <input
              ref={picker}
              type="file"
              accept="image/*"
              capture="user"
              className="sr-only"
              aria-label={t("profile.choosePhoto")}
              onChange={(e) => void onPick(e)}
            />
            <Button
              size="lg"
              variant="secondary"
              onClick={() => picker.current?.click()}
            >
              <PenLine className="size-5" />
              {hasPhoto ? t("profile.changePhoto") : t("profile.addPhoto")}
            </Button>
            {hasPhoto ? (
              <Button
                size="lg"
                variant="ghost"
                onClick={() =>
                  void removeProfilePhoto(session.user.id).then(
                    () => toast.success(t("toast.photoRemoved")),
                    saveFailed
                  )
                }
              >
                <X className="size-5" />
                {t("profile.removePhoto")}
              </Button>
            ) : null}
          </div>
          {photoError ? (
            <ErrorText className="mt-2">{t("profile.photoError")}</ErrorText>
          ) : (
            <p className="mt-2 text-xs text-muted-foreground">
              {t("profile.photoHint")}
            </p>
          )}
        </ProfileCard>

        <Link
          to="/learn"
          className="flex items-center gap-4 rounded-xl bg-card p-4 shadow-card"
        >
          <HeartPulse className="size-6 shrink-0 text-primary" aria-hidden />
          <span className="flex-1">
            <span className="block text-sm font-semibold text-foreground">
              {t("education.title")}
            </span>
            <span className="block text-xs text-muted-foreground">
              {t("education.homeHint")}
            </span>
          </span>
          <ChevronRight className="size-5 text-muted-foreground" aria-hidden />
        </Link>

        <Field id="language" label={t("profile.language")}>
          <Dropdown
            id="language"
            value={i18n.language as LanguageCode}
            onChange={(code) => void setLanguage(code)}
            options={LANGUAGES.map((l) => ({
              value: l.code,
              label: l.label,
              hint: l.draft ? `${l.sub} · ${t("common.draft")}` : l.sub,
            }))}
          />
        </Field>
        <Segmented
          name="theme"
          legend={t("profile.theme")}
          value={theme}
          onChange={setThemePref}
          options={(["system", "light", "dark"] as const).map((v) => ({
            value: v,
            label: t(`profile.theme_${v}`),
          }))}
        />
        <div className="space-y-1">
          <Button
            size="xl"
            variant="secondary"
            className="w-full"
            disabled={updating}
            onClick={() => {
              setUpdating(true)
              void getLatestVersion()
            }}
          >
            <RefreshCw className={cn("size-5", updating && "animate-spin")} />
            {updating ? t("profile.updating") : t("profile.update")}
          </Button>
          <p className="text-center text-xs text-muted-foreground">
            {t("profile.version", { when: formatDateTime(BUILT_AT) })}
          </p>
        </div>
        <Button
          size="xl"
          variant="destructive"
          className="w-full"
          onClick={signOut}
        >
          <LogOut className="size-5" />
          {t("profile.signOut")}
        </Button>
      </div>
    </>
  )
}
