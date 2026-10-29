# One customer-managed key for HealthConnect's data at rest: the database, its master
# password in Secrets Manager, and the patient media bucket. A key we own (instead of the
# AWS-managed default) means:
#   - access to patient data also needs kms:Decrypt on this key, which only the app role
#     and account admins have (iam.tf), and
#   - every use of the key is recorded in CloudTrail.

resource "aws_kms_key" "data" {
  description             = "HealthConnect patient data at rest (RDS, Secrets Manager, S3)"
  enable_key_rotation     = true
  deletion_window_in_days = 7
}

resource "aws_kms_alias" "data" {
  name          = "alias/healthconnect-data"
  target_key_id = aws_kms_key.data.key_id
}
