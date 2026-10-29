# An internal load balancer in the private app subnets, in front of the app server.
#
# It has no public address: CloudFront reaches it through a VPC origin (cloudfront.tf), and
# its security group accepts only CloudFront (network.tf). So the HTTPS front door can't be
# bypassed, without the shared-secret header the public version needed, and without paying
# for public IPv4 addresses.
#
# Two environments share the one server, so staging costs nothing extra:
#   production  port 80    the demo URL, deployed from main
#   staging     port 8081  the team's shared test URL, deployed from development
# The staging CloudFront distribution adds "X-HC-Env: staging"; the listener rule below sends
# those requests to the staging port, and everything else to production.

locals {
  env_ports = { production = 80, staging = 8081 }
}

resource "aws_lb" "main" {
  name               = "healthconnect-int"
  load_balancer_type = "application"
  internal           = true
  security_groups    = [aws_security_group.alb.id]
  subnets            = aws_subnet.app[*].id
  idle_timeout       = 60

  drop_invalid_header_fields = true
}

resource "aws_lb_target_group" "app" {
  for_each             = local.env_ports
  name                 = "healthconnect-${each.key}"
  port                 = each.value
  protocol             = "HTTP"
  vpc_id               = aws_vpc.main.id
  deregistration_delay = 30

  # nginx answers /health itself, so the server counts as up even before the API exists;
  # the API's own health is checked by the deploy (deploy/remote-deploy.sh).
  health_check {
    path                = "/health"
    matcher             = "200"
    interval            = 15
    timeout             = 5
    healthy_threshold   = 2
    unhealthy_threshold = 3
  }
}

resource "aws_lb_target_group_attachment" "app" {
  for_each         = local.env_ports
  target_group_arn = aws_lb_target_group.app[each.key].arn
  target_id        = aws_instance.app.id
  port             = each.value
}

resource "aws_lb_listener" "http" {
  load_balancer_arn = aws_lb.main.arn
  port              = 80
  protocol          = "HTTP"

  default_action {
    type             = "forward"
    target_group_arn = aws_lb_target_group.app["production"].arn
  }
}

resource "aws_lb_listener_rule" "staging" {
  listener_arn = aws_lb_listener.http.arn
  priority     = 10

  condition {
    http_header {
      http_header_name = "X-HC-Env"
      values           = ["staging"]
    }
  }

  action {
    type             = "forward"
    target_group_arn = aws_lb_target_group.app["staging"].arn
  }
}
