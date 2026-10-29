#!/bin/bash
# Runs ON the app server (sent by .github/workflows/deploy.yml through SSM Run Command, so no
# SSH and no open port). Starts one environment's release and rolls back if it isn't healthy.
#
#   remote-deploy.sh <production|staging> <frontend-tag> [backend-tag]
#
# The compose file arrives base64-encoded in COMPOSE_B64 (SSM can't copy files).
set -euo pipefail

ENV="$1"
FRONTEND_TAG="$2"
BACKEND_TAG="${3:-}"
DIR="/opt/healthconnect/$ENV"
cd "$DIR"

# shellcheck disable=SC1091
set -a; source settings.env; set +a
PROJECT="healthconnect-$ENV"

echo "$COMPOSE_B64" | base64 -d > compose.yml

aws ecr get-login-password --region "$AWS_REGION" \
  | docker login --username AWS --password-stdin "$REGISTRY" >/dev/null

# Staging keeps its data in its own database on the same RDS instance.
if [ -n "$BACKEND_TAG" ] && [ "$ENV" = staging ]; then
  PGPASSWORD=$(aws secretsmanager get-secret-value --region "$AWS_REGION" --secret-id "$DB_SECRET_ARN" \
    --query SecretString --output text | jq -r .password)
  export PGPASSWORD
  conn="host=$DB_HOST dbname=healthconnect user=healthconnect sslmode=verify-full sslrootcert=$DB_SSL_CA"
  if ! psql "$conn" -tAc "select 1 from pg_database where datname = '$DB_NAME'" | grep -q 1; then
    psql "$conn" -c "create database $DB_NAME"
  fi
  unset PGPASSWORD
fi

# Keep the running release, to roll back to.
[ -f release.env ] && cp release.env release.prev.env

echo "FRONTEND_TAG=$FRONTEND_TAG" > release.env
if [ -n "$BACKEND_TAG" ]; then
  printf 'BACKEND_TAG=%s\nCOMPOSE_PROFILES=api\n' "$BACKEND_TAG" >> release.env
fi

compose() {
  docker compose -p "$PROJECT" --env-file settings.env --env-file "$1" -f compose.yml "${@:2}"
}

# Is this release answering? nginx on the port, and the API when there is one.
healthy() {
  for _ in $(seq 1 30); do
    if curl -fsS "http://127.0.0.1:$HOST_PORT/health" >/dev/null 2>&1 &&
      { [ -z "$BACKEND_TAG" ] || curl -fsS "http://127.0.0.1:$HOST_PORT/api/health" >/dev/null 2>&1; }; then
      return 0
    fi
    sleep 3
  done
  return 1
}

compose release.env pull --quiet

# The first-boot placeholder holds the port until the first real release.
docker rm -f "placeholder-$HOST_PORT" >/dev/null 2>&1 || true
[ "$HOST_PORT" = 80 ] && docker rm -f placeholder >/dev/null 2>&1 || true

compose release.env up -d --remove-orphans

if healthy; then
  echo "$ENV is healthy on $FRONTEND_TAG${BACKEND_TAG:+ / backend $BACKEND_TAG}"
  # Old images only take disk; the last few stay in ECR for rollbacks.
  docker image prune -af --filter "until=72h" >/dev/null || true
  exit 0
fi

echo "::error::$ENV failed its health check; rolling back" >&2
compose release.env logs --tail 50 >&2 || true
if [ -f release.prev.env ]; then
  cp release.prev.env release.env
  compose release.env up -d --remove-orphans
  healthy && echo "rolled back to the previous release" >&2
fi
exit 1
