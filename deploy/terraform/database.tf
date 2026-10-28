# PostgreSQL (RDS) in the private db subnets: no public IP, no route to the internet,
# reachable only from the app server's security group.
#
# Encryption at rest: storage_encrypted with our KMS key (kms.tf).
# Encryption in transit: rds.force_ssl = 1 refuses any connection that isn't TLS.
# Credentials: RDS creates the master password and keeps it in Secrets Manager
# (manage_master_user_password), so no password ever appears in Terraform state, code or the
# server's environment, and rotating it is one API call (the Week 6 rotation drill).

resource "aws_db_subnet_group" "private" {
  name       = "healthconnect-private"
  subnet_ids = aws_subnet.db[*].id
}

resource "aws_db_parameter_group" "postgres" {
  name   = "healthconnect-postgres17"
  family = "postgres17"

  parameter {
    name         = "rds.force_ssl"
    value        = "1"
    apply_method = "pending-reboot" # how RDS records this static parameter
  }

  # Record every connection, so the database's own logs show who connected and when.
  parameter {
    name  = "log_connections"
    value = "1"
  }
}

resource "aws_db_instance" "main" {
  identifier     = "healthconnect-db"
  engine         = "postgres"
  engine_version = "17"
  instance_class = var.rds_instance_class

  allocated_storage = 20
  storage_type      = "gp3"
  storage_encrypted = true
  kms_key_id        = aws_kms_key.data.arn

  db_name                       = "healthconnect"
  username                      = "healthconnect"
  manage_master_user_password   = true
  master_user_secret_kms_key_id = aws_kms_key.data.key_id

  parameter_group_name   = aws_db_parameter_group.postgres.name
  db_subnet_group_name   = aws_db_subnet_group.private.name
  vpc_security_group_ids = [aws_security_group.database.id]
  publicly_accessible    = false
  multi_az               = false

  # Backups: daily snapshot plus point-in-time restore to any second of the last 7 days
  # (deploy/ops/restore-drill.sh rehearses it). Night-time windows, Ghana time = UTC.
  backup_retention_period         = 7
  backup_window                   = "01:00-01:30"
  maintenance_window              = "sun:02:00-sun:02:30"
  copy_tags_to_snapshot           = true
  enabled_cloudwatch_logs_exports = ["postgresql"]
  # Synthetic data for a one-week course project: protected from accidents, but a final
  # snapshot is still taken if it is ever deleted.
  deletion_protection       = true
  skip_final_snapshot       = false
  final_snapshot_identifier = "healthconnect-db-final"

  auto_minor_version_upgrade = true
  apply_immediately          = true
}
