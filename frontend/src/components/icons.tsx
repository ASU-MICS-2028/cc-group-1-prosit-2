// Icons from the HealthCheck Figma kit (ADR 0002): its own SVGs where it has the icon, otherwise
// classic Material Icons, the family the kit's "_24px" icons come from (Apache-2.0, via
// @material-design-icons/svg). Generated; colours are currentColor. `filled` switches the bottom-nav
// icons to the kit's active variant.
import type { SVGProps } from "react"

export type IconProps = SVGProps<SVGSVGElement> & { filled?: boolean }
export type IconComponent = (props: IconProps) => React.JSX.Element

function icon(viewBox: string, body: string, active?: string): IconComponent {
  return function Icon({ filled, ...props }: IconProps) {
    return (
      <svg
        xmlns="http://www.w3.org/2000/svg"
        viewBox={viewBox}
        width={24}
        height={24}
        fill="none"
        aria-hidden
        focusable={false}
        {...props}
        dangerouslySetInnerHTML={{ __html: filled && active ? active : body }}
      />
    )
  }
}

/** Figma kit (nav/home.svg) */
export const House = icon(
  "0 0 24 24",
  '<path d="M12 5L18.6574 9.83688L16.1145 17.6631H7.8855L5.3426 9.83688L12 5Z" stroke-width="2" stroke="currentColor"/>',
  '<path d="M12 5L18.6574 9.83688L16.1145 17.6631H7.8855L5.3426 9.83688L12 5Z" fill="currentColor"/>'
)

/** Figma kit (nav/contacts.svg) */
export const Users = icon(
  "0 0 24 24",
  '<path d="M12 2C6.48 2 2 6.48 2 12C2 17.52 6.48 22 12 22C17.52 22 22 17.52 22 12C22 6.48 17.52 2 12 2ZM18.36 16.83C16.93 15.09 13.46 14.5 12 14.5C10.54 14.5 7.07 15.09 5.64 16.83C4.62 15.49 4 13.82 4 12C4 7.59 7.59 4 12 4C16.41 4 20 7.59 20 12C20 13.82 19.38 15.49 18.36 16.83ZM8.5 9.5C8.5 7.56 10.06 6 12 6C13.94 6 15.5 7.56 15.5 9.5C15.5 11.44 13.94 13 12 13C10.06 13 8.5 11.44 8.5 9.5Z" stroke-width="2" stroke="currentColor"/>',
  '<path d="M12 2C6.48 2 2 6.48 2 12C2 17.52 6.48 22 12 22C17.52 22 22 17.52 22 12C22 6.48 17.52 2 12 2ZM18.36 16.83C16.93 15.09 13.46 14.5 12 14.5C10.54 14.5 7.07 15.09 5.64 16.83C4.62 15.49 4 13.82 4 12C4 7.59 7.59 4 12 4C16.41 4 20 7.59 20 12C20 13.82 19.38 15.49 18.36 16.83ZM8.5 9.5C8.5 7.56 10.06 6 12 6C13.94 6 15.5 7.56 15.5 9.5C15.5 11.44 13.94 13 12 13C10.06 13 8.5 11.44 8.5 9.5Z" fill-rule="evenodd" clip-rule="evenodd" fill="currentColor"/>'
)

/** Figma kit (nav/add.svg) */
export const Plus = icon(
  "0 0 24 24",
  '<path d="M20 13H13V20H11V13H4V11H11V4H13V11H20V13Z" fill="currentColor"/>'
)

/** Figma kit (nav/today.svg) */
export const CalendarDays = icon(
  "0 0 24 24",
  '<path d="M19 4H18V2H16V4H8V2H6V4H5C3.89 4 3 4.9 3 6V20C3 21.1 3.89 22 5 22H19C20.1 22 21 21.1 21 20V6C21 4.9 20.1 4 19 4ZM19 20H5V9H19V20ZM6.5 13C6.5 11.62 7.62 10.5 9 10.5C10.38 10.5 11.5 11.62 11.5 13C11.5 14.38 10.38 15.5 9 15.5C7.62 15.5 6.5 14.38 6.5 13Z" fill-rule="evenodd" clip-rule="evenodd" fill="currentColor"/>',
  '<path d="M19 4H18V2H16V4H8V2H6V4H5C3.89 4 3 4.9 3 6V20C3 21.1 3.89 22 5 22H19C20.1 22 21 21.1 21 20V6C21 4.9 20.1 4 19 4ZM19 20H5V9H19V20ZM6.5 13C6.5 11.62 7.62 10.5 9 10.5C10.38 10.5 11.5 11.62 11.5 13C11.5 14.38 10.38 15.5 9 15.5C7.62 15.5 6.5 14.38 6.5 13Z" fill-rule="evenodd" clip-rule="evenodd" fill="currentColor"/>'
)

