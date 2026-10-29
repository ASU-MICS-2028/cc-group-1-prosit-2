# Deploy and operate HealthConnect

Everything runs in the team AWS account (`agroconnect` profile, 659500703236), region **af-south-1** (Cape Town, the closest region to Ghana). Decisions, research and costs are in [ADR 0005](../docs/adr/0005-hosting-delivery-and-operations.md). The security baseline is in [ADR 0003](../docs/adr/0003-security-baseline.md).

```
                      ┌── production  https://d2opah5iezm17k.cloudfront.net   (from main, approved)
phone ──HTTPS──> CloudFront
                      └── staging     https://<id>.cloudfront.net             (from development)
                 http:// → 301, /api over http → 403
        │ VPC origin (private, no public load balancer)
        ▼
internal load balancer ── X-HC-Env: staging? ──> :8081  staging  ┐ one Graviton server (t4g.small),
                       └─ otherwise ──────────> :80    production┘ private subnet, Docker Compose,
                                                                    SSM only (no SSH)
        │ TLS (verify-full)                     │ HTTPS, KMS
        ▼                                       ▼
RDS PostgreSQL 17 (db.t4g.micro, private,   S3 media bucket (private, versioned,
encrypted, PITR 7 days)                     TLS only)
  databases: healthconnect, healthconnect_staging
```

