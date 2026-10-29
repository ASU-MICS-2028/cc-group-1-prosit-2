# Optional: stop the app server and the database at night and start them in the morning
# (Ghana time), with EventBridge Scheduler (free at this volume). With 8 quiet hours a night
# that's about a third off the server and database bill. Off by default (var.nightly_stop),
# so nobody loses the demo late at night; turn it on once the demo is recorded.
#
# The NAT stays up: it is a self-healing group of one, which would just start a new one, and
# it costs $0.13 a day. The PWA keeps working offline while the server sleeps; the phone's
# outbox syncs when it wakes.

locals {
  nightly = var.nightly_stop ? {
    stop_app  = { hour = var.nightly_stop_hours.stop, action = "aws-sdk:ec2:stopInstances", input = jsonencode({ InstanceIds = [aws_instance.app.id] }) }
    start_app = { hour = var.nightly_stop_hours.start, action = "aws-sdk:ec2:startInstances", input = jsonencode({ InstanceIds = [aws_instance.app.id] }) }
    stop_db   = { hour = var.nightly_stop_hours.stop, action = "aws-sdk:rds:stopDBInstance", input = jsonencode({ DbInstanceIdentifier = aws_db_instance.main.identifier }) }
    start_db  = { hour = var.nightly_stop_hours.start, action = "aws-sdk:rds:startDBInstance", input = jsonencode({ DbInstanceIdentifier = aws_db_instance.main.identifier }) }
  } : {}
}

data "aws_iam_policy_document" "scheduler_assume" {
  statement {
    actions = ["sts:AssumeRole"]
    principals {
      type        = "Service"
      identifiers = ["scheduler.amazonaws.com"]
    }
    condition {
      test     = "StringEquals"
      variable = "aws:SourceAccount"
      values   = [data.aws_caller_identity.current.account_id]
    }
  }
}

resource "aws_iam_role" "scheduler" {
  count              = var.nightly_stop ? 1 : 0
  name               = "healthconnect-nightly-stop"
  assume_role_policy = data.aws_iam_policy_document.scheduler_assume.json
}

data "aws_iam_policy_document" "scheduler" {
  statement {
    sid       = "StartStopTheAppServerOnly"
    actions   = ["ec2:StartInstances", "ec2:StopInstances"]
    resources = [aws_instance.app.arn]
  }
  statement {
    sid       = "StartStopTheDatabaseOnly"
    actions   = ["rds:StartDBInstance", "rds:StopDBInstance"]
    resources = [aws_db_instance.main.arn]
  }
  # The server's disk is encrypted with our key, so starting it needs a grant on that key
  # (only for AWS services acting on our behalf).
  statement {
    sid       = "StartEncryptedDisk"
    actions   = ["kms:CreateGrant"]
    resources = [aws_kms_key.data.arn]
    condition {
      test     = "Bool"
      variable = "kms:GrantIsForAWSResource"
      values   = ["true"]
    }
  }
}

resource "aws_iam_role_policy" "scheduler" {
  count  = var.nightly_stop ? 1 : 0
  name   = "start-stop"
  role   = aws_iam_role.scheduler[0].id
  policy = data.aws_iam_policy_document.scheduler.json
}

resource "aws_scheduler_schedule" "nightly" {
  for_each = local.nightly

  name                         = "healthconnect-${replace(each.key, "_", "-")}"
  schedule_expression          = "cron(0 ${each.value.hour} * * ? *)"
  schedule_expression_timezone = "Africa/Accra"

  flexible_time_window {
    mode = "OFF"
  }

  target {
    arn      = "arn:aws:scheduler:::${each.value.action}"
    role_arn = aws_iam_role.scheduler[0].arn
    input    = each.value.input

    retry_policy {
      maximum_retry_attempts = 3
    }
  }
}