/** Figma kit (nav/profile.svg) */
export const CircleUser = icon(
  "0 0 24 24",
  '<path d="M8 8C8 5.79 9.79 4 12 4C14.21 4 16 5.79 16 8C16 10.21 14.21 12 12 12C9.79 12 8 10.21 8 8ZM12 6C13.1 6 14 6.9 14 8C14 9.1 13.1 10 12 10C10.9 10 10 9.1 10 8C10 6.9 10.9 6 12 6ZM12 15C14.7 15 17.8 16.29 18 17V18H6V17.01C6.2 16.29 9.3 15 12 15ZM12 13C9.33 13 4 14.34 4 17V20H20V17C20 14.34 14.67 13 12 13Z" stroke-width="2" stroke="currentColor"/>',
  '<path d="M12 4C9.79 4 8 5.79 8 8C8 10.21 9.79 12 12 12C14.21 12 16 10.21 16 8C16 5.79 14.21 4 12 4ZM14 8C14 6.9 13.1 6 12 6C10.9 6 10 6.9 10 8C10 9.1 10.9 10 12 10C13.1 10 14 9.1 14 8ZM18 17C17.8 16.29 14.7 15 12 15C9.3 15 6.2 16.29 6 17.01V18H18V17ZM4 17C4 14.34 9.33 13 12 13C14.67 13 20 14.34 20 17V20H4V17Z" fill-rule="evenodd" clip-rule="evenodd" fill="currentColor"/>'
)

/** Figma kit (nav/arrow_back.svg) */
export const ArrowLeft = icon(
  "0 0 24 24",
  '<path d="M20 11H7.83L13.42 5.41L12 4L4 12L12 20L13.41 18.59L7.83 13H20V11Z" fill="currentColor"/>'
)

/** Figma kit (search.svg) */
export const Search = icon(
  "0 0 24 24",
  '<path d="M14.76 13.27L20.49 19L19 20.49L13.27 14.76C12.2 15.53 10.91 16 9.5 16C5.91 16 3 13.09 3 9.5C3 5.91 5.91 3 9.5 3C13.09 3 16 5.91 16 9.5C16 10.91 15.53 12.2 14.76 13.27ZM9.5 5C7.01 5 5 7.01 5 9.5C5 11.99 7.01 14 9.5 14C11.99 14 14 11.99 14 9.5C14 7.01 11.99 5 9.5 5Z" fill-rule="evenodd" clip-rule="evenodd" fill="currentColor"/>'
)

/** Figma kit (check.svg) */
export const Check = icon(
  "0 0 24 24",
  '<path d="M8.99991 16.17L4.82991 12L3.40991 13.41L8.99991 19L20.9999 7L19.5899 5.59L8.99991 16.17Z" fill="currentColor"/>'
)

/** Figma kit (close.svg) */
export const X = icon(
  "0 0 24 24",
  '<path d="M19 6.41L17.59 5L12 10.59L6.41 5L5 6.41L10.59 12L5 17.59L6.41 19L12 13.41L17.59 19L19 17.59L13.41 12L19 6.41Z" fill="currentColor"/>'
)

/** Figma kit (chat_bubble.svg) */
export const MessageCircle = icon(
  "0 0 24 24",
  '<path d="M20 2H4C2.9 2 2 2.9 2 4V22L6 18H20C21.1 18 22 17.1 22 16V4C22 2.9 21.1 2 20 2Z" fill="currentColor"/>'
)