| | Where |
|---|---|
| Infrastructure | `deploy/terraform` (state in S3 `healthconnect-tfstate-659500703236`, locked) |
| What runs on the server | `deploy/compose.yml`, released by `deploy/remote-deploy.sh` |
| Pipeline | `.github/workflows/deploy.yml` (release), `.github/workflows/ci.yml` (checks) |
| Dashboard and alarms | CloudWatch dashboard **healthconnect**; alerts go to the emails in `terraform.tfvars` |
| Logs | CloudWatch Logs `/healthconnect/app`, one stream per container |
| Architecture diagram | `deploy/architecture.drawio`: open in [diagrams.net](https://app.diagrams.net) (File → Open), edit, export PNG/PDF. Regenerate with `python3 deploy/diagram/generate.py deploy/architecture.drawio` |

## Day to day: deploying

You never deploy by hand:

1. **Merge a PR into `development`.** Staging updates on its own in about 4 minutes. Open the staging URL to check it.
2. **Open a PR `development` → `main` and merge it.** The Deploy run waits for approval. Approve it in Actions → the run → *Review deployments*, and production updates.
3. Every run builds arm64 images tagged with the commit SHA and blocks critical vulnerabilities. It releases through SSM, waits for health, **rolls back by itself** if the new version isn't healthy, and finishes with a smoke test through CloudFront.

**Roll back by hand:** Actions → **Deploy** → *Run workflow* → choose the environment and paste an older commit SHA into *frontend_tag* (and *backend_tag*). Each run's summary lists the SHAs.

**The API:** the pipeline starts deploying the backend the moment `backend/Dockerfile` exists. The container must listen on 8080 and answer `GET /health`. It gets `DB_HOST`, `DB_NAME`, `DB_SECRET_ARN`, `DB_SSL_CA`, `MEDIA_BUCKET` and `AWS_REGION` as environment variables, and reads the database password from Secrets Manager with the server's role. Until then `/api/*` answers `503 {"error":"api_unavailable"}`, and the app keeps working offline.

## Changing the infrastructure

```bash
export AWS_PROFILE=agroconnect
cd deploy/terraform
terraform init
terraform plan -out=tfplan       # read it: what is replaced or destroyed?
terraform apply tfplan
```

First-time setup only: `terraform -chdir=bootstrap init && terraform -chdir=bootstrap apply` creates the state bucket, then `cp terraform.tfvars.example terraform.tfvars` and set `budget_emails`.

After an apply that creates or changes the URLs, tell the pipeline where staging is:

```bash
gh variable set APP_URL --env staging    --body "$(terraform output -raw staging_url)"
gh variable set APP_URL --env production --body "$(terraform output -raw app_url)"
```

**Alarm emails:** AWS sends one "Confirm subscription" email per address and region (Cape Town, plus N. Virginia for the CloudFront alarm). Click both, or no alerts arrive.

## Costs

About **$2.25 a day** while running (ADR 0005 §6). A $20 monthly budget alerts at 50/80/100% on HealthConnect-tagged costs.

| To save money | Set in `terraform.tfvars`, then plan + apply |
|---|---|
| Stop the server and database every night, 23:00–07:00 Ghana time | `nightly_stop = true` |
| Stop the server between demos (the database keeps running) | `app_running = false` |

Turn these on once the demo is recorded. The deploy refuses to run while the server is stopped. The app on phones keeps working offline either way.

**Free web firewall (optional, 2 minutes):** CloudFront → each distribution → *Pricing plan* → choose **Free**. It adds AWS WAF (common-attack rules) and DDoS protection at $0, up to 1M requests and 100 GB a month. Terraform can't select a plan yet, which is why this is a console step.

## Backups and recovery

| What | How | Recovery |
|---|---|---|
| Database | Daily backup (01:00 GMT) + point-in-time recovery for 7 days; deletion protection; final snapshot on delete | Any second of the last 7 days |
| Patient media | S3 versioning (old versions kept 30 days) | Restore a previous version of the object |
| The phones | Every record stays in the phone's IndexedDB until the server confirms it | A server outage delays sync; no data is lost |
| Infrastructure | All in Terraform; state versioned in S3 | `terraform apply` rebuilds it |

**Rehearse a restore** (about 20 minutes, a few cents):

```bash
AWS_PROFILE=agroconnect bash deploy/ops/restore-drill.sh
```

It restores the database to 5 minutes ago as a private copy, prints how long that took, and deletes the copy.

**Real restore:** restore the same way to a new identifier. Point the server at it by editing `DB_HOST` in `/opt/healthconnect/<env>/settings.env` through Session Manager, re-run the Deploy workflow for that environment, then rename the instances in RDS. **RPO 5 minutes, RTO about 30 minutes.**

## When something is wrong

| Symptom | Look at |
|---|---|
| Alarm email "production-down" | Dashboard → Healthy servers. Then Actions → last Deploy run. Then logs (below) |
| Server or OS failure | Already handled: AWS recovers or reboots the server by itself; you get an email |
| Deploy failed | The run's "Release on the server" step prints the server's output; it rolled back if it could |
| Errors in the app | CloudWatch Logs → `/healthconnect/app` → the `healthconnect-production-frontend-1` / `-backend-1` streams |

**On the server**, without SSH:

```bash
ID=$(terraform -chdir=deploy/terraform output -raw app_instance_id)
aws ssm start-session --target "$ID" --region af-south-1     # needs the Session Manager plugin
sudo docker ps
sudo docker compose -p healthconnect-production -f /opt/healthconnect/production/compose.yml \
  --env-file /opt/healthconnect/production/settings.env --env-file /opt/healthconnect/production/release.env logs --tail 100
```

## Load testing

```bash
k6 run -e URL="$(terraform -chdir=deploy/terraform output -raw staging_url)" deploy/ops/load-test.js
```

It ramps to 200 virtual users. It fails if more than 1% of requests fail or p95 is over 1.5 s. Run it against staging, not production.

## Lab 6 evidence

| Part | What | How |
|---|---|---|
| A | Least-privilege policy document | `terraform -chdir=deploy/terraform output -raw app_policy_json \| jq` (source: `terraform/iam.tf`) |
| A | Allowed and denied actions | `verify/verify_least_privilege.sh`, run **on the server as its role** (Session Manager, above) |
| B | RDS and S3 encryption at rest, HTTP → HTTPS | `AWS_PROFILE=agroconnect bash deploy/verify/verify_encryption.sh` |

### Why not the lab's `rds-data` template
The template's `rds-data:ExecuteStatement` exists only for **Aurora** with the Data API. We use standard RDS PostgreSQL (cheaper, and what the API connects to with a normal driver). So the same idea is expressed differently: read **one** secret, use **one** KMS key only through Secrets Manager and S3, and touch **one** bucket. There is no `rds:*` at all, which is why deleting the database is denied.

## Known gaps (for the report)
- The CloudFront → load balancer leg is HTTP on AWS's private network (VPC origin). TLS on that leg, and TLS 1.2 as the minimum at the edge, need a custom domain with an ACM certificate.
- One server and a single-AZ database: no promise of 99.7%. ADR 0005 §7 sets out the production path (Auto Scaling across two zones, RDS Multi-AZ).
- Production and staging share one server and one RDS instance: a runaway staging test can slow production. That's acceptable for a course demo and is mitigated by container memory limits.
