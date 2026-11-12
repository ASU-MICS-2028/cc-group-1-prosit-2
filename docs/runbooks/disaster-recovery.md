# Disaster recovery runbook

What to do when something breaks badly, in the order you would try it. Every command runs from a
laptop with the team's AWS profile (`export AWS_PROFILE=… AWS_REGION=af-south-1`) unless it says
otherwise. Synthetic data only: the targets below are for this MVP, not a hospital's.

| What happened | Recovery point (data lost) | Recovery time | Section |
|---|---|---|---|
| A bad release | none | minutes, automatic | 1 |
| The app server is broken or deleted | none (it keeps no data) | ~5 min auto-recover; ~20 min rebuild | 2 |
| Data deleted or corrupted in the database | back to any second of the last 7 days (≈5 min behind) | ~30 min, measured in the drill below | 3 |
| Photos or recordings deleted | none (30 days of versions) | minutes | 4 |
| Cape Town (af-south-1) unavailable, or the account's resources lost | ≈5 min (replicated backups) | a few hours | 5 |
| Phones offline / server down for hours | none: records wait in each phone's outbox | n/a | — |

Phones keep working through all of this: every record is saved on the phone first and sent when the
server answers again.

## 1. A bad release

Nothing to do in most cases: `deploy.yml` health-checks the new containers and rolls back to the
previous release on its own. To go back to an older release by hand, run the **Deploy** workflow
with `frontend_tag` / `backend_tag` set to an earlier commit SHA (the images stay in ECR).

## 2. The app server

- **Hardware or system failure:** the CloudWatch alarm recovers the instance automatically (same
  instance, same disk, same private IP).
- **Deleted or unrecoverable:** `cd deploy/terraform && terraform apply` creates a new one (the
  first boot installs Docker and fetches settings), then run the **Deploy** workflow for
  production and staging.

## 3. The database: point-in-time restore

1. Pick the moment just before the damage (UTC). `aws rds describe-db-instances
   --db-instance-identifier healthconnect-db --query 'DBInstances[0].LatestRestorableTime'` shows
   how recent it can be.
2. Restore it as a **new** instance, private, in the same subnets and security group:
   ```bash
   aws rds restore-db-instance-to-point-in-time --source-db-instance-identifier healthconnect-db \
     --target-db-instance-identifier healthconnect-db-restored --restore-time 2026-11-12T06:30:00Z \
     --db-subnet-group-name healthconnect-private --vpc-security-group-ids <db security group> \
     --db-instance-class db.t4g.micro --no-publicly-accessible
   ```
3. When it is `available`, check it (Session Manager to the app server, `psql` to the new
   endpoint), then point the app at it: rename the old instance out of the way and the new one to
   `healthconnect-db` (same endpoint name, so nothing else changes), or set the new endpoint in
   Terraform and apply. Redeploy both environments.
4. Keep the damaged instance until the restored one is confirmed, then delete it.

**Drill (12 Nov 2026):** production restored to its latest restorable time as
`healthconnect-dr-drill`, private, then deleted. Time to `available`: see the result recorded below.

## 4. Photos and recordings (S3)

The media bucket keeps old versions for 30 days. Restore a deleted object by deleting its
"delete marker", or copy an older version back over it (`aws s3api list-object-versions`).

## 5. Losing Cape Town: restore in Ireland

Every automated backup of the database is copied to **eu-west-1 (Ireland)** and re-encrypted there
with `alias/healthconnect-dr-backups` (`deploy/terraform/dr.tf`). Any second of the last 7 days can
be restored there:

```bash
aws rds describe-db-instance-automated-backups --region eu-west-1   # find the replicated backup ARN
aws rds restore-db-instance-to-point-in-time --region eu-west-1 \
  --source-db-instance-automated-backups-arn <arn> --use-latest-restorable-time \
  --target-db-instance-identifier healthconnect-db --db-instance-class db.t4g.micro --no-publicly-accessible
```

Then rebuild the rest in Ireland from Terraform: set `region = "eu-west-1"` (and a new state key),
`terraform apply`, point it at the restored database, run the **Deploy** workflow. The phones need
nothing: their CloudFront address is replaced by the new one in the app's configuration.

## What is not covered yet

- **One Availability Zone:** the database is single-AZ to save cost; Multi-AZ (`multi_az = true`)
  roughly doubles its price and fails over in about a minute.
- **The TURN relay and the API are single instances**: losing them stops calls or saves until they
  are back, never data.
- The Ireland rebuild (section 5) is written down but has not been rehearsed end to end.
