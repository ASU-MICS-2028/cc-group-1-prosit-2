import {
  CalendarDays,
  ChartColumn,
  CircleUser,
  House,
  RefreshCw,
  Users,
  type LucideIcon,
} from "lucide-react"
import type { Role } from "@/auth/session"

export interface NavItem {
  to: string
  key: `nav.${"home" | "patients" | "appointments" | "summary" | "sync" | "profile"}`
  icon: LucideIcon
  end: boolean
}

const home: NavItem = { to: "/", key: "nav.home", icon: House, end: true }
const patients: NavItem = {
  to: "/patients",
  key: "nav.patients",
  icon: Users,
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

/** The places in the menu for each role: patient work for clinical roles, the summary for officers. */
export function navFor(role: Role): readonly NavItem[] {
  return role === "district_officer" || role === "admin"
    ? [summary, sync, profile]
    : [home, patients, appointments, sync, profile]
}
