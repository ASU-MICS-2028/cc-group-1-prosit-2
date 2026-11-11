# Lab 7: appointment events and the two serverless consumers. One set per environment.
#
#   API (EC2)  --publish-->  SNS topic  healthconnect-<env>-appointment-events
#                               |-- SQS reminders  (+ DLQ)  --> Lambda appointment-reminder
#                               |                                   `-> EventBridge Scheduler one-off schedule
#                               |                                         `-> same Lambda, "send" -> SMS (Arkesel, or logged)
#                               `-- SQS specialist-notify (+ DLQ, referrals/teleconsults only)
#                                                               --> Lambda specialist-notify --> SNS clinician alerts (email)
#
# Why SNS in front of the queues: one SQS message is consumed by one reader, so two consumers
# reading the same queue would each see half of the events. SNS copies every event to every
# queue; each consumer gets its own copy, its own retries and its own dead-letter queue.
#
# Why the API stays on EC2 and only these move to Lambda: reminders are bursty, short and
# rare (a few hundred a day), which is what Lambda's per-invocation pricing suits. The API
# holds database connections and the video-call WebSockets open for minutes; that is a poor
# fit for Lambda and already paid for on the instance.
#
# Cost at class volume: well inside the free tiers (SNS, SQS, Lambda, DynamoDB on-demand,
# Scheduler 14M/month free). The only fixed cost is the events KMS key, $1/month.

locals {
  event_envs = toset(keys(local.env_ports))
}

# ---- Encryption -------------------------------------------------------------------------
# The events carry the patient's phone number, so the topic, queues and schedules are
# encrypted with a key of our own. SNS needs to use it to hand messages to the encrypted
# queues, which is the one grant the key policy adds beyond the account's IAM.

data "aws_iam_policy_document" "events_key" {
  statement {
    sid       = "AccountAdminsAndIamPolicies"
    actions   = ["kms:*"]
    resources = ["*"]
    principals {
      type        = "AWS"
      identifiers = ["arn:aws:iam::${data.aws_caller_identity.current.account_id}:root"]
    }
  }

  statement {
    sid       = "SnsDeliversToEncryptedQueues"
    actions   = ["kms:GenerateDataKey*", "kms:Decrypt"]
    resources = ["*"]
    principals {
      type        = "Service"
      identifiers = ["sns.amazonaws.com"]
    }
    condition {
      test     = "StringEquals"
      variable = "aws:SourceAccount"
      values   = [data.aws_caller_identity.current.account_id]
    }
  }
}

resource "aws_kms_key" "events" {
  description             = "HealthConnect appointment events (SNS, SQS, reminder schedules)"
  enable_key_rotation     = true
  deletion_window_in_days = 7
  policy                  = data.aws_iam_policy_document.events_key.json
}

resource "aws_kms_alias" "events" {
  name          = "alias/healthconnect-events"
  target_key_id = aws_kms_key.events.key_id
}

# ---- Topic and queues -------------------------------------------------------------------

resource "aws_sns_topic" "appointment_events" {
  for_each          = local.event_envs
  name              = "healthconnect-${each.key}-appointment-events"
  kms_master_key_id = aws_kms_key.events.arn
  tags              = { Environment = each.key }
}

locals {
  # consumer => SNS filter policy (null = every event)
  consumers = {
    reminders = null
    specialist-notify = jsonencode({
      appointmentType = ["referral", "teleconsult"]
      eventType       = ["appointment.created", "appointment.rescheduled", "appointment.cancelled"]
    })
  }
  consumer_queues = { for pair in setproduct(local.event_envs, keys(local.consumers)) : "${pair[0]}/${pair[1]}" => {
    env      = pair[0]
    consumer = pair[1]
  } }
}

resource "aws_sqs_queue" "events_dlq" {
  for_each                  = local.consumer_queues
  name                      = "healthconnect-${each.value.env}-${each.value.consumer}-dlq"
  message_retention_seconds = 1209600 # 14 days to look at what failed and redrive it
  kms_master_key_id         = aws_kms_key.events.arn
  tags                      = { Environment = each.value.env }
}

resource "aws_sqs_queue" "events" {
  for_each                   = local.consumer_queues
  name                       = "healthconnect-${each.value.env}-${each.value.consumer}"
  visibility_timeout_seconds = 60 # 6x the function timeout, as AWS recommends
  message_retention_seconds  = 345600
  kms_master_key_id          = aws_kms_key.events.arn
  redrive_policy = jsonencode({
    deadLetterTargetArn = aws_sqs_queue.events_dlq[each.key].arn
    maxReceiveCount     = 3 # three tries, then the dead-letter queue: nothing vanishes silently
  })
  tags = { Environment = each.value.env }
}

