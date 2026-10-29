#!/bin/bash
# Backup restore drill: proves the database backups actually restore, and how long it takes
# (the recovery time objective in deploy/README.md comes from this).
#
#   AWS_PROFILE=agroconnect bash deploy/ops/restore-drill.sh
#
# Restores healthconnect-db to "5 minutes ago" as a temporary copy (point-in-time recovery),
# waits until it is available, prints the timings, then deletes the copy. The copy is private
# (same subnets and security group, no public access) and costs a few cents for its ~20 min.
set -euo pipefail

REGION="${AWS_REGION:-af-south-1}"
SOURCE=healthconnect-db
COPY="healthconnect-db-drill-$(date +%Y%m%d%H%M)"
AT=$(date -u -v-5M +%Y-%m-%dT%H:%M:%SZ 2>/dev/null || date -u -d '5 minutes ago' +%Y-%m-%dT%H:%M:%SZ)

info=$(aws rds describe-db-instances --region "$REGION" --db-instance-identifier "$SOURCE" --query 'DBInstances[0]')
subnets=$(jq -r .DBSubnetGroup.DBSubnetGroupName <<<"$info")
sg=$(jq -r '.VpcSecurityGroups[0].VpcSecurityGroupId' <<<"$info")
latest=$(jq -r .LatestRestorableTime <<<"$info")
echo "Latest restorable time: $latest; restoring to $AT"

cleanup() {
  echo "Deleting the drill copy $COPY"
  aws rds delete-db-instance --region "$REGION" --db-instance-identifier "$COPY" \
    --skip-final-snapshot --delete-automated-backups >/dev/null 2>&1 || true
}
trap cleanup EXIT

start=$(date +%s)
aws rds restore-db-instance-to-point-in-time --region "$REGION" \
  --source-db-instance-identifier "$SOURCE" --target-db-instance-identifier "$COPY" \
  --restore-time "$AT" --db-instance-class db.t4g.micro \
  --db-subnet-group-name "$subnets" --vpc-security-group-ids "$sg" \
  --no-publicly-accessible --no-multi-az --no-deletion-protection \
  --tags Key=Project,Value=healthconnect Key=Purpose,Value=restore-drill >/dev/null
echo "Restore started at $(date -u +%H:%M:%SZ)"
aws rds wait db-instance-available --region "$REGION" --db-instance-identifier "$COPY"
end=$(date +%s)

aws rds describe-db-instances --region "$REGION" --db-instance-identifier "$COPY" \
  --query 'DBInstances[0].[DBInstanceIdentifier,DBInstanceStatus,Engine,EngineVersion,StorageEncrypted,PubliclyAccessible]' \
  --output text
echo "RESTORE OK: point-in-time copy available in $(((end - start) / 60)) min $(((end - start) % 60)) s"
echo "(Data check: from the app server, psql against the copy's endpoint and count patients.)"
