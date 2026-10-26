# One app server in a private subnet, running the frontend (nginx + PWA) and the API as
# containers. One week, one demo: a single server instead of AgroConnect's Auto Scaling groups.
# Reached by the team through Session Manager only (no SSH, no public IP).

data "aws_ssm_parameter" "al2023" {
  name = "/aws/service/ami-amazon-linux-latest/al2023-ami-kernel-default-x86_64"
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
  })
  user_data_replace_on_change = false

  tags = { Name = "healthconnect-app", Environment = "production" }

  # First boot downloads packages, so the NAT route must exist first.
  depends_on = [module.fck_nat]

  lifecycle {
    ignore_changes = [ami] # don't replace the server every time Amazon publishes a new image
  }
}

resource "aws_ec2_instance_state" "app" {
  instance_id = aws_instance.app.id
  state       = var.app_running ? "running" : "stopped"
}