resource "aws_sqs_queue_redrive_allow_policy" "events_dlq" {
  for_each  = local.consumer_queues
  queue_url = aws_sqs_queue.events_dlq[each.key].id
  redrive_allow_policy = jsonencode({
    redrivePermission = "byQueue"
    sourceQueueArns   = [aws_sqs_queue.events[each.key].arn]
  })
}

data "aws_iam_policy_document" "events_queue" {
  for_each = local.consumer_queues
  statement {
    sid       = "OnlyOurTopicMaySend"
    actions   = ["sqs:SendMessage"]
    resources = [aws_sqs_queue.events[each.key].arn]
    principals {
      type        = "Service"
      identifiers = ["sns.amazonaws.com"]
    }
    condition {
      test     = "ArnEquals"
      variable = "aws:SourceArn"
      values   = [aws_sns_topic.appointment_events[each.value.env].arn]
    }
  }
}

resource "aws_sqs_queue_policy" "events" {
  for_each  = local.consumer_queues
  queue_url = aws_sqs_queue.events[each.key].id
  policy    = data.aws_iam_policy_document.events_queue[each.key].json
}

resource "aws_sns_topic_subscription" "events" {
  for_each             = local.consumer_queues
  topic_arn            = aws_sns_topic.appointment_events[each.value.env].arn
  protocol             = "sqs"
  endpoint             = aws_sqs_queue.events[each.key].arn
  raw_message_delivery = true # the queue gets the API's JSON as is, with SNS attributes as SQS attributes
  filter_policy        = local.consumers[each.value.consumer]
}

# ---- Idempotency ------------------------------------------------------------------------
# SNS and SQS deliver at least once. Each consumer records the events it handled (key
# "<consumer>#<eventId>") so a repeat does not send a second text. Rows expire after 30 days.

resource "aws_dynamodb_table" "processed_events" {
  for_each     = local.event_envs
  name         = "healthconnect-${each.key}-processed-events"
  billing_mode = "PAY_PER_REQUEST"
  hash_key     = "pk"

  attribute {
    name = "pk"
    type = "S"
  }

  ttl {
    attribute_name = "expires"
    enabled        = true
  }

  server_side_encryption {
    enabled     = true
    kms_key_arn = aws_kms_key.events.arn
  }

  point_in_time_recovery {
    enabled = false # only a dedupe log: losing it means at worst one repeated reminder
  }

  tags = { Environment = each.key }
}

# ---- Clinician alerts (what specialist-notify publishes to) ------------------------------

resource "aws_sns_topic" "clinician_alerts" {
  for_each          = local.event_envs
  name              = "healthconnect-${each.key}-clinician-alerts"
  kms_master_key_id = aws_kms_key.events.arn
  tags              = { Environment = each.key }
}

resource "aws_sns_topic_subscription" "clinician_alerts" {
  for_each = { for pair in setproduct(local.event_envs, var.clinician_alert_emails) : "${pair[0]}/${pair[1]}" => {
    env   = pair[0]
    email = pair[1]
  } }
  topic_arn = aws_sns_topic.clinician_alerts[each.value.env].arn
  protocol  = "email"
  endpoint  = each.value.email
}

# ---- Functions --------------------------------------------------------------------------

data "archive_file" "functions" {
  type        = "zip"
  source_dir  = "${path.module}/../../functions/src"
  output_path = "${path.module}/.build/functions.zip"
  excludes    = ["__pycache__"]
}

locals {
  functions = {
    appointment-reminder = { handler = "appointment_reminder.handler", consumer = "reminders" }
    specialist-notify    = { handler = "specialist_notify.handler", consumer = "specialist-notify" }
  }
  env_functions = { for pair in setproduct(local.event_envs, keys(local.functions)) : "${pair[0]}/${pair[1]}" => {
    env  = pair[0]
    name = pair[1]
    fn   = local.functions[pair[1]]
  } }
}

data "aws_iam_policy_document" "lambda_assume" {
  statement {
    actions = ["sts:AssumeRole"]
    principals {
      type        = "Service"
      identifiers = ["lambda.amazonaws.com"]
    }
  }
}

resource "aws_iam_role" "function" {
  for_each           = local.env_functions
  name               = "healthconnect-${each.value.env}-${each.value.name}"
  description        = "Lab 7 ${each.value.name} (${each.value.env}): its own queue, table rows and nothing else"
  assume_role_policy = data.aws_iam_policy_document.lambda_assume.json
}

resource "aws_cloudwatch_log_group" "function" {
  for_each          = local.env_functions
  name              = "/aws/lambda/healthconnect-${each.value.env}-${each.value.name}"
  retention_in_days = 30
}

