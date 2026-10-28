# Lab 6 Part A: least privilege. The app server runs as its own role, healthconnect-app,
# through an instance profile: no access keys on the server, and nothing borrowed from an
# admin user. The role can do exactly what the patient-record service needs:
#
#   read the database password      secretsmanager:GetSecretValue on that one secret
#   use the data key                 kms:Decrypt / GenerateDataKey, only via Secrets Manager or S3
#   store and fetch patient media    s3:GetObject / PutObject on the media bucket only
#   pull the app images              ECR, HealthConnect repositories only
#   write its logs                   CloudWatch Logs, its own log group only
#   be reached without SSH           AWS's Session Manager policy
#
# Everything else is denied by default: no rds:* (so it cannot delete or modify the database),
# no other buckets or secrets (AgroConnect's included), no IAM. deploy/verify proves both.

data "aws_iam_policy_document" "ec2_assume" {
  statement {
    actions = ["sts:AssumeRole"]
    principals {
      type        = "Service"
      identifiers = ["ec2.amazonaws.com"]
    }
  }
}

resource "aws_iam_role" "app" {
  name                 = "healthconnect-app"
  description          = "HealthConnect patient-record service (least privilege, Lab 6)"
  assume_role_policy   = data.aws_iam_policy_document.ec2_assume.json
  max_session_duration = 3600
}

resource "aws_iam_role_policy_attachment" "ssm" {
  role       = aws_iam_role.app.name
  policy_arn = "arn:aws:iam::aws:policy/AmazonSSMManagedInstanceCore"
}

data "aws_iam_policy_document" "app" {
  statement {
    sid       = "HealthConnectSecretAccess"
    actions   = ["secretsmanager:GetSecretValue", "secretsmanager:DescribeSecret"]
    resources = [aws_db_instance.main.master_user_secret[0].secret_arn]
  }

  statement {
    sid       = "HealthConnectKeyUseViaServicesOnly"
    actions   = ["kms:Decrypt", "kms:GenerateDataKey"]
    resources = [aws_kms_key.data.arn]
    condition {
      test     = "StringEquals"
      variable = "kms:ViaService"
      values = [
        "secretsmanager.${var.region}.amazonaws.com",
        "s3.${var.region}.amazonaws.com",
      ]
    }
  }

  statement {
    sid       = "HealthConnectMediaObjects"
    actions   = ["s3:GetObject", "s3:PutObject"]
    resources = ["${aws_s3_bucket.media.arn}/*"]
  }

  statement {
    sid       = "HealthConnectPullImages"
    actions   = ["ecr:BatchGetImage", "ecr:GetDownloadUrlForLayer", "ecr:BatchCheckLayerAvailability"]
    resources = [for r in aws_ecr_repository.app : r.arn]
  }

  statement {
    sid       = "EcrLoginHasNoResourceScope"
    actions   = ["ecr:GetAuthorizationToken"]
    resources = ["*"]
  }

  statement {
    sid       = "HealthConnectOwnLogs"
    actions   = ["logs:CreateLogStream", "logs:PutLogEvents"]
    resources = ["${aws_cloudwatch_log_group.app.arn}:*"]
  }
}

resource "aws_iam_role_policy" "app" {
  name   = "healthconnect-least-privilege"
  role   = aws_iam_role.app.id
  policy = data.aws_iam_policy_document.app.json
}

resource "aws_iam_instance_profile" "app" {
  name = "healthconnect-app"
  role = aws_iam_role.app.name
}

resource "aws_cloudwatch_log_group" "app" {
  name              = "/healthconnect/app"
  retention_in_days = 30
}

# GitHub Actions -> AWS without stored keys (OIDC). The account already has GitHub's OIDC
# provider (AgroConnect created it), so it is looked up, not created again.
data "aws_iam_openid_connect_provider" "github" {
  url = "https://token.actions.githubusercontent.com"
}

data "aws_iam_policy_document" "github_assume" {
  statement {
    actions = ["sts:AssumeRoleWithWebIdentity"]
    principals {
      type        = "Federated"
      identifiers = [data.aws_iam_openid_connect_provider.github.arn]
    }
    condition {
      test     = "StringEquals"
      variable = "token.actions.githubusercontent.com:aud"
      values   = ["sts.amazonaws.com"]
    }
    # Only this repository's deploy jobs, and only in its two GitHub environments (production
    # needs a reviewer's approval in GitHub; staging deploys from development on its own).
    condition {
      test     = "StringLike"
      variable = "token.actions.githubusercontent.com:sub"
      values = [
        "repo:${var.github_repo}:environment:production",
        "repo:${var.github_repo}:environment:staging",
      ]
    }
  }
}

resource "aws_iam_role" "github_deploy" {
  name               = "healthconnect-github-deploy"
  assume_role_policy = data.aws_iam_policy_document.github_assume.json
}

data "aws_iam_policy_document" "github_deploy" {
  statement {
    sid       = "PushImages"
    actions   = ["ecr:BatchCheckLayerAvailability", "ecr:BatchGetImage", "ecr:GetDownloadUrlForLayer", "ecr:InitiateLayerUpload", "ecr:UploadLayerPart", "ecr:CompleteLayerUpload", "ecr:PutImage"]
    resources = [for r in aws_ecr_repository.app : r.arn]
  }
  statement {
    sid       = "EcrLogin"
    actions   = ["ecr:GetAuthorizationToken"]
    resources = ["*"]
  }
  statement {
    sid       = "RunDeployOnTheAppServerOnly"
    actions   = ["ssm:SendCommand"]
    resources = ["arn:aws:ssm:${var.region}::document/AWS-RunShellScript", aws_instance.app.arn]
  }
  statement {
    sid       = "ReadCommandResults"
    actions   = ["ssm:GetCommandInvocation", "ssm:ListCommandInvocations"]
    resources = ["*"]
  }
  # Find the app server by its Name tag (its id changes if it is ever replaced). Describe
  # calls can't be scoped to one instance.
  statement {
    sid       = "FindTheAppServer"
    actions   = ["ec2:DescribeInstances"]
    resources = ["*"]
  }
  statement {
    sid       = "ReadImageScanResults"
    actions   = ["ecr:DescribeImageScanFindings", "ecr:DescribeImages"]
    resources = [for r in aws_ecr_repository.app : r.arn]
  }
}

resource "aws_iam_role_policy" "github_deploy" {
  name   = "deploy-via-ssm"
  role   = aws_iam_role.github_deploy.id
  policy = data.aws_iam_policy_document.github_deploy.json
}
