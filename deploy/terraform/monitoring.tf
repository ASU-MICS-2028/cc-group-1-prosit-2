# Knowing when it's down, and fixing what fixes itself.
#
#   self-healing   a failed host is recovered onto new hardware (same disk, same IP); a hung
#                  OS is rebooted. Both are free CloudWatch alarm actions.
#   alerts         email (SNS) when the site is down, throws errors, or the database runs low
#   dashboard      one page with traffic, errors, latency, CPU and database health
#
# All of it is CloudWatch's pay-per-use tier: ~10 alarms at $0.10 each and one dashboard
# (the first three are free), so well under $2 a month. The CloudFront alarm lives in
# us-east-1 because that's where CloudFront publishes its metrics.

provider "aws" {
  alias  = "us_east_1"
  region = "us-east-1"
  default_tags {
    tags = { Project = "healthconnect", Team = "Godabeg", ManagedBy = "terraform" }
  }
}

locals {
  alert_emails = length(var.alert_emails) > 0 ? var.alert_emails : var.budget_emails
}

resource "aws_sns_topic" "alerts" {
  name = "healthconnect-alerts"
}

resource "aws_sns_topic_subscription" "alerts" {
  for_each  = toset(local.alert_emails)
  topic_arn = aws_sns_topic.alerts.arn
  protocol  = "email"
  endpoint  = each.value # AWS emails a confirmation link first
}

resource "aws_sns_topic" "alerts_us_east_1" {
  provider = aws.us_east_1
  name     = "healthconnect-alerts"
}

resource "aws_sns_topic_subscription" "alerts_us_east_1" {
  provider  = aws.us_east_1
  for_each  = toset(local.alert_emails)
  topic_arn = aws_sns_topic.alerts_us_east_1.arn
  protocol  = "email"
  endpoint  = each.value
}

# ---- Self-healing ---------------------------------------------------------------------

resource "aws_cloudwatch_metric_alarm" "app_recover" {
  alarm_name          = "healthconnect-app-host-failed"
  alarm_description   = "AWS hardware or network under the app server failed: recover it onto healthy hardware."
  namespace           = "AWS/EC2"
  metric_name         = "StatusCheckFailed_System"
  dimensions          = { InstanceId = aws_instance.app.id }
  statistic           = "Maximum"
  period              = 60
  evaluation_periods  = 2
  threshold           = 1
  comparison_operator = "GreaterThanOrEqualToThreshold"
  alarm_actions       = ["arn:aws:automate:${var.region}:ec2:recover", aws_sns_topic.alerts.arn]
}

resource "aws_cloudwatch_metric_alarm" "app_reboot" {
  alarm_name          = "healthconnect-app-os-unresponsive"
  alarm_description   = "The app server's operating system stopped responding: reboot it."
  namespace           = "AWS/EC2"
  metric_name         = "StatusCheckFailed_Instance"
  dimensions          = { InstanceId = aws_instance.app.id }
  statistic           = "Maximum"
  period              = 60
  evaluation_periods  = 3
  threshold           = 1
  comparison_operator = "GreaterThanOrEqualToThreshold"
  alarm_actions       = ["arn:aws:automate:${var.region}:ec2:reboot", aws_sns_topic.alerts.arn]
}

# ---- The site -------------------------------------------------------------------------

resource "aws_cloudwatch_metric_alarm" "site_down" {
  alarm_name          = "healthconnect-production-down"
  alarm_description   = "No healthy production server behind the load balancer for 3 minutes."
  namespace           = "AWS/ApplicationELB"
  metric_name         = "HealthyHostCount"
  dimensions          = { LoadBalancer = aws_lb.main.arn_suffix, TargetGroup = aws_lb_target_group.app["production"].arn_suffix }
  statistic           = "Minimum"
  period              = 60
  evaluation_periods  = 3
  threshold           = 1
  comparison_operator = "LessThanThreshold"
  treat_missing_data  = "breaching"
  alarm_actions       = [aws_sns_topic.alerts.arn]
  ok_actions          = [aws_sns_topic.alerts.arn]
  # Quiet while the server is deliberately stopped (app_running = false or the nightly stop).
  actions_enabled = var.app_running && !var.nightly_stop
}

