# ADR 0001: Frontend stack, offline storage and branch flow

- **Status:** Proposed (to confirm Tue 27 Oct)
- **Date:** 2026-10-26

## Context
CHVs work in rural Ghana on cheap Android phones and paid 2G/3G data, often for days without signal. The app must install from a web address, open offline and keep every record until it reaches the server. We have one week. The team built the same kind of app for Prosit 1 (AgroConnect).

## Decision
- **Reuse the AgroConnect frontend stack:** React 19, TypeScript, Vite, vite-plugin-pwa (Workbox), Dexie (IndexedDB), i18next, react-hook-form + zod, shadcn/ui on Base UI, Tailwind v4, system font. It has already been tested on slow networks and the team knows it.
- **Offline first:** every save goes to the phone's database and the outbox in one transaction. Sync sends the outbox oldest first as `PUT /api/<entity>/<id>`, so a retry after a lost reply does no harm. A 409 is kept for review instead of overwritten.
- **Patient data never goes in the service worker cache.** The service worker caches only the app's code and icons, and `/api` always goes to the network.
- **Five languages from the brief** (English, Twi, Ewe, Dagbani, Hausa). Only English is enabled until native speakers translate.
- **Branch flow:** `feature/*` → `development` → `main`, with one required check, **CI passed** (branch name, pre-commit, gitleaks, lint, tests with a 70% coverage gate, build, Docker build). This is lighter than AgroConnect, with no staging branch, because the project lasts one week.

## Alternatives considered
- **A native Android app:** an app store, a second codebase and a bigger download.
- **Keeping AgroConnect's staging branch and autoscaling:** more safety than a one-week demo needs, and more cost.
- **localStorage for records:** too small, synchronous, and has no transactions.

## Consequences
- Initial JavaScript is about 92 KB gzipped, and every screen opens offline after the first visit.
- Records on the phone are not yet encrypted at rest. The device's own lock protects them; encrypting IndexedDB is documented future work (Germain's security notes).
- The demo sign-in is replaced once the API's `/auth/login` exists.