/** Figma kit (mode_edit.svg) */
export const PenLine = icon(
  "0 0 24 24",
  '<path d="M19.06 3.59L20.41 4.94C21.2 5.72 21.2 6.99 20.41 7.77L7.18 21H3V16.82L13.4 6.41L16.23 3.59C17.01 2.81 18.28 2.81 19.06 3.59ZM5 19L6.41 19.06L16.23 9.23L14.82 7.82L5 17.64V19Z" fill-rule="evenodd" clip-rule="evenodd" fill="currentColor"/>'
)

/** Figma kit (cancel.svg) */
export const CircleX = icon(
  "0 0 24 24",
  '<path d="M12 2C6.47 2 2 6.47 2 12C2 17.53 6.47 22 12 22C17.53 22 22 17.53 22 12C22 6.47 17.53 2 12 2ZM12 20C7.59 20 4 16.41 4 12C4 7.59 7.59 4 12 4C16.41 4 20 7.59 20 12C20 16.41 16.41 20 12 20ZM12 10.59L15.59 7L17 8.41L13.41 12L17 15.59L15.59 17L12 13.41L8.41 17L7 15.59L10.59 12L7 8.41L8.41 7L12 10.59Z" fill-rule="evenodd" clip-rule="evenodd" fill="currentColor"/>'
)

/** Figma kit (monitor_heart.svg) */
export const HeartPulse = icon(
  "0 0 20 16",
  '<path d="M2 16C1.45 16 0.979333 15.8043 0.588 15.413C0.196 15.021 0 14.55 0 14V9H5.375L7.1 12.45C7.18333 12.6333 7.30833 12.771 7.475 12.863C7.64167 12.9543 7.81667 13 8 13C8.18333 13 8.35833 12.9543 8.525 12.863C8.69167 12.771 8.81667 12.6333 8.9 12.45L12 6.25L13.1 8.45C13.1833 8.63333 13.3083 8.771 13.475 8.863C13.6417 8.95433 13.8167 9 14 9H20V14C20 14.55 19.8043 15.021 19.413 15.413C19.021 15.8043 18.55 16 18 16H2ZM8 9.75L6.9 7.55C6.81667 7.36667 6.69167 7.229 6.525 7.137C6.35833 7.04567 6.18333 7 6 7H0V2C0 1.45 0.196 0.979333 0.588 0.588C0.979333 0.196 1.45 0 2 0H18C18.55 0 19.021 0.196 19.413 0.588C19.8043 0.979333 20 1.45 20 2V7H14.625L12.9 3.55C12.7167 3.21667 12.4167 3.05 12 3.05C11.5833 3.05 11.2833 3.21667 11.1 3.55L8 9.75Z" fill="currentColor"/>'
)

/** Material Icons "bar_chart" (filled) */
export const ChartColumn = icon(
  "0 0 24 24",
  '<path fill="currentColor" d="M4 9h4v11H4zm12 4h4v7h-4zm-6-9h4v16h-4z"/>'
)

/** Material Icons "sync" (filled) */
export const RefreshCw = icon(
  "0 0 24 24",
  '<path fill="currentColor" d="M12 4V1L8 5l4 4V6c3.31 0 6 2.69 6 6 0 1.01-.25 1.97-.7 2.8l1.46 1.46A7.93 7.93 0 0 0 20 12c0-4.42-3.58-8-8-8zm0 14c-3.31 0-6-2.69-6-6 0-1.01.25-1.97.7-2.8L5.24 7.74A7.93 7.93 0 0 0 4 12c0 4.42 3.58 8 8 8v3l4-4-4-4v3z"/>'
)

/** Material Icons "inbox" (outlined) */
export const Inbox = icon(
  "0 0 24 24",
  '<path fill="currentColor" d="M19 3H5c-1.1 0-2 .9-2 2v14a2 2 0 0 0 2 2h14c1.1 0 2-.9 2-2V5c0-1.1-.9-2-2-2zm0 16H5v-3h3.56c.69 1.19 1.97 2 3.45 2s2.75-.81 3.45-2H19v3zm0-5h-4.99c0 1.1-.9 2-2 2s-2-.9-2-2H5V5h14v9z"/>',
  '<path fill="currentColor" d="M19 3H4.99c-1.11 0-1.98.89-1.98 2L3 19c0 1.1.88 2 1.99 2H19c1.1 0 2-.9 2-2V5a2 2 0 0 0-2-2zm0 12h-4c0 1.66-1.35 3-3 3s-3-1.34-3-3H4.99V5H19v10z"/>'
)

