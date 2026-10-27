import { useRef, useState } from "react"
import { useTranslation } from "react-i18next"
import { useNavigate } from "react-router-dom"
import { clearSession, useSession } from "@/auth/session"
import { Dropdown } from "@/components/form/Dropdown"
import { Field } from "@/components/form/Field"
import { Segmented } from "@/components/form/Segmented"
import { LogOut, PenLine, X } from "@/components/icons"
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
                onClick={() => void removeProfilePhoto(session.user.id)}
              >
                <X className="size-5" />
                {t("profile.removePhoto")}
              </Button>
            ) : null}
          </div>
          {photoError ? (
            <p role="alert" className="mt-2 text-sm text-destructive">
              {t("profile.photoError")}
            </p>
          ) : (
            <p className="mt-2 text-xs text-muted-foreground">
              {t("profile.photoHint")}
            </p>
          )}
        </ProfileCard>

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
