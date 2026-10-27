import { createBrowserRouter, type RouteObject } from "react-router-dom"
import { CLINICAL_ROLES, type Role } from "@/auth/session"
import { AppLayout } from "./AppLayout"
import { needsLanguage, requireRole, signedOutOnly } from "./guards"
import { Loading } from "./Loading"
import { RouteError } from "./RouteError"

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
    // Start: welcome + language on a new phone, then phone number and SMS code (ADR 0004).
    errorElement: <RouteError />,
    HydrateFallback: Loading,
    children: [
      {
        path: "/welcome",
        loader: signedOutOnly,
        lazy: () => import("@/features/start/WelcomePage"),
      },
      {
        path: "/login",
        loader: needsLanguage,
        lazy: () => import("@/features/auth/LoginPage"),
      },
      {
        path: "/login/code",
        loader: needsLanguage,
        lazy: () => import("@/features/auth/CodePage"),
      },
    ],
  },
  {
    path: "/",
    element: <AppLayout />,
    errorElement: <RouteError />,
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
        path: "patients/:id/edit",
        loader: clinical,
        handle: { hideBottomNav: true },
        lazy: () => import("@/features/patients/EditPatientPage"),
      },
      {
        path: "patients/:id/registered",
        loader: clinical,
        handle: { hideBottomNav: true },
        lazy: () => import("@/features/patients/RegisteredPage"),
      },
      {
        path: "patients/:id/refer",
        loader: clinical,
        handle: { hideBottomNav: true },
        lazy: () => import("@/features/vitals/ReferPage"),
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
        path: "patients/:id/appointments/:appointmentId/edit",
        loader: clinical,
        handle: { hideBottomNav: true },
        lazy: () => import("@/features/appointments/NewAppointmentPage"),
      },
      {
        path: "referrals",
        loader: requireRole("nurse", "doctor"),
        lazy: () => import("@/features/referrals/ReferralsPage"),
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