resource "aws_cloudwatch_metric_alarm" "server_errors" {
  alarm_name          = "healthconnect-production-5xx"
  alarm_description   = "The app answered with more than 10 server errors (5xx) in 5 minutes."
  namespace           = "AWS/ApplicationELB"
  metric_name         = "HTTPCode_Target_5XX_Count"
  dimensions          = { LoadBalancer = aws_lb.main.arn_suffix, TargetGroup = aws_lb_target_group.app["production"].arn_suffix }
  statistic           = "Sum"
  period              = 300
  evaluation_periods  = 1
  threshold           = 10
  comparison_operator = "GreaterThanThreshold"
  treat_missing_data  = "notBreaching"
  alarm_actions       = [aws_sns_topic.alerts.arn]
}

resource "aws_cloudwatch_metric_alarm" "slow" {
  alarm_name          = "healthconnect-production-slow"
  alarm_description   = "Typical response (p90) slower than 2 seconds for 10 minutes: 2G users will give up."
  namespace           = "AWS/ApplicationELB"
  metric_name         = "TargetResponseTime"
  dimensions          = { LoadBalancer = aws_lb.main.arn_suffix, TargetGroup = aws_lb_target_group.app["production"].arn_suffix }
  extended_statistic  = "p90"
  period              = 300
  evaluation_periods  = 2
  threshold           = 2
  comparison_operator = "GreaterThanThreshold"
  treat_missing_data  = "notBreaching"
  alarm_actions       = [aws_sns_topic.alerts.arn]
}

# What users actually see, including anything CloudFront itself can't reach.
resource "aws_cloudwatch_metric_alarm" "edge_errors" {
  provider            = aws.us_east_1
  alarm_name          = "healthconnect-production-edge-5xx"
  alarm_description   = "More than 5% of requests to the production URL failed with 5xx for 10 minutes."
  namespace           = "AWS/CloudFront"
  metric_name         = "5xxErrorRate"
  dimensions          = { DistributionId = aws_cloudfront_distribution.app["production"].id, Region = "Global" }
  statistic           = "Average"
  period              = 300
  evaluation_periods  = 2
  threshold           = 5
  comparison_operator = "GreaterThanThreshold"
  treat_missing_data  = "notBreaching"
  alarm_actions       = [aws_sns_topic.alerts_us_east_1.arn]
}

# ---- The database ---------------------------------------------------------------------

resource "aws_cloudwatch_metric_alarm" "db_cpu" {
  alarm_name          = "healthconnect-db-cpu"
  alarm_description   = "Database CPU above 80% for 15 minutes (burstable credits running out)."
  namespace           = "AWS/RDS"
  metric_name         = "CPUUtilization"
  dimensions          = { DBInstanceIdentifier = aws_db_instance.main.identifier }
  statistic           = "Average"
  period              = 300
  evaluation_periods  = 3
  threshold           = 80
  comparison_operator = "GreaterThanThreshold"
  alarm_actions       = [aws_sns_topic.alerts.arn]
}

resource "aws_cloudwatch_metric_alarm" "db_storage" {
  alarm_name          = "healthconnect-db-storage"
  alarm_description   = "Less than 2 GB of database storage left."
  namespace           = "AWS/RDS"
  metric_name         = "FreeStorageSpace"
  dimensions          = { DBInstanceIdentifier = aws_db_instance.main.identifier }
  statistic           = "Minimum"
  period              = 300
  evaluation_periods  = 1
  threshold           = 2 * 1024 * 1024 * 1024
  comparison_operator = "LessThanThreshold"
  alarm_actions       = [aws_sns_topic.alerts.arn]
}

resource "aws_cloudwatch_metric_alarm" "db_memory" {
  alarm_name          = "healthconnect-db-memory"
  alarm_description   = "Database free memory under 64 MB for 15 minutes."
  namespace           = "AWS/RDS"
  metric_name         = "FreeableMemory"
  dimensions          = { DBInstanceIdentifier = aws_db_instance.main.identifier }
  statistic           = "Average"
  period              = 300
  evaluation_periods  = 3
  threshold           = 64 * 1024 * 1024
  comparison_operator = "LessThanThreshold"
  alarm_actions       = [aws_sns_topic.alerts.arn]
}