data "aws_iam_policy_document" "function" {
  for_each = local.env_functions

  statement {
    sid       = "OwnLogs"
    actions   = ["logs:CreateLogStream", "logs:PutLogEvents"]
    resources = ["${aws_cloudwatch_log_group.function[each.key].arn}:*"]
  }

  statement {
    sid       = "ReadOwnQueue"
    actions   = ["sqs:ReceiveMessage", "sqs:DeleteMessage", "sqs:GetQueueAttributes", "sqs:ChangeMessageVisibility"]
    resources = [aws_sqs_queue.events["${each.value.env}/${each.value.fn.consumer}"].arn]
  }

  statement {
    sid       = "UseEventsKey"
    actions   = ["kms:Decrypt", "kms:GenerateDataKey"]
    resources = [aws_kms_key.events.arn]
  }

  statement {
    sid       = "Idempotency"
    actions   = ["dynamodb:PutItem", "dynamodb:DeleteItem"]
    resources = [aws_dynamodb_table.processed_events[each.value.env].arn]
    condition {
      test     = "ForAllValues:StringLike"
      variable = "dynamodb:LeadingKeys"
      values   = ["${each.value.name}#*"] # only its own rows
    }
  }

  dynamic "statement" {
    for_each = each.value.name == "appointment-reminder" ? [1] : []
    content {
      sid       = "OwnReminderSchedules"
      actions   = ["scheduler:CreateSchedule", "scheduler:UpdateSchedule", "scheduler:DeleteSchedule"]
      resources = ["arn:aws:scheduler:${var.region}:${data.aws_caller_identity.current.account_id}:schedule/${aws_scheduler_schedule_group.reminders[each.value.env].name}/*"]
    }
  }

  dynamic "statement" {
    for_each = each.value.name == "appointment-reminder" ? [1] : []
    content {
      sid       = "HandScheduleItsRole"
      actions   = ["iam:PassRole"]
      resources = [aws_iam_role.reminder_scheduler[each.value.env].arn]
    }
  }

  dynamic "statement" {
    for_each = each.value.name == "appointment-reminder" && var.sms_api_key_secret_arn != "" ? [1] : []
    content {
      sid       = "SmsApiKey"
      actions   = ["secretsmanager:GetSecretValue"]
      resources = [var.sms_api_key_secret_arn]
    }
  }

  dynamic "statement" {
    for_each = each.value.name == "specialist-notify" ? [1] : []
    content {
      sid       = "PublishClinicianAlerts"
      actions   = ["sns:Publish"]
      resources = [aws_sns_topic.clinician_alerts[each.value.env].arn]
    }
  }
}

resource "aws_iam_role_policy" "function" {
  for_each = local.env_functions
  name     = "least-privilege"
  role     = aws_iam_role.function[each.key].id
  policy   = data.aws_iam_policy_document.function[each.key].json
}

resource "aws_lambda_function" "event" {
  for_each         = local.env_functions
  function_name    = "healthconnect-${each.value.env}-${each.value.name}"
  description      = "Lab 7 consumer of appointment events (${each.value.env})"
  role             = aws_iam_role.function[each.key].arn
  runtime          = "python3.12"
  architectures    = ["arm64"]
  handler          = each.value.fn.handler
  filename         = data.archive_file.functions.output_path
  source_code_hash = data.archive_file.functions.output_base64sha256
  timeout          = 10
  memory_size      = 128

  # Guard against a runaway loop or a flood: never more than 5 at once per function.
  reserved_concurrent_executions = 5

  logging_config {
    log_format = "Text"
    log_group  = aws_cloudwatch_log_group.function[each.key].name
  }

  environment {
    variables = merge(
      {
        ENVIRONMENT       = each.value.env
        IDEMPOTENCY_TABLE = aws_dynamodb_table.processed_events[each.value.env].name
      },
      each.value.name == "appointment-reminder" ? {
        SCHEDULE_GROUP         = aws_scheduler_schedule_group.reminders[each.value.env].name
        SELF_ARN               = "arn:aws:lambda:${var.region}:${data.aws_caller_identity.current.account_id}:function:healthconnect-${each.value.env}-appointment-reminder"
        SCHEDULER_ROLE_ARN     = aws_iam_role.reminder_scheduler[each.value.env].arn
        EVENTS_KMS_KEY_ARN     = aws_kms_key.events.arn
        SMS_PROVIDER           = var.sms_api_key_secret_arn != "" && each.value.env == "production" ? "arkesel" : "log"
        SMS_API_KEY_SECRET_ARN = var.sms_api_key_secret_arn
        SMS_SENDER_ID          = var.sms_sender_id
      } : {},
      each.value.name == "specialist-notify" ? {
        CLINICIAN_TOPIC_ARN = aws_sns_topic.clinician_alerts[each.value.env].arn
      } : {},
    )
  }

  depends_on = [aws_iam_role_policy.function]
}

