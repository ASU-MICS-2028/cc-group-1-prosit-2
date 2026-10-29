# ADR 0004: Sign-in, referral status and the district summary

- **Status:** Accepted (frontend); server side to match
- **Date:** 2026-10-27

## Context
Three screens were waiting on decisions owned by other people: how health workers sign in (backend and security), how a referral moves between a CHV and a clinician (backend), and what the district officer sees (data). With the feature freeze on Mon 2 Nov, the frontend made the calls, built them behind clean API seams, and records them here so the server side can match.

## Decision
**1. Sign-in: phone number + 6-digit SMS code** (the Figma "Completa tu Registro" and "Validar Registro" screens).
- `POST /api/auth/code {phone}` sends the code; `POST /api/auth/verify {phone, code}` returns `{token, expiresAt, user}`.
- Only numbers registered as health workers get a code (404 otherwise). The server should expire codes after 10 minutes, allow 5 attempts, and limit sends (429). The app waits 60 s before offering "Send again".
- The session lasts 7 days on the phone, so a CHV can work offline between signals.
- No passwords. CHVs have phones, often shared, and no email. A code needs nothing remembered and works on any phone with SMS. AgroConnect used the same approach.
- **Demo mode** is the default until the endpoints exist (`VITE_AUTH_MODE=api` switches it off). It accepts four synthetic workers (`020 000 0101`–`0104`) with code `123456`, and the screen says so.

**2. Referral status: requested → accepted or declined → seen.**
- Created as `requested` by the CHV, usually from the "Refer today" screen. A nurse or doctor accepts or declines it, then marks it seen. Each change records `handledBy` and `handledAt`.
- Clinicians get a Referrals tab (instead of Add): emergencies first, then urgent, then by time. The CHV sees the status on the patient record.
- Changes are saved on the phone and synced through the outbox like every record, so it works offline.
- Maps to FHIR `ServiceRequest.status` (active / revoked / completed) when the record is exported.

**3. District summary: anonymised weekly counts from `surveillance_weekly`.**
- `GET /api/surveillance/weekly` returns the view's rows. Officers never receive patient rows; cells under 5 patients come back null and show as "<5" (Act 843 s.24(3)).
- Week and Month tabs (Figma "Día / Semana / Mes"), with headline numbers and bar charts of patients seen and patients with fever.
- **Early warning:** if last week's fever count is at least double the average of the 4 weeks before, and at least 4, the page raises "Fever rising in …". This is a first step towards the brief's outbreak detection, not a validated epidemiological method.
- Demo data, clearly labelled, until the API is deployed.

**4. Welcome first.** A brand-new phone opens on the Welcome screen to choose a language; sign-in comes after.

## Alternatives considered
- **Password or PIN sign-in:** forgotten often, and shared phones make PINs weak. A device PIN as a second factor can be added later.
- **Referral status changed only online:** simpler, but clinicians at rural facilities lose signal too.
- **Officers reading raw tables:** never; Postgres refuses it (`db/rbac.sql`).

## Consequences
- The frontend works end to end today in demo mode; switching to the real API is one setting.
- Bernard implements `/auth/code`, `/auth/verify` and `/surveillance/weekly`, plus accepting `referralStatus` on `PUT /appointments/{id}` (`docs/frontend-needs.md`).
- Germain's security review covers the code rules (expiry, attempts, rate limit) and logging every sign-in in `access_log`.
