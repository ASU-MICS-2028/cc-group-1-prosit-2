# One app server in a private subnet, running the frontend (nginx + PWA) and the API as
# containers, for both environments (production on :80, staging on :8081). One week, one
# demo: a single server instead of AgroConnect's Auto Scaling groups. Reached by the team
# through Session Manager only (no SSH, no public IP).
#
# Graviton (arm64) by default: t4g.small is 20% cheaper than t3.small in af-south-1 for the
# same 2 vCPU / 2 GB, and CI builds arm64 images natively on GitHub's arm runners.

locals {
  # t4g, m7g, c7g... are arm64 (Graviton); everything else here is x86.
  app_arch = can(regex("^[a-z]+[0-9]+g[a-z]*\\.", var.app_instance_type)) ? "arm64" : "x86_64"
}

data "aws_ssm_parameter" "al2023" {
  name = "/aws/service/ami-amazon-linux-latest/al2023-ami-kernel-default-${local.app_arch}"
}

# Changing CPU architecture needs a new server (the image is per architecture).
resource "terraform_data" "app_arch" {
  input = local.app_arch
}

resource "aws_instance" "app" {
  ami                    = data.aws_ssm_parameter.al2023.value
  instance_type          = var.app_instance_type
  subnet_id              = aws_subnet.app[0].id
  vpc_security_group_ids = [aws_security_group.app.id]
  iam_instance_profile   = aws_iam_instance_profile.app.name

  # IMDSv2 only: a request forgery bug in the app can't read the role's credentials.
  metadata_options {
    http_tokens                 = "required"
    http_put_response_hop_limit = 2 # containers on the host still reach it
  }

  root_block_device {
    volume_type = "gp3"
    volume_size = 20
    encrypted   = true
    kms_key_id  = aws_kms_key.data.arn
  }

  user_data = templatefile("${path.module}/../server-boot.sh.tftpl", {
    region       = var.region
    db_host      = aws_db_instance.main.address
    db_secret    = aws_db_instance.main.master_user_secret[0].secret_arn
    media_bucket = aws_s3_bucket.media.bucket
    registry     = local.ecr_registry
    log_group    = aws_cloudwatch_log_group.app.name
  })
  user_data_replace_on_change = false

  tags = { Name = "healthconnect-app", Environment = "production" }

  # First boot downloads packages, so the NAT route must exist first.
  depends_on = [module.fck_nat]

  lifecycle {
    ignore_changes       = [ami] # don't replace the server every time Amazon publishes a new image
    replace_triggered_by = [terraform_data.app_arch]
  }
}

resource "aws_ec2_instance_state" "app" {
  instance_id = aws_instance.app.id
  state       = var.app_running ? "running" : "stopped"
}
