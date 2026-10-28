# ADR 0005: Hosting, delivery and operations

- **Status:** Accepted
- **Date:** 2026-10-28
- **Owner:** Mubarak Tijani (DevOps)

## Context
The workplan asks DevOps for Docker, a CI pipeline, a shared staging environment by Sun 1 Nov, and a live demo URL with repeatable deployment steps. The brief adds:
- 99.7% uptime;
- load balancing for concurrent consultations;
- secure backup;
- encryption and audit trails;
- 50,000 records and 1,000 concurrent users.

The constraints are one week, a $20/month budget on a student AWS account, and synthetic data only. We want the cheapest setup that is still secure and still demonstrates each requirement. Where a requirement can't be met at this budget, we document how it would be met at production scale.

Lab 6 (ADR 0003) already gave us:
- a private network;
- CloudFront HTTPS;
- an encrypted RDS PostgreSQL with secrets in Secrets Manager;
- a KMS-encrypted media bucket;
- ECR;
- a least-privilege server role;
- GitHub OIDC.

This ADR covers what runs on top of that.

## Decision

### 1. One Graviton server, two environments
- **Production** and **staging** run as two Docker Compose projects on the same t4g.small:
  - production on port 80, deployed from `main`, approved in GitHub;
  - staging on port 8081, deployed from `development` on every merge.
- Staging has its own database (`healthconnect_staging`) on the same RDS instance, and its own CloudFront URL that adds `X-HC-Env: staging`. The load balancer routes on that header.
- **Why:** a second server would double the compute bill for an environment used a few hours a week. Separate Compose projects give separate containers and networks, so the two never see each other.
- **Graviton:** t4g.small costs $0.0217/h against t3.small's $0.0271/h in af-south-1 (20% less), and db.t4g.micro costs $0.021/h against $0.024/h. Both prices come from the AWS Pricing API on 28 Oct 2026.
  - GitHub's arm64 runners are available to private repositories since Jan 2026, so images are built natively, with no slow emulation.

### 2. Private load balancer behind CloudFront VPC origins
- The load balancer is now **internal**. CloudFront reaches it through a VPC origin: a CloudFront-managed network interface inside our VPC, at no extra charge, and supported in af-south-1.
- This replaces the Lab 6 setup: a public load balancer protected by the CloudFront IP list plus a secret header.
- Nothing about the origin is reachable from the internet now, and we stop paying for the load balancer's public IPv4 addresses (about $0.24/day).
- **Why keep a load balancer at all:** the brief asks for load balancing, and the ALB gives health checks, connection draining, and a path to more servers (§7) without changing CloudFront.
  - The cheapest alternative is a VPC origin straight to the EC2 instance, which saves another $0.72/day. It's noted for a budget emergency.

### 3. Delivery pipeline (`.github/workflows/deploy.yml`)
1. **Build:** each image is built for arm64 and tagged with the commit SHA. ECR tags are immutable, so a SHA always means the same image.
2. **Scan:** Trivy blocks a release with a fixable CRITICAL vulnerability. ECR also scans on push.
3. **Release:** SSM Run Command runs `deploy/remote-deploy.sh` on the server: pull, `docker compose up`, then wait for `/health` (and `/api/health` once there is an API).
   - **If unhealthy, it rolls back** to the previous release automatically.
   - No SSH, no open port, and no AWS keys in GitHub (OIDC, a role scoped to our images and our server).
4. **Smoke test:** checks through CloudFront, as a user would reach it. It confirms `/health`, the app shell, the manifest, and that `http://` redirects to `https://`.
5. **Rollback by hand:** Actions → Deploy → Run workflow with an older SHA.

CI (`ci.yml`) also checks infrastructure on every PR that touches it:
- `terraform fmt` and `validate`;
- shellcheck on the scripts;
- a Trivy misconfiguration scan (fails on CRITICAL).

