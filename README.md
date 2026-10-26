# HealthConnect Ghana

An offline-first Progressive Web App for Community Health Volunteers: register patients with consent, record vitals, book visits and referrals, and sync when there is network. District officers see an anonymised summary.

Cloud Computing Prosit 2, group 1. One-week MVP, deadline **Tue 3 Nov 2026, 5 PM GMT**. Synthetic data only. Hospital, payment, insurance and national integrations are mock flows or documented future work. Nothing here is clinically validated or a production HIPAA system.

| Area | Owner | Folder |
|---|---|---|
| Frontend / UI / DevOps | Mubarak Tijani | `frontend/`, `.github/`, `deploy/` |
| Backend / APIs | Bernard Marfo Adjei | `backend/` |
| Database / sync / analytics | Liza Aikins | `backend/` (schema, migrations, seed) |
| Security / community / docs | Germain Emil Allotey-Pappoe | `docs/` |

## Run the frontend

Node 24 (`frontend/.nvmrc`).

```bash
cd frontend
npm ci
npm run dev        # http://localhost:5173, /api forwarded to http://localhost:8000
npm run test:ci    # unit tests + coverage (CI needs 70%)
npm run build && npm run preview   # the installable, offline build
```

To try it, sign in with a demo role. Records save on the device. Turn the network off in DevTools to see the offline banner and the sync queue.

## Branches

`feature/*` → `development` → `main`. Open a PR into `development`; it merges when **CI passed** is green. Only the DevOps lead merges `development` → `main`, and `main` is what gets deployed. See [CONTRIBUTING.md](CONTRIBUTING.md).

## Docs

- [What the frontend needs (API and data contract proposal)](docs/frontend-needs.md)
- [Decisions](docs/adr/)
