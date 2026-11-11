# API: clinical records, audit, media and the district summary

What each endpoint takes and gives back. Sign-in is in [auth.md](auth.md); the exact machine-readable contract is [openapi/healthconnect.json](openapi/healthconnect.json), rebuilt on every API build. The code is `backend/Services/RecordsService`.

Every endpoint below needs the token (`Authorization: Bearer ...`). Not signed in is `401`; signed in with the wrong role is `403`. **Who you are comes from the token, never from the request body.** Every data endpoint also checks, on each call, that the worker is still switched on (`401 {error:"unknown_user"}` if not), so switching a worker off takes effect at once, not when the 7-day token expires.

The API runs each request inside one database transaction **as the worker's database role and district**, so Postgres itself refuses what the worker may not see (a CHV never gets another district's patients, even if the API had a bug). See [database/README.md](database/README.md).

## Saving: `PUT /api/<thing>/{id}`

`patients`, `vitals` and `appointments` work the same way. The id is made on the phone, so saving the same thing twice (a lost reply on a weak signal) does no harm.

| Status | Meaning |
|---|---|
| `201` | Created. The body is the saved record, with `version: 1`. |
| `200` | Updated, or the exact same thing sent again. The body is the saved record. |
| `409 {error:"conflict", current}` | Someone else changed it first (the `version` you sent is not the server's). `current` is the server's copy. See [sync.md](sync.md). |
| `422 {errors:{field:"reason"}}` | Something is wrong; the field names are the app's own. Nothing is saved. |
| `403` | Not allowed for this role or patient. |

### `PUT /api/patients/{id}` (chv, nurse, doctor)

The body is the app's `Patient` (see `frontend/src/db/types.ts`): names, sex, `dateOfBirth`, `phone`, `nationalId`, `community`, `district`, `preferredLanguage`, `consent`, `version`.

- Consent comes first: `consent.given` must be true, with a method (`spoken`, `thumbprint`, `signature`). With no recording a named `witness` is required. Nothing about a patient is saved without it (Act 843).
- A phone number is stored as `+233...` whatever way it was typed. A CHV can only save patients in their own district.

### `PUT /api/vitals/{id}` (chv, nurse, doctor)

One set of measurements: `systolic`/`diastolic` (a pair), `temperatureC`, `pulse`, `weightKg`, `heightCm`, `notes`, `takenAt`. At least one value is needed and each must be believable (systolic 50-260, diastolic 30-160, temperature 30-45, pulse 20-250, weight 0.5-300, height 30-250). A reading is never edited: the same reading again is `200`, a different one under the same id is `409`. Each measurement is stored as a coded observation (LOINC/UCUM). The patient must already be on the server: if not, `422 {errors:{patientId}}` and the phone keeps the reading to try again.

### `PUT /api/appointments/{id}` (chv, nurse, doctor)

A visit, referral or teleconsult. A CHV creates a referral; only a nurse or doctor may move its `referralStatus` (`requested` to `accepted`, `declined` or `seen`), and the server fills in `handledBy` and `handledAt` from the token.

## Pulling what others saved: `GET /api/patients|vitals|appointments?updatedSince=<time>`

Returns what the caller may see that changed after that moment, oldest first, at most 500. A nurse sees every district; a CHV only their own. To get the next batch, ask again with the last record's `updatedAt` (`createdAt` for vitals).

## Audit trail

| | Who | What |
|---|---|---|
| `POST /api/audit-events` `{ events: [...] }` | any signed-in worker | The phone's recorded accesses go into the hash-chained, append-only `access_log`. Every sign-in, and every refused try by a registered worker (wrong, late or reused code, too many tries), is written to the same log by the server itself, as entity `sign-in`; numbers that are not registered workers are not (they would let anyone fill the log). The actor and role come from the token. An event id already stored is ignored. Up to 500 per call. `204`. |
| `GET /api/audit-events?patientId=&limit=` | admin (auditor) | The log, newest first (default 200, at most 1000). |
| `GET /api/audit-events/verify` | admin (auditor) | Recomputes the hash chain: `{ intact, entriesChecked, firstBadId }`. |

## Files: `POST /api/media/{id}/upload-url` (chv, nurse, doctor)

Body `{ kind, mime, size, sha256, patientId, ownerEntity, ownerId }`. The answer is `{ url, headers, expiresAt, key }`: a link valid for **5 minutes** that the phone `PUT`s the file to, straight to the private S3 bucket (the file never passes through the API). The phone must send the `headers` exactly. Limits: at most **2 MB**; only audio, `image/png` and `image/jpeg`; recordings must be audio and signatures or photos images; the patient must be one the caller may write to. Asking again for the same file gives a fresh link; a different file under the same id is `409`. Without `MEDIA_BUCKET` set (a laptop) the answer is `503 {error:"media_unavailable"}`.

The bucket allows the `PUT` from the production and staging sites only (a CORS rule in `deploy/terraform/storage.tf`), and encrypts every upload with our own KMS key through the bucket's default, so the link itself names no encryption setting.

## District summary: `GET /api/surveillance/weekly` (district_officer, admin)

Rows `{ district, week, patients_seen, patients_high_bp, patients_fever, suppressed }`, one per district and week. When fewer than 5 patients were seen, every count is `null` and `suppressed` is true, so a small group cannot be identified. Counts only, never a patient.
