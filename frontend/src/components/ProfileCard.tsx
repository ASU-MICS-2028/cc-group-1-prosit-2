import type { ReactNode } from "react"
import { useTranslation } from "react-i18next"
import type { User } from "@/auth/session"
import { MapPin } from "@/components/icons"
import { useProfilePhoto } from "@/features/profile/profilePhoto"
import { cn } from "@/lib/utils"

const initials = (name: string) =>
  name
    .split(" ")
    .filter(Boolean)
    .slice(0, 2)
    .map((p) => p[0]!.toUpperCase())
    .join("")

/** The health worker's photo, or their initials when there is none. */
export function Avatar({
  user,
  className,
}: {
  user: User
  className?: string
}) {
  const url = useProfilePhoto(user.id)
  return url ? (
    <img
      src={url}
      alt=""
      className={cn("rounded-full object-cover", className)}
    />
  ) : (
    <span
      aria-hidden
      className={cn(
        "grid place-items-center rounded-full bg-primary font-semibold text-primary-foreground",
        className
      )}
    >
      {initials(user.name)}
    </span>
  )
}

/**
 * The health worker's card with the photo on its top-left edge: half of the circle sits outside
 * the card, half inside, and a ring in the page colour keeps the circle from touching the card.
 */
export function ProfileCard({
  user,
  children,
}: {
  user: User
  children?: ReactNode
}) {
  const { t } = useTranslation()
  return (
    // the top margin leaves room for the half of the circle that sits above the card
    <section className="relative mt-12 rounded-xl bg-card px-5 pt-14 pb-5 shadow-card">
      <div className="absolute -top-12 left-5 rounded-full bg-background p-1.5">
        <Avatar user={user} className="size-[84px] text-2xl" />
      </div>
      <p className="text-lg font-semibold break-words text-foreground">
        {user.name}
      </p>
      <p className="text-sm font-medium text-muted-foreground">
        {t(`roles.${user.role}`)}
      </p>
      <p className="mt-1 flex items-center gap-1 text-sm text-muted-foreground">
        <MapPin className="size-4 text-primary" />
        {user.district}
      </p>
      {children}
    </section>
  )
}
