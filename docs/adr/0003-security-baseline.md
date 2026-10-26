# ADR 0003: Security baseline on AWS (Lab 6)

- **Status:** Proposed (applied by the DevOps lead)
- **Date:** 2026-10-26

## Context
Health data is more sensitive than AgroConnect's farm records. Lab 6 asks for least-privilege IAM with allow and deny evidence, verified encryption at rest and in transit, a FHIR-aligned schema and audit logging. The infrastructure part (IAM, encryption, TLS) is the DevOps lead's. The schema and audit log belong to the database, backend and security owners.

## Decision
- **Separate from AgroConnect** in the same team account: own VPC, Terraform state and `healthconnect-*` names. The Week 5 network pattern is copied: public, app and db subnets, fck-nat, private RDS.
- **Least privilege:** the app server runs as `healthconnect-app` through an instance profile. It can read one secret, use one KMS key only via Secrets Manager or S3, read and write one bucket, pull HealthConnect images, and write its own log group. It has no `rds:*`, no IAM and no other buckets or secrets. IMDSv2 is required. There's no SSH; access is through Session Manager.
- **At rest:** one customer-managed KMS key (with rotation) encrypts RDS storage, the RDS master password in Secrets Manager, the S3 media bucket (SSE-KMS) and the server's disk.
- **In transit:** CloudFront's HTTPS is the only way in. `http://` gets a 301 to `https://`, the API over `http://` gets a 403, and the load balancer accepts only CloudFront's addresses plus a secret header. The database refuses non-TLS connections (`rds.force_ssl = 1`), and the media bucket refuses non-HTTPS requests.
- **Credentials:** RDS manages the master password in Secrets Manager, so it never appears in code, Terraform state or the server's files, and rotation is one API call.

## Alternatives considered
- **Aurora Serverless with the Data API** (the lab template's `rds-data`): matches the template literally, but costs more, and its availability in af-south-1 is uncertain.
- **An ACM certificate on the load balancer:** needs a domain we don't have this week.
- **AWS-managed keys:** simpler, but key use couldn't be restricted per role.

## Consequences
- The proof is scripted: `deploy/verify/`.
- Known gaps: CloudFront → load balancer is plain HTTP inside AWS's network, CloudFront's default certificate still allows TLS 1.0, and there's no Multi-AZ. See `deploy/README.md`.