### 4. Monitoring and self-healing (`monitoring.tf`)
| Signal | Action |
|---|---|
| Host hardware failure (`StatusCheckFailed_System`) | EC2 **recover** onto new hardware (same disk and IP) + email |
| OS hang (`StatusCheckFailed_Instance`) | **reboot** + email |
| No healthy production server for 3 min | email (and when it's back) |
| More than 10 server errors in 5 min; p90 latency above 2 s | email |
| More than 5% 5xx at the CloudFront edge (what users see) | email |
| Database CPU above 80%, free storage under 2 GB, free memory under 64 MB | email |

- One CloudWatch dashboard shows traffic, errors, latency, healthy servers, CPU and database health.
- Container logs go to CloudWatch Logs (`/healthconnect/app`, kept 30 days) through Docker's awslogs driver, in non-blocking mode so a logging hiccup can't freeze the app.
- Cost: about 10 alarms × $0.10 plus one dashboard, under $2 a month.

### 5. Backups and recovery
- **Database:**
  - automated daily backups;
  - point-in-time recovery to any second of the last 7 days;
  - deletion protection;
  - a final snapshot if the database is ever deleted.
- **Media bucket:** S3 versioning lets a deleted or overwritten photo be restored. Old versions expire after 30 days.
- **Drill:** `deploy/ops/restore-drill.sh` restores the database to "5 minutes ago" as a private copy, times it, and deletes it.
- **Targets:**
  - **RPO 5 minutes** (point-in-time recovery);
  - **RTO about 30 minutes**: restore time plus re-pointing the app's `DB_HOST`. Measured by the drill.
- **Offline-first helps here:** phones keep every record in IndexedDB until the server confirms it. A server outage loses no patient data; it only delays sync.

### 6. Cost
| | Per day | Per month |
|---|---|---|
| Before: HealthConnect resources on 27 Oct (Cost Explorer; the account's $3.10 also included an unrelated EU server) | $2.69 | ~$82 |
| After this ADR: Graviton saves $0.20 and the private load balancer about $0.24 (estimate) | ~$2.25 | ~$68 |
| With the nightly stop on (`nightly_stop = true`, 23:00–07:00 GMT) | ~$1.90 | ~$58 |

- The monthly budget alerts at 50%, 80% and 100% of $20 on HealthConnect-tagged costs. The team should expect the forecast alert: the setup costs more than $20 for a full month, but stays far under it for the one-week course.
- The biggest single cost is the load balancer ($0.72/day), kept for the brief (§2).
- **For the course:** run until the demo is recorded, then set `nightly_stop = true` or `app_running = false`.

### 7. What 99.7% and 1,000 concurrent users would take (not built)
99.7% allows about 2 h 11 min of downtime a month. One server and one single-AZ database can't promise that: a zone outage or a database patch takes it down. At production scale:
- an **Auto Scaling group of 2+ servers across two zones** behind the same ALB (the AgroConnect pattern from Prosit 1);
- **RDS Multi-AZ** (about 2× the database cost) for automatic failover;
- a second NAT, or NAT Gateways;
- **Load testing** with `deploy/ops/load-test.js` (k6) before each release.
  - CloudFront absorbs static traffic, since the app shell and assets are cached at the edge, so 1,000 concurrent users mostly hit the API.

The Terraform is written to grow into this. The ALB and target groups stay the same; only the compute and database blocks change.

### 8. Not chosen
- **ECS Fargate / App Runner / EKS:** no servers to patch, but about 2–3× the compute cost for the same capacity, and App Runner isn't in af-south-1. Compose on one server is enough at this scale and easy to reason about.
- **AWS WAF web ACL:** about $5/month plus $1 per rule.
  - Instead: CloudFront's **flat-rate Free plan** (Nov 2025) includes AWS WAF and DDoS protection at $0 for up to 1M requests and 100 GB a month. Terraform can't select it yet (the aws-cdk issue linked below), so it is a one-time console step in `deploy/README.md`.
- **NAT Gateway:** about $43/month in af-south-1 before data charges. fck-nat on a t4g.nano costs $0.13/day.
- **A custom domain:** would allow TLS between CloudFront and the load balancer and TLS 1.2 as the minimum at the edge. It costs a domain, and the course doesn't need one. It stays a documented gap.

## Consequences
- The demo URL from Lab 6 stays the same (`moved` blocks keep the distribution). Staging gets a second `*.cloudfront.net` URL.
- The team gets a real promotion flow: merge to `development` → staging updates by itself → open `development` → `main` → approve → production.
- Bernard's API ships the moment `backend/Dockerfile` exists. Until then the app runs offline-first and `/api` answers `503 {"error":"api_unavailable"}`, which the sync retries.
- One server is still a single point of failure; §7 is the path out.

## References
- CloudFront VPC origins, including the supported regions: https://docs.aws.amazon.com/AmazonCloudFront/latest/DeveloperGuide/private-content-vpc-origins.html
- CloudFront flat-rate plans: https://docs.aws.amazon.com/AmazonCloudFront/latest/DeveloperGuide/flat-rate-pricing-plan.html and https://aws.amazon.com/cloudfront/pricing/
- Terraform/CDK support for flat-rate plans is still open: https://github.com/aws/aws-cdk/issues/37857
- arm64 GitHub-hosted runners for private repositories: https://github.blog/changelog/2026-01-29-arm64-standard-runners-are-now-available-in-private-repositories
- fck-nat: https://github.com/AndrewGuenther/fck-nat
