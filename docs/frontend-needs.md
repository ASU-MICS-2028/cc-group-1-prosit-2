# What the frontend needs (for the Tue 27 Oct contract meeting)

**Owner:** Tijani (Frontend / UI / DevOps) · **Status:** proposal, to agree with Bernard (API), Liza (data, sync) and Germain (roles, consent).

The PWA in `frontend/` already works offline against this shape: screens save to the phone's own database and queue each change in an outbox. The meeting needs to confirm or change the field names below. After that, `frontend/src/db/types.ts` is updated to match and becomes the contract.

## MVP screens

| Screen | Route | Who | Online? |
|---|---|---|---|
| Sign-in | `/login` | everyone | yes, the first time (demo sign-in for now) |
| Home (counts, "Register a patient") | `/` | CHV, nurse, doctor | no |
| Patient list + search | `/patients` | CHV, nurse, doctor | no |
| Register a patient (consent first) | `/patients/new` | CHV, nurse, doctor | no |
| Patient record (details, vitals, visits) | `/patients/:id` | CHV, nurse, doctor | no |
| Record vitals (live "refer today" prompt) | `/patients/:id/vitals/new` | CHV, nurse, doctor | no |
| Book a visit / referral / teleconsult | `/patients/:id/appointments/new` | CHV, nurse, doctor | no |
| Upcoming visits | `/appointments` | CHV, nurse, doctor | no |
| Sync (what's waiting, Sync now) | `/sync` | everyone | n/a |
| District summary (anonymised counts) | `/surveillance` | district officer, admin | yes |
| Profile (language, sign out) | `/profile` | everyone | no |

Officers never get patient names; the API must enforce that too, since hiding a screen is not access control.

## Roles (to confirm with Germain's matrix)

`chv`, `nurse`, `doctor`, `district_officer`, `admin`

## Records

Ids are **UUIDs made on the phone** (`crypto.randomUUID()`), so a record has its id before it reaches the server and offline records can link to each other (vitals → patient). Times are ISO 8601 UTC strings. `version` is the server's row version: `0` = never synced.

### Patient
| Field | Type | Notes |
|---|---|---|
| id | uuid | from the phone |
| givenName, familyName | string | required |
| sex | `female` \| `male` | required |
| dateOfBirth | `YYYY-MM-DD` | when unknown, the CHV enters an age; we store 1 July of that year |
| birthDateEstimated | boolean | |
| phone | string? | Ghana number |
| nationalId | string? | Ghana Card or NHIS number |
| community, district | string | required |
| preferredLanguage | `en` \| `tw` \| `ee` \| `dag` \| `ha` | |
| consent | `{ given: true, method: "verbal" \| "thumbprint" \| "signature", at }` | nothing is saved without it |
| createdAt, updatedAt | ISO time | |
| version | int | |

### Vitals
| Field | Type | Notes |
|---|---|---|
| id, patientId | uuid | |
| takenAt | ISO time | |
| systolic, diastolic | int? mmHg | both or neither |
| temperatureC | number? | |
| pulse | int? per minute | |
| weightKg, heightCm | number? | |
| notes | string? | |
| createdAt, version | | |

At least one measurement is required. The app's flags (high BP, fever…) are prompts for the CHV, not stored data. The server can recompute them for surveillance.

### Appointment (visit, referral or teleconsult)
| Field | Type | Notes |
|---|---|---|
| id, patientId | uuid | |
| type | `follow_up` \| `antenatal` \| `postnatal` \| `referral` \| `teleconsult` | |
| scheduledAt | ISO time | in the future when booked |
| facility | string | free text for the MVP |
| priority | `routine` \| `urgent` \| `emergency` | referral prioritisation |
| reason | string | |
| status | `scheduled` \| `completed` \| `missed` \| `cancelled` | |
| createdAt, updatedAt, version | | |

## Endpoints the frontend calls

All under `/api`, JSON, `Authorization: Bearer <token>`. nginx serves the app and `/api` on the same address, so CORS isn't needed.

| Method | Path | Body / result | Used by |
|---|---|---|---|
| POST | `/auth/code` | `{ phone }` (E.164, +233…) → 204; 404 if the number isn't a registered health worker; 429 if asked too often | sign-in step 1 (ADR 0004) |
| POST | `/auth/verify` | `{ phone, code }` → `{ token, expiresAt, user: { id, name, role, district } }`; 401 wrong or expired code | sign-in step 2 |
| PUT | `/patients/{id}` | Patient → 200/201 with the saved record | sync |
| PUT | `/vitals/{id}` | Vitals → 200/201 | sync |
| PUT | `/appointments/{id}` | Appointment → 200/201 | sync |
| GET | `/patients?updatedSince=` | → Patient[] | pulling other workers' changes (stretch) |
| PUT | `/appointments/{id}` with `referralStatus` | `requested` → `accepted` / `declined` → `seen`, plus `handledBy`, `handledAt` | referral inbox (nurse, doctor) |
| GET | `/surveillance/weekly` | → rows of `surveillance_weekly` (db/rbac.sql): `{ district, week, patients_seen, patients_high_bp, patients_fever, suppressed }`, cells under 5 null | district summary (officer) |
| GET | `/health` | → 200 | load balancer / deploy check |

**Why PUT by id:** on 2G a reply can be lost after the server saved the record. The phone then sends it again, and a PUT to the same id saves it once.

### What the frontend expects back
- **401** → the app signs the person out.
- **403** → the role isn't allowed (Germain's checks). The item stays in the outbox, marked failed.
- **409** → conflict: the server has a newer `version`. The item is kept and shown as "Needs review". **Liza:** the conflict policy decides what happens next (e.g. last-writer-wins on vitals, review on patient details).
- **422** → validation errors, `{ errors: { field: message } }`.

## Asks
- **Bernard:** implement `/auth/code` and `/auth/verify` as above (ADR 0004). Until they exist the app runs in demo mode; set `VITE_AUTH_MODE=api` to switch.
- **Liza:** confirm the field names against the ERD; give me the conflict policy and the summary's shape.
- **Germain:** confirm the role names and whether a thumbprint is enough for consent in the demo (the biometric part is a mock).
