# Sign-in and access

How a health worker signs in, what the token means, and who may call what. The app side is [ADR 0004](adr/0004-sign-in-referrals-and-summary.md); the code is `backend/Services/AuthService`.

## The flow

1. The worker types their phone number. The app calls `POST /api/auth/code`. The API texts a 6-digit code.
2. The worker types the code. The app calls `POST /api/auth/verify` and gets a **token** and who they are.
3. The app sends `Authorization: Bearer <token>` on every call. The token lasts **7 days**, so a CHV can work offline between signals.

## The endpoints

| | Send | Success | Refusals |
|---|---|---|---|
| `POST /api/auth/code` | `{ "phone": "020 000 0101" }` (any usual way of typing a Ghana number) | `204` no body | `404 {error:"unknown_number"}` not a registered worker · `422 {errors:{phone}}` not a Ghana number · `429 {error:"too_many_requests"}` asked too soon · `503 {error:"sms_unavailable"}` |
| `POST /api/auth/verify` | `{ "phone": "...", "code": "123456" }` | `200 { token, expiresAt, user:{ id, name, role, district } }` | `401 {error:"wrong_code"}` or `"code_expired"` · `422 {errors:{phone,code}}` · `429 {error:"too_many_attempts"}` |
| `GET /api/auth/me` | the token | `200 { id, name, role, district }` | `401` no or bad token, or the worker was switched off |

`role` is one of `chv`, `nurse`, `doctor`, `district_officer`, `admin` (the auditor).

## The rules

- A code **expires after 10 minutes**, allows **5 wrong tries**, and **works once**. Only the newest code counts.
- A number gets **one code per 30 seconds and 6 per hour**. One address gets 30 requests a minute on these endpoints.
- Codes are kept only as an HMAC hash (never the code itself).
- An unknown number and a wrong code both give `401` when verifying, so the check reveals nothing. (`/auth/code` says `404`, because the app's contract asks for it.)
- **A database problem never signs people out.** If the token-signing key cannot be loaded the API answers `503`, not `401`, because a `401` makes the app clear the session.

## Who may call what

The token carries the worker's id, role and district. They come from the token, never from a request body. Each endpoint names one of these rules; they match the pages the app shows each role.

| Rule | chv | nurse | doctor | district_officer | admin |
|---|---|---|---|---|---|
| `Clinical` (patients, vitals, visits) | yes | yes | yes | no | no |
| `Clinician` (accept or decline referrals) | no | yes | yes | no | no |
| `Summary` (district counts) | no | no | no | yes | yes |
| `Auditor` (activity log) | no | no | no | no | yes |

Not signed in is `401`; signed in but not allowed is `403`. The database enforces the same split again for patient data ([docs/database](database/README.md)).

## The token signing key

Tokens are signed with HMAC-SHA256. The key is **32 random bytes made by the API the first time it starts and kept in the `auth_keys` table**, so every container and every restart uses the same one and nothing new has to be set up in AWS. Only the API's own database login can read that table. To use a key from AWS Secrets Manager instead, set `Auth__SigningKey` (at least 32 characters) in the container's environment; it wins over the database key. Changing the key signs everyone out.

## Demo workers and the demo code

No SMS provider is connected yet, so the MVP has **five synthetic demo workers**, the same people and ids as the app's offline demo mode (`020 000 0101` to `0105`: chv-042, nurse-17, dr-eshun, ghs-officer-07, auditor-02). With `Demo:Enabled` on (the default in `appsettings.json`), the fixed code **123456** signs in those five accounts, and **only** those; a real worker never accepts it. The API logs a warning at start-up while it is on, and every demo sign-in is logged.

### Sample data

While `Demo:Enabled` is on, the API also fills an empty database with **invented** patients, readings, visits and referrals when it starts, so every screen has something to show: 33 patients in four districts (names end in `-Demo`), eight weeks of readings, a few upcoming, overdue, done and missed visits for the demo CHV, and referrals the demo nurse has accepted, seen and declined. Kintampo North's fever count rises towards the present, to show the outbreak signal; Sawla-Tuna-Kalba is too small to be shown. It goes through the same code as the API (every rule is checked), only adds what is missing, adds the new week's readings each week, and never overwrites. Switch it off alone with `Demo__SampleData=false`; `Demo__Enabled=false` switches off both. The records stay in the database until someone removes them, so remove them before real use (`name LIKE '%-Demo'`).

**Before real people use the system:** set `Demo__Enabled=false`, remove or deactivate the demo rows in `users`, add the real workers, and connect an SMS provider behind `ISmsSender`.

## Known limits

- The text is only logged (the number masked); on a laptop `Sms:LogMessages` also logs the text, so a developer can read the code. Never turn that on for a server.
- Each sign-in, and each refused try by a registered worker, is written to the audit log (`access_log`) as entity `sign-in`, and is also a structured log line with the worker's id. Tries with a number that is not a registered worker are only in the server's own log.
- A token is checked against the database only on `/auth/me`. A worker switched off keeps a valid token until it expires, except where an endpoint looks the worker up: every data endpoint ([api.md](api.md)) does, so they lose access at once.

## Try it

```bash
curl -s -X POST localhost:8000/api/auth/verify -H 'Content-Type: application/json' \
  -d '{"phone":"020 000 0101","code":"123456"}'
```

To use the real sign-in in the app, build it with `VITE_AUTH_MODE=api`; `npm run dev` already forwards `/api` to `localhost:8000`.
