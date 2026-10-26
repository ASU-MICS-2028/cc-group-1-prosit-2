import { createBrowserRouter, type RouteObject } from "react-router-dom"
import { CLINICAL_ROLES, type Role } from "@/auth/session"
import { AppLayout } from "./AppLayout"
import { requireRole, signedOutOnly } from "./guards"
import { Loading } from "./Loading"

const clinical = requireRole(...CLINICAL_ROLES)
const everyone = requireRole(
  ...([...CLINICAL_ROLES, "district_officer", "admin"] as Role[])
)

// Every page is its own chunk (`lazy`), so the first screen only downloads
// the code it needs. Each page module exports a component named `Component`.
// Forms and detail pages hide the phone's bottom bar (`hideBottomNav`) so a half-done
// form is not lost by tapping a tab by mistake.
export const routes: RouteObject[] = [
  {
    path: "/login",
    loader: signedOutOnly,
    HydrateFallback: Loading,
    lazy: () => import("@/features/auth/LoginPage"),
  },
  {
    path: "/",
    element: <AppLayout />,
    HydrateFallback: Loading,
    children: [
      {
        index: true,
        loader: clinical,
        lazy: () => import("@/features/home/HomePage"),
      },
      {
        path: "patients",
        loader: clinical,
        lazy: () => import("@/features/patients/PatientListPage"),
      },
      {
        path: "patients/new",
        loader: clinical,
        handle: { hideBottomNav: true },
        lazy: () => import("@/features/patients/NewPatientPage"),
      },
      {
        path: "patients/:id",
        loader: clinical,
        handle: { hideBottomNav: true },
        lazy: () => import("@/features/patients/PatientDetailPage"),
      },
      {
        path: "patients/:id/vitals/new",
        loader: clinical,
        handle: { hideBottomNav: true },
        lazy: () => import("@/features/vitals/NewVitalsPage"),
      },
      {
        path: "patients/:id/appointments/new",
        loader: clinical,
        handle: { hideBottomNav: true },
        lazy: () => import("@/features/appointments/NewAppointmentPage"),
      },
      {
        path: "appointments",
        loader: clinical,
        lazy: () => import("@/features/appointments/AppointmentsPage"),
      },
      {
        path: "surveillance",
        loader: requireRole("district_officer", "admin"),
        lazy: () => import("@/features/surveillance/SurveillancePage"),
      },
      {
        path: "sync",
        loader: everyone,
        lazy: () => import("@/features/sync/SyncPage"),
      },
      {
        path: "profile",
        loader: everyone,
        lazy: () => import("@/features/profile/ProfilePage"),
      },
      { path: "*", lazy: () => import("@/features/NotFoundPage") },
    ],
  },
]

export const router = createBrowserRouter(routes)
