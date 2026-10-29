#!/usr/bin/env bash
# Lab 6 Part A evidence: run ON THE APP SERVER, as its own role (healthconnect-app).
#
#   aws ssm start-session --target <app_instance_id> --profile agroconnect --region af-south-1
#   sudo bash /opt/healthconnect/verify_least_privilege.sh   (or paste this file)
#
# Allowed actions should succeed; forbidden ones should fail with AccessDenied.
# The forbidden "delete the database" is safe to try: the role has no rds:* permission at all,
# and the database also has deletion protection.
set -uo pipefail
source /opt/healthconnect/settings.env
export AWS_REGION

pass() { echo "PASS: $*"; }
fail() { echo "FAIL: $*"; }
denied() { grep -qE "AccessDenied|not authorized|UnauthorizedOperation"; }

echo "=== Who am I? (should be the healthconnect-app role, not an admin user) ==="
aws sts get-caller-identity

echo
echo "=== ALLOWED 1: read the database password from Secrets Manager ==="
if secret=$(aws secretsmanager get-secret-value --secret-id "$DB_SECRET_ARN" --query SecretString --output text); then
  pass "got the secret (user: $(jq -r .username <<<"$secret"), password not shown)"
else
  fail "could not read the database secret"
fi

echo
echo "=== ALLOWED 2: query the database over verified TLS ==="
PGPASSWORD=$(jq -r .password <<<"$secret") psql \
  "host=$DB_HOST dbname=$DB_NAME user=$(jq -r .username <<<"$secret") sslmode=verify-full sslrootcert=$DB_SSL_CA" \
  -c "SELECT 1 AS allowed, ssl, version AS tls_version FROM pg_stat_ssl WHERE pid = pg_backend_pid();" \
  && pass "SELECT 1 over TLS" || fail "database query"

echo
echo "=== ALLOWED 3: write and read a (synthetic) file in the media bucket ==="
echo "synthetic test $(date -u +%FT%TZ)" > /tmp/lab6-test.txt
aws s3 cp /tmp/lab6-test.txt "s3://$MEDIA_BUCKET/lab6/test.txt" && aws s3 cp "s3://$MEDIA_BUCKET/lab6/test.txt" - \
  && pass "media bucket put/get" || fail "media bucket put/get"

echo
echo "=== FORBIDDEN 1: delete the database (should be AccessDenied) ==="
aws rds delete-db-instance --db-instance-identifier healthconnect-db --skip-final-snapshot 2>&1 | tee /tmp/out
denied </tmp/out && pass "delete database denied" || fail "delete database was NOT denied"

echo
echo "=== FORBIDDEN 2: list every bucket in the account (should be AccessDenied) ==="
aws s3api list-buckets 2>&1 | tee /tmp/out
denied </tmp/out && pass "list buckets denied" || fail "list buckets was NOT denied"

echo
echo "=== FORBIDDEN 3: list other secrets, e.g. AgroConnect's (should be AccessDenied) ==="
aws secretsmanager list-secrets 2>&1 | tee /tmp/out
denied </tmp/out && pass "list secrets denied" || fail "list secrets was NOT denied"

echo
echo "=== FORBIDDEN 4: create an IAM user (should be AccessDenied) ==="
aws iam create-user --user-name lab6-should-not-exist 2>&1 | tee /tmp/out
denied </tmp/out && pass "IAM denied" || fail "IAM was NOT denied"

echo
echo "=== FORBIDDEN 5: connect to the database WITHOUT TLS (rds.force_ssl should refuse) ==="
PGPASSWORD=$(jq -r .password <<<"$secret") psql \
  "host=$DB_HOST dbname=$DB_NAME user=$(jq -r .username <<<"$secret") sslmode=disable" -c "SELECT 1" 2>&1 | tee /tmp/out
grep -qE "no pg_hba.conf entry|SSL off|encryption" /tmp/out && pass "plaintext database connection refused" \
  || fail "plaintext database connection was NOT refused"

rm -f /tmp/out /tmp/lab6-test.txt
