# TURN relay for the teleconsult video call (Lab 7 Part C). Phones on mobile data in Ghana sit
# behind carrier-grade NAT, so two of them usually can't reach each other directly and the call
# never starts. coturn relays the audio and video for those calls; when a direct path exists the
# browsers still use it and the relay carries nothing.
#
# One small Graviton instance in a public subnet with an Elastic IP, shared by production and
# staging. coturn runs as a container with the TURN REST scheme ("use-auth-secret"): the API hands
# each call ticket its own login made from a shared secret (CallOptions.ServersFor), so nothing is
# stored on the relay and a login copied from a browser expires. The secret lives in Secrets
# Manager; the relay and the app server each read it with their own role.
#
# Cost: t4g.micro ~$7.88 + public IPv4 $3.65 a month, plus data out ($0.154/GB) only for calls
# that need the relay. turn_enabled = false removes it all.

locals {
  turn_count = var.turn_enabled ? 1 : 0
  turn_ports = { min = 49160, max = 49999 }
}

resource "random_password" "turn" {
  count   = local.turn_count
  length  = 48
  special = false
}

resource "aws_secretsmanager_secret" "turn" {
  count                   = local.turn_count
  name                    = "healthconnect/turn"
  description             = "Shared secret and URLs of the coturn relay for video calls (turn.tf)"
  recovery_window_in_days = 0
}

resource "aws_secretsmanager_secret_version" "turn" {
  count     = local.turn_count
  secret_id = aws_secretsmanager_secret.turn[0].id
  secret_string = jsonencode({
    urls = join(",", [
      "turn:${aws_eip.turn[0].public_ip}:3478?transport=udp",
      "turn:${aws_eip.turn[0].public_ip}:3478?transport=tcp",
    ])
    secret = random_password.turn[0].result
  })
}

resource "aws_eip" "turn" {
  count  = local.turn_count
  domain = "vpc"
  tags   = { Name = "healthconnect-turn" }
}

resource "aws_security_group" "turn" {
  count       = local.turn_count
  name        = "healthconnect-turn"
  description = "TURN relay: STUN/TURN on 3478 and the relay port range, from anywhere"
  vpc_id      = aws_vpc.main.id

  ingress {
    description = "TURN over UDP"
    from_port   = 3478
    to_port     = 3478
    protocol    = "udp"
    cidr_blocks = ["0.0.0.0/0"]
  }

  ingress {
    description = "TURN over TCP, for networks that block UDP"
    from_port   = 3478
    to_port     = 3478
    protocol    = "tcp"
    cidr_blocks = ["0.0.0.0/0"]
  }

  ingress {
    description = "Relayed media"
    from_port   = local.turn_ports.min
    to_port     = local.turn_ports.max
    protocol    = "udp"
    cidr_blocks = ["0.0.0.0/0"]
  }

  egress {
    description = "Relayed media to the phones, and package and image downloads"
    from_port   = 0
    to_port     = 0
    protocol    = "-1"
    cidr_blocks = ["0.0.0.0/0"]
  }

  tags = { Name = "healthconnect-turn" }
}

data "aws_iam_policy_document" "turn_assume" {
  statement {
    actions = ["sts:AssumeRole"]
    principals {
      type        = "Service"
      identifiers = ["ec2.amazonaws.com"]
    }
  }
}

resource "aws_iam_role" "turn" {
  count              = local.turn_count
  name               = "healthconnect-turn"
  assume_role_policy = data.aws_iam_policy_document.turn_assume.json
}

resource "aws_iam_role_policy_attachment" "turn_ssm" {
  count      = local.turn_count
  role       = aws_iam_role.turn[0].name
  policy_arn = "arn:aws:iam::aws:policy/AmazonSSMManagedInstanceCore"
}

resource "aws_iam_role_policy" "turn" {
  count = local.turn_count
  name  = "read-turn-secret"
  role  = aws_iam_role.turn[0].id
  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Sid      = "ReadTurnSecret"
      Effect   = "Allow"
      Action   = ["secretsmanager:GetSecretValue"]
      Resource = aws_secretsmanager_secret.turn[0].arn
    }]
  })
}

resource "aws_iam_instance_profile" "turn" {
  count = local.turn_count
  name  = "healthconnect-turn"
  role  = aws_iam_role.turn[0].name
}

data "aws_ssm_parameter" "al2023_arm64" {
  name = "/aws/service/ami-amazon-linux-latest/al2023-ami-kernel-default-arm64"
}

resource "aws_instance" "turn" {
  count                  = local.turn_count
  ami                    = data.aws_ssm_parameter.al2023_arm64.value
  instance_type          = var.turn_instance_type
  subnet_id              = aws_subnet.public[0].id
  vpc_security_group_ids = [aws_security_group.turn[0].id]
  iam_instance_profile   = aws_iam_instance_profile.turn[0].name

  metadata_options {
    http_tokens = "required"
  }

  root_block_device {
    volume_type = "gp3"
    volume_size = 8
    encrypted   = true
  }

  user_data = templatefile("${path.module}/../turn-boot.sh.tftpl", {
    region    = var.region
    secret_id = aws_secretsmanager_secret.turn[0].arn
    public_ip = aws_eip.turn[0].public_ip
    min_port  = local.turn_ports.min
    max_port  = local.turn_ports.max
  })
  user_data_replace_on_change = true

  tags = { Name = "healthconnect-turn" }

  # The boot script reads the secret, so it must exist first.
  depends_on = [aws_secretsmanager_secret_version.turn]

  lifecycle {
    ignore_changes = [ami]
  }
}

resource "aws_eip_association" "turn" {
  count         = local.turn_count
  instance_id   = aws_instance.turn[0].id
  allocation_id = aws_eip.turn[0].id
}
