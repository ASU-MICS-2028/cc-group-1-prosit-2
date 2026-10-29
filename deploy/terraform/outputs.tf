output "app_url" {
  description = "Production: the demo URL (no domain needed)."
  value       = "https://${aws_cloudfront_distribution.app["production"].domain_name}"
}

output "staging_url" {
  description = "Staging: the team's shared test URL, deployed from development."
  value       = "https://${aws_cloudfront_distribution.app["staging"].domain_name}"
}

output "distribution_ids" {
  value = { for k, d in aws_cloudfront_distribution.app : k => d.id }
}

output "load_balancer_dns" {
  description = "Internal load balancer: resolves to private addresses only."
  value       = aws_lb.main.dns_name
}

output "dashboard_url" {
  value = "https://${var.region}.console.aws.amazon.com/cloudwatch/home?region=${var.region}#dashboards/dashboard/${aws_cloudwatch_dashboard.main.dashboard_name}"
}

output "alerts_topic_arn" {
  value = aws_sns_topic.alerts.arn
}

output "app_instance_id" {
  description = "For Session Manager: aws ssm start-session --target <id>"
  value       = aws_instance.app.id
}

output "app_role_arn" {
  value = aws_iam_role.app.arn
}

output "app_policy_json" {
  description = "The least-privilege policy document (Lab 6 deliverable A)."
  value       = data.aws_iam_policy_document.app.json
}

output "db_instance_id" {
  value = aws_db_instance.main.identifier
}

output "db_endpoint" {
  value = aws_db_instance.main.address
}

output "db_secret_arn" {
  value = aws_db_instance.main.master_user_secret[0].secret_arn
}

output "media_bucket" {
  value = aws_s3_bucket.media.bucket
}

output "kms_key_arn" {
  value = aws_kms_key.data.arn
}

output "github_deploy_role_arn" {
  value = aws_iam_role.github_deploy.arn
}