/** Material Icons "location_on" (filled) */
export const MapPin = icon(
  "0 0 24 24",
  '<path fill="currentColor" d="M12 2C8.13 2 5 5.13 5 9c0 5.25 7 13 7 13s7-7.75 7-13c0-3.87-3.13-7-7-7zm0 9.5a2.5 2.5 0 0 1 0-5 2.5 2.5 0 0 1 0 5z"/>'
)

/** Material Icons "call" (filled) */
export const Phone = icon(
  "0 0 24 24",
  '<path fill="currentColor" d="M20.01 15.38c-1.23 0-2.42-.2-3.53-.56a.977.977 0 0 0-1.01.24l-1.57 1.97c-2.83-1.35-5.48-3.9-6.89-6.83l1.95-1.66c.27-.28.35-.67.24-1.02-.37-1.11-.56-2.3-.56-3.53 0-.54-.45-.99-.99-.99H4.19C3.65 3 3 3.24 3 3.99 3 13.28 10.73 21 20.01 21c.71 0 .99-.63.99-1.18v-3.45c0-.54-.45-.99-.99-.99z"/>'
)

/** Material Icons "device_thermostat" (filled) */
export const Thermometer = icon(
  "0 0 24 24",
  '<path fill="currentColor" d="M15 13V5c0-1.66-1.34-3-3-3S9 3.34 9 5v8c-1.21.91-2 2.37-2 4 0 2.76 2.24 5 5 5s5-2.24 5-5c0-1.63-.79-3.09-2-4zm-4-8c0-.55.45-1 1-1s1 .45 1 1h-1v1h1v2h-1v1h1v2h-2V5z"/>'
)

/** Material Icons "monitor_weight" (filled) */
export const Weight = icon(
  "0 0 24 24",
  '<path fill="currentColor" d="M19 3H5c-1.1 0-2 .9-2 2v14c0 1.1.9 2 2 2h14c1.1 0 2-.9 2-2V5c0-1.1-.9-2-2-2zm-7 9c-1.66 0-3-1.34-3-3s1.34-3 3-3 3 1.34 3 3-1.34 3-3 3z"/><path fill="currentColor" d="M10 8.5h1v1h-1zm1.5 0h1v1h-1zm1.5 0h1v1h-1z"/>'
)

/** Material Icons "straighten" (filled) */
export const Ruler = icon(
  "0 0 24 24",
  '<path fill="currentColor" d="M21 6H3c-1.1 0-2 .9-2 2v8c0 1.1.9 2 2 2h18c1.1 0 2-.9 2-2V8c0-1.1-.9-2-2-2zm0 10H3V8h2v4h2V8h2v4h2V8h2v4h2V8h2v4h2V8h2v8z"/>'
)

/** Material Icons "warning" (filled) */
export const TriangleAlert = icon(
  "0 0 24 24",
  '<path fill="currentColor" d="M1 21h22L12 2 1 21zm12-3h-2v-2h2v2zm0-4h-2v-4h2v4z"/>'
)

/** Material Icons "emergency" (filled) */
export const Siren = icon(
  "0 0 24 24",
  '<path fill="currentColor" d="m20.79 9.23-2-3.46L14 8.54V3h-4v5.54L5.21 5.77l-2 3.46L8 12l-4.79 2.77 2 3.46L10 15.46V21h4v-5.54l4.79 2.77 2-3.46L16 12z"/>'
)

/** Material Icons "local_hospital" (filled) */
export const Hospital = icon(
  "0 0 24 24",
  '<path fill="currentColor" d="M19 3H5c-1.1 0-1.99.9-1.99 2L3 19c0 1.1.9 2 2 2h14c1.1 0 2-.9 2-2V5c0-1.1-.9-2-2-2zm-1 11h-4v4h-4v-4H6v-4h4V6h4v4h4v4z"/>'
)

