/**
 * The HealthConnect Ghana logo (Team Godabeg logo pack, public/brand). Each variant has a light
 * and a dark-theme image; only the one for the current theme is shown (and read out).
 */

const ALT = "HealthConnect Ghana"

/** The full logo, symbol over the wordmark: the welcome screen. */
export function BrandLogo({ className = "" }: { className?: string }) {
  return (
    <span className={`block ${className}`}>
      <img
        src="/brand/logo-full-blue.png"
        alt={ALT}
        width={720}
        height={569}
        className="h-auto w-full dark:hidden"
      />
      <img
        src="/brand/logo-full-white.png"
        alt={ALT}
        width={720}
        height={569}
        className="hidden h-auto w-full dark:block"
      />
    </span>
  )
}

/** Symbol beside the wordmark, for headers: sign-in and the sidebar. `size` is the height in px. */
export function BrandLockup({
  size = 40,
  className = "",
}: {
  size?: number
  className?: string
}) {
  return (
    <span className={`inline-flex items-center gap-2.5 ${className}`}>
      <img src="/brand/symbol-blue.png" alt="" width={size} height={size} />
      <img
        src="/brand/wordmark-blue.png"
        alt={ALT}
        height={size * 0.78}
        width={Math.round(size * 0.78 * (616 / 120))}
        className="dark:hidden"
      />
      <img
        src="/brand/wordmark-white.png"
        alt={ALT}
        height={size * 0.78}
        width={Math.round(size * 0.78 * (616 / 120))}
        className="hidden dark:block"
      />
    </span>
  )
}
