import {
  CalendarDays,
  ChartColumn,
  CircleUser,
  House,
  Inbox,
  Plus,
  ShieldCheck,
  HeartPulse,
  UserPlus,
  Users,
  Wallet,
  type IconComponent,
} from "@/components/icons"
import type { Role } from "@/auth/session"

export interface NavItem {
  to: string
  key: `nav.${"home" | "patients" | "add" | "referrals" | "appointments" | "summary" | "payments" | "workers" | "my" | "audit" | "sync" | "profile"}`
  icon: IconComponent
  end: boolean
  /** The design's centre "Agregar" tab: orange when active */
  add?: boolean
}

const home: NavItem = { to: "/", key: "nav.home", icon: House, end: true }
const patients: NavItem = {
  to: "/patients",
  key: "nav.patients",
  icon: Users,
  end: true,
}
const add: NavItem = {
  to: "/patients/new",
  key: "nav.add",
  icon: Plus,
  end: true,
  add: true,
}
const referrals: NavItem = {
  to: "/referrals",
  key: "nav.referrals",
  icon: Inbox,
  end: false,
}
const appointments: NavItem = {
  to: "/appointments",
  key: "nav.appointments",
  icon: CalendarDays,
  end: false,
}
const summary: NavItem = {
  to: "/surveillance",
  key: "nav.summary",
  icon: ChartColumn,
  end: false,
}
const payments: NavItem = {
  to: "/payments",
  key: "nav.payments",
  icon: Wallet,
  end: false,
}
const workers: NavItem = {
  to: "/workers",
  key: "nav.workers",
  icon: UserPlus,
  end: false,
}
const my: NavItem = {
  to: "/my",
  key: "nav.my",
  icon: HeartPulse,
  end: false,
}
const audit: NavItem = {
  to: "/audit",
  key: "nav.audit",
  icon: ShieldCheck,
  end: false,
}
const profile: NavItem = {
  to: "/profile",
  key: "nav.profile",
  icon: CircleUser,
  end: false,
}

/**
 * The places in the menu for each role, in the Figma bottom bar's order
 * (Inicio, Estadísticas, Agregar, Contactos, Mi Perfil): patient work for clinical roles, the
 * summary for officers. Sync is reached from the offline banner, Home and Profile.
 */
export function navFor(role: Role): readonly NavItem[] {
  if (role === "patient") return [my, profile]
  if (role === "admin") return [audit, summary, payments, workers, profile]
  if (role === "district_officer") return [summary, payments, workers, profile]
  // Nurses and doctors receive referrals; CHVs register patients.
  if (role === "nurse" || role === "doctor")
    return [home, patients, referrals, appointments, profile]
  return [home, patients, add, appointments, profile]
}