/** Material Icons "verified_user" (filled) */
export const ShieldCheck = icon(
  "0 0 24 24",
  '<path fill="currentColor" d="M12 1 3 5v6c0 5.55 3.84 10.74 9 12 5.16-1.26 9-6.45 9-12V5l-9-4zm-2 16-4-4 1.41-1.41L10 14.17l6.59-6.59L18 9l-8 8z"/>'
)

/** Material Icons "fingerprint" (filled) */
export const Fingerprint = icon(
  "0 0 24 24",
  '<path fill="currentColor" d="M17.81 4.47c-.08 0-.16-.02-.23-.06C15.66 3.42 14 3 12.01 3c-1.98 0-3.86.47-5.57 1.41-.24.13-.54.04-.68-.2a.506.506 0 0 1 .2-.68C7.82 2.52 9.86 2 12.01 2c2.13 0 3.99.47 6.03 1.52.25.13.34.43.21.67a.49.49 0 0 1-.44.28zM3.5 9.72a.499.499 0 0 1-.41-.79c.99-1.4 2.25-2.5 3.75-3.27C9.98 4.04 14 4.03 17.15 5.65c1.5.77 2.76 1.86 3.75 3.25a.5.5 0 0 1-.12.7c-.23.16-.54.11-.7-.12a9.388 9.388 0 0 0-3.39-2.94c-2.87-1.47-6.54-1.47-9.4.01-1.36.7-2.5 1.7-3.4 2.96-.08.14-.23.21-.39.21zm6.25 12.07a.47.47 0 0 1-.35-.15c-.87-.87-1.34-1.43-2.01-2.64-.69-1.23-1.05-2.73-1.05-4.34 0-2.97 2.54-5.39 5.66-5.39s5.66 2.42 5.66 5.39c0 .28-.22.5-.5.5s-.5-.22-.5-.5c0-2.42-2.09-4.39-4.66-4.39-2.57 0-4.66 1.97-4.66 4.39 0 1.44.32 2.77.93 3.85.64 1.15 1.08 1.64 1.85 2.42.19.2.19.51 0 .71-.11.1-.24.15-.37.15zm7.17-1.85c-1.19 0-2.24-.3-3.1-.89-1.49-1.01-2.38-2.65-2.38-4.39 0-.28.22-.5.5-.5s.5.22.5.5c0 1.41.72 2.74 1.94 3.56.71.48 1.54.71 2.54.71.24 0 .64-.03 1.04-.1.27-.05.53.13.58.41.05.27-.13.53-.41.58-.57.11-1.07.12-1.21.12zM14.91 22c-.04 0-.09-.01-.13-.02-1.59-.44-2.63-1.03-3.72-2.1a7.297 7.297 0 0 1-2.17-5.22c0-1.62 1.38-2.94 3.08-2.94 1.7 0 3.08 1.32 3.08 2.94 0 1.07.93 1.94 2.08 1.94s2.08-.87 2.08-1.94c0-3.77-3.25-6.83-7.25-6.83-2.84 0-5.44 1.58-6.61 4.03-.39.81-.59 1.76-.59 2.8 0 .78.07 2.01.67 3.61.1.26-.03.55-.29.64-.26.1-.55-.04-.64-.29a11.14 11.14 0 0 1-.73-3.96c0-1.2.23-2.29.68-3.24 1.33-2.79 4.28-4.6 7.51-4.6 4.55 0 8.25 3.51 8.25 7.83 0 1.62-1.38 2.94-3.08 2.94s-3.08-1.32-3.08-2.94c0-1.07-.93-1.94-2.08-1.94s-2.08.87-2.08 1.94c0 1.71.66 3.31 1.87 4.51.95.94 1.86 1.46 3.27 1.85.27.07.42.35.35.61-.05.23-.26.38-.47.38z"/>'
)

/** Material Icons "female" (filled) */
export const Venus = icon(
  "0 0 24 24",
  '<path fill="currentColor" d="M17.5 9.5C17.5 6.46 15.04 4 12 4S6.5 6.46 6.5 9.5c0 2.7 1.94 4.93 4.5 5.4V17H9v2h2v2h2v-2h2v-2h-2v-2.1c2.56-.47 4.5-2.7 4.5-5.4zm-9 0C8.5 7.57 10.07 6 12 6s3.5 1.57 3.5 3.5S13.93 13 12 13s-3.5-1.57-3.5-3.5z"/>'
)

