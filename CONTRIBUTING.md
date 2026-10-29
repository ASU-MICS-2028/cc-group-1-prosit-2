# Contributing

## Branches and PRs

1. Branch from `development`: `<type>/<short-name>`, with type one of `feature`, `fix`, `hotfix`, `devops`, `docs`, `refactor`, `test`, `chore`. Example: `feature/vitals-api`.
2. Open a PR into `development`. It needs the **CI passed** check; PRs that touch `.github/`, `deploy/` or Dockerfiles also need the DevOps lead's review.
3. `development` → `main` is a PR the DevOps lead merges after integration testing. `main` deploys.

Never push straight to `development` or `main`. The pre-commit hook and branch protection both stop it.

## Before you push

```bash
pip install pre-commit && pre-commit install   # once
```

The hooks block secrets, private keys, large files and broken JSON/YAML, and run eslint and prettier on frontend files.

## Tests are required

Every PR adds or updates unit tests, and CI fails below **70%** line coverage. Frontend: `npm run test:ci`.

## Health data rules

- **Synthetic records only.** No real names, phone numbers, Ghana Card/NHIS numbers or photos, in code, seeds, tests or screenshots.
- No secrets in the repo. `.env.example` is the template; real values go in GitHub secrets or the server.
