import type { Role } from "@/auth/session"
import { api } from "./client"

export interface Worker {
  id: string
  name: string
  phone: string
  role: Exclude<Role, "patient">
  district: string
  active: boolean
  demo: boolean
  addedBy: string | null
  createdAt: string
}

export interface NewWorker {
  name: string
  phone: string
  role: Worker["role"]
  district?: string
}

export const workers = () => api<Worker[]>("/workers")

export const addWorker = (w: NewWorker) =>
  api<Worker>("/workers", { method: "POST", body: w })

export const changeWorker = (
  id: string,
  change: { name?: string; role?: Worker["role"]; active?: boolean }
) =>
  api<Worker>(`/workers/${encodeURIComponent(id)}`, {
    method: "PATCH",
    body: change,
  })