/** Material Icons "male" (filled) */
export const Mars = icon(
  "0 0 24 24",
  '<path fill="currentColor" d="M9.5 11c1.93 0 3.5 1.57 3.5 3.5S11.43 18 9.5 18 6 16.43 6 14.5 7.57 11 9.5 11zm0-2C6.46 9 4 11.46 4 14.5S6.46 20 9.5 20s5.5-2.46 5.5-5.5c0-1.16-.36-2.23-.97-3.12L18 7.42V10h2V4h-6v2h2.58l-3.97 3.97C11.73 9.36 10.66 9 9.5 9z"/>'
)

/** Material Icons "transgender" (filled) */
export const VenusAndMars = icon(
  "0 0 24 24",
  '<path fill="currentColor" d="M12 8c1.93 0 3.5 1.57 3.5 3.5S13.93 15 12 15s-3.5-1.57-3.5-3.5S10.07 8 12 8zm4.53.38 3.97-3.96V7h2V1h-6v2h2.58l-3.97 3.97C14.23 6.36 13.16 6 12 6s-2.23.36-3.11.97l-.65-.65 1.41-1.41-1.41-1.42L6.82 4.9 4.92 3H7.5V1h-6v6h2V4.42l1.91 1.9-1.42 1.42L5.4 9.15l1.41-1.41.65.65c-.6.88-.96 1.95-.96 3.11a5.5 5.5 0 0 0 4.5 5.41V19H9v2h2v2h2v-2h2v-2h-2v-2.09a5.5 5.5 0 0 0 3.53-8.53z"/>'
)

/** Material Icons "cloud_done" (filled) */
export const Cloud = icon(
  "0 0 24 24",
  '<path fill="currentColor" d="M19.35 10.04A7.49 7.49 0 0 0 12 4C9.11 4 6.6 5.64 5.35 8.04A5.994 5.994 0 0 0 0 14c0 3.31 2.69 6 6 6h13c2.76 0 5-2.24 5-5 0-2.64-2.05-4.78-4.65-4.96zM10 17l-3.5-3.5 1.41-1.41L10 14.17 15.18 9l1.41 1.41L10 17z"/>'
)

/** Material Icons "cloud_off" (filled) */
export const CloudOff = icon(
  "0 0 24 24",
  '<path fill="currentColor" d="M19.35 10.04A7.49 7.49 0 0 0 12 4c-1.48 0-2.85.43-4.01 1.17l1.46 1.46a5.497 5.497 0 0 1 8.05 4.87v.5H19c1.66 0 3 1.34 3 3 0 1.13-.64 2.11-1.56 2.62l1.45 1.45C23.16 18.16 24 16.68 24 15c0-2.64-2.05-4.78-4.65-4.96zM3 5.27l2.75 2.74C2.56 8.15 0 10.77 0 14c0 3.31 2.69 6 6 6h11.73l2 2L21 20.73 4.27 4 3 5.27zM7.73 10l8 8H6c-2.21 0-4-1.79-4-4s1.79-4 4-4h1.73z"/>'
)

/** Material Icons "cloud_upload" (filled) */
export const CloudUpload = icon(
  "0 0 24 24",
  '<path fill="currentColor" d="M19.35 10.04A7.49 7.49 0 0 0 12 4C9.11 4 6.6 5.64 5.35 8.04A5.994 5.994 0 0 0 0 14c0 3.31 2.69 6 6 6h13c2.76 0 5-2.24 5-5 0-2.64-2.05-4.78-4.65-4.96zM14 13v4h-4v-4H7l5-5 5 5h-3z"/>'
)

/** Material Icons "chevron_right" (filled) */
export const ChevronRight = icon(
  "0 0 24 24",
  '<path fill="currentColor" d="M10 6 8.59 7.41 13.17 12l-4.58 4.59L10 18l6-6z"/>'
)

