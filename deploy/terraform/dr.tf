# Disaster recovery: the database's automated backups are copied to a second region, so losing
# Cape Town (or the instance and every backup in it) does not lose the data. Any second of the
# last 7 days can be restored in Ireland as a new instance (docs/runbooks/disaster-recovery.md).
#
# Cost: backup storage in eu-west-1 (about the size of the database, a few GB) plus a KMS key
# ($1/month), roughly $2-3 a month. dr_backup_region = "" switches it off.

variable "dr_backup_region" {
  description = "Region the database's automated backups are replicated to; empty turns replication off"
  type        = string
  default     = "eu-west-1"
}

provider "aws" {
  alias  = "dr"
  region = var.dr_backup_region == "" ? var.region : var.dr_backup_region
  default_tags {
    tags = { Project = "healthconnect", Team = "Godabeg", ManagedBy = "terraform" }
  }
}

locals {
  dr_count = var.dr_backup_region == "" ? 0 : 1
}

# The copies are encrypted again in the destination region with a key that lives there.
resource "aws_kms_key" "dr" {
  count                   = local.dr_count
  provider                = aws.dr
  description             = "HealthConnect database backups replicated from ${var.region}"
  enable_key_rotation     = true
  deletion_window_in_days = 30
}

resource "aws_kms_alias" "dr" {
  count         = local.dr_count
  provider      = aws.dr
  name          = "alias/healthconnect-dr-backups"
  target_key_id = aws_kms_key.dr[0].key_id
}

resource "aws_db_instance_automated_backups_replication" "main" {
  count                  = local.dr_count
  provider               = aws.dr
  source_db_instance_arn = aws_db_instance.main.arn
  kms_key_id             = aws_kms_key.dr[0].arn
  retention_period       = 7
}

output "dr_backups" {
  description = "Where the database's backups are replicated, for the recovery runbook"
  value       = local.dr_count == 0 ? null : { region = var.dr_backup_region, kms_key = aws_kms_alias.dr[0].name }
}