# ---- Dashboard ------------------------------------------------------------------------

locals {
  lb = aws_lb.main.arn_suffix
  tg = { for k, v in aws_lb_target_group.app : k => v.arn_suffix }
  widget = {
    type   = "metric"
    width  = 12
    height = 6
  }
}

resource "aws_cloudwatch_dashboard" "main" {
  dashboard_name = "healthconnect"
  dashboard_body = jsonencode({
    widgets = [
      merge(local.widget, { x = 0, y = 0, properties = {
        title   = "Requests per minute", region = var.region, stat = "Sum", period = 60, view = "timeSeries",
        metrics = [for env in keys(local.tg) : ["AWS/ApplicationELB", "RequestCount", "LoadBalancer", local.lb, "TargetGroup", local.tg[env], { label = env }]]
      } }),
      merge(local.widget, { x = 12, y = 0, properties = {
        title = "Server errors (5xx) and client errors (4xx)", region = var.region, stat = "Sum", period = 60, view = "timeSeries",
        metrics = [
          ["AWS/ApplicationELB", "HTTPCode_Target_5XX_Count", "LoadBalancer", local.lb, "TargetGroup", local.tg["production"], { label = "5xx", color = "#d62728" }],
          ["AWS/ApplicationELB", "HTTPCode_Target_4XX_Count", "LoadBalancer", local.lb, "TargetGroup", local.tg["production"], { label = "4xx", color = "#ff7f0e" }],
        ]
      } }),
      merge(local.widget, { x = 0, y = 6, properties = {
        title = "Response time (seconds)", region = var.region, period = 60, view = "timeSeries",
        metrics = [
          ["AWS/ApplicationELB", "TargetResponseTime", "LoadBalancer", local.lb, "TargetGroup", local.tg["production"], { stat = "p50", label = "p50" }],
          ["AWS/ApplicationELB", "TargetResponseTime", "LoadBalancer", local.lb, "TargetGroup", local.tg["production"], { stat = "p90", label = "p90" }],
        ]
      } }),
      merge(local.widget, { x = 12, y = 6, properties = {
        title   = "Healthy servers", region = var.region, stat = "Minimum", period = 60, view = "timeSeries",
        metrics = [for env in keys(local.tg) : ["AWS/ApplicationELB", "HealthyHostCount", "LoadBalancer", local.lb, "TargetGroup", local.tg[env], { label = env }]]
      } }),
      merge(local.widget, { x = 0, y = 12, properties = {
        title = "CPU %", region = var.region, stat = "Average", period = 300, view = "timeSeries",
        metrics = [
          ["AWS/EC2", "CPUUtilization", "InstanceId", aws_instance.app.id, { label = "app server" }],
          ["AWS/RDS", "CPUUtilization", "DBInstanceIdentifier", aws_db_instance.main.identifier, { label = "database" }],
        ]
      } }),
      merge(local.widget, { x = 12, y = 12, properties = {
        title = "Database connections and free storage (GB)", region = var.region, period = 300, view = "timeSeries",
        metrics = [
          ["AWS/RDS", "DatabaseConnections", "DBInstanceIdentifier", aws_db_instance.main.identifier, { stat = "Maximum", label = "connections" }],
          [{ expression = "m2 / 1073741824", label = "free storage (GB)", yAxis = "right" }],
          ["AWS/RDS", "FreeStorageSpace", "DBInstanceIdentifier", aws_db_instance.main.identifier, { stat = "Minimum", id = "m2", visible = false }],
        ]
      } }),
      { type = "alarm", x = 0, y = 18, width = 24, height = 4, properties = {
        title = "Alarms",
        alarms = [
          aws_cloudwatch_metric_alarm.site_down.arn,
          aws_cloudwatch_metric_alarm.server_errors.arn,
          aws_cloudwatch_metric_alarm.slow.arn,
          aws_cloudwatch_metric_alarm.app_recover.arn,
          aws_cloudwatch_metric_alarm.app_reboot.arn,
          aws_cloudwatch_metric_alarm.db_cpu.arn,
          aws_cloudwatch_metric_alarm.db_storage.arn,
          aws_cloudwatch_metric_alarm.db_memory.arn,
        ]
      } },
    ]
  })
}