/** Material Icons "logout" (filled) */
export const LogOut = icon(
  "0 0 24 24",
  '<path fill="currentColor" d="m17 7-1.41 1.41L18.17 11H8v2h10.17l-2.58 2.58L17 17l5-5zM4 5h8V3H4c-1.1 0-2 .9-2 2v14c0 1.1.9 2 2 2h8v-2H4V5z"/>'
)

/** Material Icons "person_add" (filled) */
export const UserPlus = icon(
  "0 0 24 24",
  '<path fill="currentColor" d="M15 12c2.21 0 4-1.79 4-4s-1.79-4-4-4-4 1.79-4 4 1.79 4 4 4zm-9-2V7H4v3H1v2h3v3h2v-3h3v-2H6zm9 4c-2.67 0-8 1.34-8 4v2h16v-2c0-2.66-5.33-4-8-4z"/>'
)

/** Material Icons "event" (filled) */
export const CalendarPlus = icon(
  "0 0 24 24",
  '<path fill="currentColor" d="M17 12h-5v5h5v-5zM16 1v2H8V1H6v2H5c-1.11 0-1.99.9-1.99 2L3 19a2 2 0 0 0 2 2h14c1.1 0 2-.9 2-2V5c0-1.1-.9-2-2-2h-1V1h-2zm3 18H5V8h14v11z"/>'
)

/** Material Icons "insights" (filled) */
export const Activity = icon(
  "0 0 24 24",
  '<path fill="currentColor" d="M21 8c-1.45 0-2.26 1.44-1.93 2.51l-3.55 3.56c-.3-.09-.74-.09-1.04 0l-2.55-2.55C12.27 10.45 11.46 9 10 9c-1.45 0-2.27 1.44-1.93 2.52l-4.56 4.55C2.44 15.74 1 16.55 1 18c0 1.1.9 2 2 2 1.45 0 2.26-1.44 1.93-2.51l4.55-4.56c.3.09.74.09 1.04 0l2.55 2.55C12.73 16.55 13.54 18 15 18c1.45 0 2.27-1.44 1.93-2.52l3.56-3.55c1.07.33 2.51-.48 2.51-1.93 0-1.1-.9-2-2-2z"/><path fill="currentColor" d="m15 9 .94-2.07L18 6l-2.06-.93L15 3l-.92 2.07L12 6l2.08.93zM3.5 11 4 9l2-.5L4 8l-.5-2L3 8l-2 .5L3 9z"/>'
)

/** Material Icons "trending_up" (filled) */
export const TrendingUp = icon(
  "0 0 24 24",
  '<path fill="currentColor" d="m16 6 2.29 2.29-4.88 4.88-4-4L2 16.59 3.41 18l6-6 4 4 6.3-6.29L22 12V6z"/>'
)

/** Material Icons "check_circle" (filled) */
export const CircleCheckBig = icon(
  "0 0 24 24",
  '<path fill="currentColor" d="M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm-2 15-5-5 1.41-1.41L10 14.17l7.59-7.59L19 8l-9 9z"/>'
)

/** Material Icons "pending" (filled) */
export const CircleDashed = icon(
  "0 0 24 24",
  '<path fill="currentColor" d="M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zM7 13.5c-.83 0-1.5-.67-1.5-1.5s.67-1.5 1.5-1.5 1.5.67 1.5 1.5-.67 1.5-1.5 1.5zm5 0c-.83 0-1.5-.67-1.5-1.5s.67-1.5 1.5-1.5 1.5.67 1.5 1.5-.67 1.5-1.5 1.5zm5 0c-.83 0-1.5-.67-1.5-1.5s.67-1.5 1.5-1.5 1.5.67 1.5 1.5-.67 1.5-1.5 1.5z"/>'
)

/** Material Icons "schedule" (filled) */
export const Clock = icon(
  "0 0 24 24",
  '<path fill="currentColor" d="M11.99 2C6.47 2 2 6.48 2 12s4.47 10 9.99 10C17.52 22 22 17.52 22 12S17.52 2 11.99 2zM12 20c-4.42 0-8-3.58-8-8s3.58-8 8-8 8 3.58 8 8-3.58 8-8 8z"/><path fill="currentColor" d="M12.5 7H11v6l5.25 3.15.75-1.23-4.5-2.67z"/>'
)