resource "aws_lambda_event_source_mapping" "event" {
  for_each                = local.env_functions
  function_name           = aws_lambda_function.event[each.key].arn
  event_source_arn        = aws_sqs_queue.events["${each.value.env}/${each.value.fn.consumer}"].arn
  batch_size              = 10
  function_response_types = ["ReportBatchItemFailures"] # only the failed messages are retried

  scaling_config {
    maximum_concurrency = 5
  }
}

# ---- Reminder schedules -----------------------------------------------------------------

resource "aws_scheduler_schedule_group" "reminders" {
  for_each = local.event_envs
  name     = "healthconnect-${each.key}-reminders"
}

data "aws_iam_policy_document" "reminder_scheduler_assume" {
  for_each = local.event_envs
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

resource "aws_iam_role" "reminder_scheduler" {
  for_each           = local.event_envs
  name               = "healthconnect-${each.key}-reminder-scheduler"
  description        = "Lets EventBridge Scheduler start the reminder function when a reminder is due"
  assume_role_policy = data.aws_iam_policy_document.reminder_scheduler_assume[each.key].json
}

data "aws_iam_policy_document" "reminder_scheduler" {
  for_each = local.event_envs
  statement {
    actions   = ["lambda:InvokeFunction"]
    resources = [aws_lambda_function.event["${each.key}/appointment-reminder"].arn]
  }
  statement {
    actions   = ["kms:Decrypt"]
    resources = [aws_kms_key.events.arn]
  }
}

resource "aws_iam_role_policy" "reminder_scheduler" {
  for_each = local.event_envs
  name     = "invoke-reminder"
  role     = aws_iam_role.reminder_scheduler[each.key].id
  policy   = data.aws_iam_policy_document.reminder_scheduler[each.key].json
}

# ---- The API may publish to the appointment topics (one server runs both environments) ---

data "aws_iam_policy_document" "app_events" {
  statement {
    sid       = "PublishAppointmentEvents"
    actions   = ["sns:Publish"]
    resources = [for t in aws_sns_topic.appointment_events : t.arn]
  }
  statement {
    sid       = "EncryptEvents"
    actions   = ["kms:GenerateDataKey", "kms:Decrypt"]
    resources = [aws_kms_key.events.arn]
    condition {
      test     = "StringEquals"
      variable = "kms:ViaService"
      values   = ["sns.${var.region}.amazonaws.com"]
    }
  }
}

resource "aws_iam_role_policy" "app_events" {
  name   = "healthconnect-publish-appointment-events"
  role   = aws_iam_role.app.id
  policy = data.aws_iam_policy_document.app_events.json
}

# ---- Nothing fails silently -------------------------------------------------------------

resource "aws_cloudwatch_metric_alarm" "events_dlq" {
  for_each            = local.consumer_queues
  alarm_name          = "healthconnect-${each.value.env}-${each.value.consumer}-dead-letters"
  alarm_description   = "An appointment event failed three times and is in the ${each.value.consumer} dead-letter queue. Look at the function's logs, fix, then redrive from the SQS console."
  namespace           = "AWS/SQS"
  metric_name         = "ApproximateNumberOfMessagesVisible"
  dimensions          = { QueueName = aws_sqs_queue.events_dlq[each.key].name }
  statistic           = "Maximum"
  period              = 300
  evaluation_periods  = 1
  threshold           = 0
  comparison_operator = "GreaterThanThreshold"
  treat_missing_data  = "notBreaching"
  alarm_actions       = [aws_sns_topic.alerts.arn]
  ok_actions          = [aws_sns_topic.alerts.arn]
}

resource "aws_cloudwatch_metric_alarm" "function_errors" {
  for_each            = local.env_functions
  alarm_name          = "healthconnect-${each.value.env}-${each.value.name}-errors"
  alarm_description   = "The ${each.value.name} function is throwing errors."
  namespace           = "AWS/Lambda"
  metric_name         = "Errors"
  dimensions          = { FunctionName = aws_lambda_function.event[each.key].function_name }
  statistic           = "Sum"
  period              = 300
  evaluation_periods  = 1
  threshold           = 0
  comparison_operator = "GreaterThanThreshold"
  treat_missing_data  = "notBreaching"
  alarm_actions       = [aws_sns_topic.alerts.arn]
}
