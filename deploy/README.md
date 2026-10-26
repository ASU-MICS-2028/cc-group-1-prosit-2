# Deploy and Lab 6 security evidence

Infrastructure for HealthConnect in the team AWS account (`agroconnect` profile, 659500703236), region **af-south-1**. It is fully separate from AgroConnect: its own VPC (10.30.0.0/16), its own Terraform state and `healthconnect-*` names.

```
phone ──HTTPS──> CloudFront ──HTTP + secret header──> load balancer ──> app server (private) ──TLS──> RDS PostgreSQL (private)
                 http:// → 301                         only CloudFront     role: healthconnect-app        encrypted (KMS), force_ssl
                 /api over http → 403                  can reach it        no keys, IMDSv2                    password in Secrets Manager
                                                                            └──> S3 media bucket (KMS, private, HTTPS only)
```

## Apply (DevOps lead)

```bash
export AWS_PROFILE=agroconnect
terraform -chdir=deploy/terraform/bootstrap init && terraform -chdir=deploy/terraform/bootstrap apply   # once: state bucket
cp deploy/terraform/terraform.tfvars.example deploy/terraform/terraform.tfvars                            # set budget_emails
terraform -chdir=deploy/terraform init
terraform -chdir=deploy/terraform plan -out=tfplan
terraform -chdir=deploy/terraform apply tfplan
```

Cost while running is roughly **$1.50–2 a day**: t3.small server, t4g.nano NAT, the load balancer, db.t3.micro, the KMS key, plus near-zero CloudFront. A $20/month budget alerts on HealthConnect-tagged costs. Between demos, set `app_running = false` to stop the server.

## Lab 6 evidence

| Part | What | How |
|---|---|---|
| A | Least-privilege policy document | `terraform -chdir=deploy/terraform output -raw app_policy_json \| jq` (source: `terraform/iam.tf`) |
| A | Allowed + denied actions | `verify/verify_least_privilege.sh`, run **on the server as its role** (below) |
| B | RDS + S3 encryption at rest, HTTP → HTTPS | `AWS_PROFILE=agroconnect bash deploy/verify/verify_encryption.sh` |
| B | Console screenshots | RDS → healthconnect-db → Configuration → *Encryption: Enabled*; S3 → bucket → Properties → *Default encryption: SSE-KMS* |

Run Part A on the server without SSH:

```bash
ID=$(terraform -chdir=deploy/terraform output -raw app_instance_id)
aws ssm start-session --target "$ID" --region af-south-1      # needs the Session Manager plugin
# then on the server, paste deploy/verify/verify_least_privilege.sh into a file and: sudo bash it
```

### Why not the lab's `rds-data` template
The template's `rds-data:ExecuteStatement` only exists for **Aurora** with the Data API. We use standard RDS PostgreSQL (cheaper, and what the API connects to with a normal driver), so the same idea is expressed as: read **one** secret, use **one** KMS key only through Secrets Manager and S3, and touch **one** bucket. There is no `rds:*` at all, which is why deleting the database is denied.

### Known gaps (for the report)
- CloudFront → load balancer is HTTP inside AWS's network. End-to-end TLS needs a certificate on the load balancer, which needs a domain.
- CloudFront's default certificate allows TLS 1.0 for old clients. A custom domain with an ACM certificate lets us set TLS 1.2 as the minimum.
- One server, one database, no Multi-AZ: fine for a one-week demo, not for 99.7% uptime.
