import {
  CalendarDays,
  ChartColumn,
  CircleUser,
  House,
  Plus,
  RefreshCw,
  Users,
  type LucideIcon,
} from "lucide-react"
import type { Role } from "@/auth/session"

export interface NavItem {
  to: string
  key: `nav.${"home" | "patients" | "add" | "appointments" | "summary" | "sync" | "profile"}`
  icon: LucideIcon
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
const sync: NavItem = {
  to: "/sync",
  key: "nav.sync",
  icon: RefreshCw,
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
  return role === "district_officer" || role === "admin"
    ? [summary, sync, profile]
    : [home, patients, add, appointments, profile]
}
