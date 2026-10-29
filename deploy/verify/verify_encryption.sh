#!/usr/bin/env bash
# Lab 6 Part B evidence: run from a laptop with the team's admin profile, after `terraform apply`.
#
#   AWS_PROFILE=agroconnect bash deploy/verify/verify_encryption.sh
#
# Checks encryption at rest (RDS, S3) from AWS itself, not from our Terraform, and that plain
# HTTP is redirected or refused.
set -uo pipefail
cd "$(dirname "$0")/../terraform" || exit 1
export AWS_REGION=af-south-1

out() { terraform output -raw "$1"; }
APP_URL=$(out app_url)
HOST=${APP_URL#https://}
ALB=$(out load_balancer_dns)
BUCKET=$(out media_bucket)
DB=$(out db_instance_id)

pass() { echo "PASS: $*"; }
fail() { echo "FAIL: $*"; }

echo "=== AT REST 1: the database's storage is encrypted with our KMS key ==="
aws rds describe-db-instances --db-instance-identifier "$DB" \
  --query 'DBInstances[0].{StorageEncrypted:StorageEncrypted,KmsKeyId:KmsKeyId,PubliclyAccessible:PubliclyAccessible,MasterUserSecret:MasterUserSecret.SecretArn}' \
  --output table
[ "$(aws rds describe-db-instances --db-instance-identifier "$DB" --query 'DBInstances[0].StorageEncrypted')" = true ] \
  && pass "RDS StorageEncrypted = true" || fail "RDS not encrypted"

echo
echo "=== AT REST 2: the database refuses connections without TLS (rds.force_ssl) ==="
aws rds describe-db-parameters --db-parameter-group-name healthconnect-postgres17 \
  --query "Parameters[?ParameterName=='rds.force_ssl'].[ParameterName,ParameterValue]" --output text

echo
echo "=== AT REST 3: the media bucket is encrypted and not public ==="
aws s3api get-bucket-encryption --bucket "$BUCKET"
aws s3api get-public-access-block --bucket "$BUCKET"
aws s3api get-bucket-policy-status --bucket "$BUCKET" 2>/dev/null || echo "(no policy status: bucket is not public)"

echo
echo "=== IN TRANSIT 1: http:// is redirected to https:// ==="
code=$(curl -s -o /dev/null -w "%{http_code} -> %{redirect_url}" "http://$HOST/health")
curl -sv "http://$HOST/health" -o /dev/null 2>&1 | grep -E "^> GET|^< HTTP|^< [Ll]ocation"
echo "$code"
[[ $code == 301* ]] && pass "HTTP redirects to HTTPS" || fail "HTTP did not redirect"

echo
echo "=== IN TRANSIT 2: https:// has a valid certificate and /health answers 200 ==="
curl -sv "https://$HOST/health" 2>&1 | grep -E "SSL connection using|subject:|issuer:|SSL certificate verify|^< HTTP"
[ "$(curl -s -o /dev/null -w '%{http_code}' "https://$HOST/health")" = 200 ] && pass "HTTPS 200" || fail "HTTPS not 200"

echo
echo "=== IN TRANSIT 3: the API refuses plain HTTP outright (no redirect for patient data) ==="
code=$(curl -s -o /dev/null -w '%{http_code}' -X POST "http://$HOST/api/patients")
echo "POST http://$HOST/api/patients -> $code"
[ "$code" = 403 ] && pass "API over HTTP refused (403)" || fail "API over HTTP returned $code"

echo
echo "=== IN TRANSIT 4: the load balancer can't be used directly over HTTP ==="
code=$(curl -s -o /dev/null -w '%{http_code}' --max-time 8 "http://$ALB/health")
echo "http://$ALB/health -> ${code:-timeout}"
[[ $code == 000 || $code == 403 ]] && pass "direct load balancer access blocked" || fail "load balancer answered $code"
