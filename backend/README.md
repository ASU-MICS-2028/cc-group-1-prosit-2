# Backend

Owned by Bernard (API) and Liza (database, sync, analytics). The stack is agreed on Tue 27 Oct.

What the frontend expects is in [`../docs/frontend-needs.md`](../docs/frontend-needs.md). In short:

- JSON under `/api`, bearer tokens, `GET /health` returning 200.
- `PUT /api/{patients|vitals|appointments}/{id}` with ids made on the phone.
- Listen on port **8080** inside the container. nginx in the frontend image forwards `/api` and `/health` to the `backend` service on that port.
- Add a `Dockerfile` here and CI builds it automatically. Add the unit-test job to `.github/workflows/ci.yml` (or ask Tijani to).
