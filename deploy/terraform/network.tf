# HealthConnect's own network, the Week 5 pattern copied from AgroConnect, across two zones:
#
#   public subnets   the fck-nat instance only; the one thing with a public IP
#   app subnets      the internal load balancer and the app server; outbound through fck-nat
#   db subnets       RDS; no route to the internet at all
#
# The way in is CloudFront (HTTPS) -> load balancer -> app server, and the load balancer is
# internal: CloudFront reaches it through a VPC origin (cloudfront.tf), so it has no public
# address at all and nobody can skip HTTPS by calling it directly.

data "aws_availability_zones" "available" {
  state = "available"
}

locals {
  azs = slice(data.aws_availability_zones.available.names, 0, 2)
}

resource "aws_vpc" "main" {
  cidr_block           = var.vpc_cidr
  enable_dns_support   = true
  enable_dns_hostnames = true
  tags                 = { Name = "healthconnect" }
}

resource "aws_internet_gateway" "main" {
  vpc_id = aws_vpc.main.id
  tags   = { Name = "healthconnect" }
}

resource "aws_subnet" "public" {
  count                   = 2
  vpc_id                  = aws_vpc.main.id
  availability_zone       = local.azs[count.index]
  cidr_block              = cidrsubnet(var.vpc_cidr, 8, count.index)
  map_public_ip_on_launch = false
  tags                    = { Name = "healthconnect-public-${local.azs[count.index]}" }
}

resource "aws_subnet" "app" {
  count             = 2
  vpc_id            = aws_vpc.main.id
  availability_zone = local.azs[count.index]
  cidr_block        = cidrsubnet(var.vpc_cidr, 8, 10 + count.index)
  tags              = { Name = "healthconnect-app-${local.azs[count.index]}" }
}

resource "aws_subnet" "db" {
  count             = 2
  vpc_id            = aws_vpc.main.id
  availability_zone = local.azs[count.index]
  cidr_block        = cidrsubnet(var.vpc_cidr, 8, 20 + count.index)
  tags              = { Name = "healthconnect-db-${local.azs[count.index]}" }
}

resource "aws_route_table" "public" {
  vpc_id = aws_vpc.main.id
  tags   = { Name = "healthconnect-public" }
}

resource "aws_route" "public_internet" {
  route_table_id         = aws_route_table.public.id
  destination_cidr_block = "0.0.0.0/0"
  gateway_id             = aws_internet_gateway.main.id
}

resource "aws_route_table_association" "public" {
  count          = 2
  subnet_id      = aws_subnet.public[count.index].id
  route_table_id = aws_route_table.public.id
}

# App: the default route (0.0.0.0/0 -> fck-nat) is added by nat.tf.
resource "aws_route_table" "app" {
  vpc_id = aws_vpc.main.id
  tags   = { Name = "healthconnect-app" }
}

resource "aws_route_table_association" "app" {
  count          = 2
  subnet_id      = aws_subnet.app[count.index].id
  route_table_id = aws_route_table.app.id
}

# Database: local traffic only. No internet route, on purpose.
resource "aws_route_table" "db" {
  vpc_id = aws_vpc.main.id
  tags   = { Name = "healthconnect-db" }
}

resource "aws_route_table_association" "db" {
  count          = 2
  subnet_id      = aws_subnet.db[count.index].id
  route_table_id = aws_route_table.db.id
}

# S3 without the internet and without NAT charges (free).
resource "aws_vpc_endpoint" "s3" {
  vpc_id            = aws_vpc.main.id
  service_name      = "com.amazonaws.${var.region}.s3"
  vpc_endpoint_type = "Gateway"
  route_table_ids   = [aws_route_table.app.id]
  tags              = { Name = "healthconnect-s3" }
}

# ---- Security groups: CloudFront -> load balancer -> app server -> database ----

# The addresses CloudFront uses to reach origins, kept up to date by AWS.
data "aws_ec2_managed_prefix_list" "cloudfront" {
  name = "com.amazonaws.global.cloudfront.origin-facing"
}

resource "aws_security_group" "alb" {
  name        = "healthconnect-alb"
  description = "Load balancer: only from CloudFront"
  vpc_id      = aws_vpc.main.id
}

resource "aws_vpc_security_group_ingress_rule" "alb_from_cloudfront" {
  security_group_id = aws_security_group.alb.id
  description       = "CloudFront origin-facing addresses"
  ip_protocol       = "tcp"
  from_port         = 80
  to_port           = 80
  prefix_list_id    = data.aws_ec2_managed_prefix_list.cloudfront.id
}

# Production on port 80, staging on 8081 (alb.tf): both live on the one app server.
resource "aws_vpc_security_group_egress_rule" "alb_to_app" {
  for_each                     = local.env_ports
  security_group_id            = aws_security_group.alb.id
  description                  = "To the app server (${each.key})"
  ip_protocol                  = "tcp"
  from_port                    = each.value
  to_port                      = each.value
  referenced_security_group_id = aws_security_group.app.id
}

resource "aws_security_group" "app" {
  name        = "healthconnect-app"
  description = "App server: port 80 from the load balancer only; no SSH (Session Manager)"
  vpc_id      = aws_vpc.main.id
}

resource "aws_vpc_security_group_ingress_rule" "app_from_alb" {
  for_each                     = local.env_ports
  security_group_id            = aws_security_group.app.id
  description                  = "From the load balancer (${each.key})"
  ip_protocol                  = "tcp"
  from_port                    = each.value
  to_port                      = each.value
  referenced_security_group_id = aws_security_group.alb.id
}

# Outbound from the app server: HTTPS only (images, OS updates, AWS APIs, through fck-nat or
# the S3 endpoint), plus PostgreSQL to the database's security group and nothing else.
resource "aws_vpc_security_group_egress_rule" "app_https" {
  security_group_id = aws_security_group.app.id
  description       = "HTTPS: ECR, S3, Secrets Manager, SSM, CloudWatch, OS packages"
  ip_protocol       = "tcp"
  from_port         = 443
  to_port           = 443
  cidr_ipv4         = "0.0.0.0/0"
}

resource "aws_vpc_security_group_egress_rule" "app_to_db" {
  security_group_id            = aws_security_group.app.id
  description                  = "PostgreSQL to the database only"
  ip_protocol                  = "tcp"
  from_port                    = 5432
  to_port                      = 5432
  referenced_security_group_id = aws_security_group.database.id
}

resource "aws_security_group" "database" {
  name        = "healthconnect-db"
  description = "PostgreSQL only from the app server"
  vpc_id      = aws_vpc.main.id
}

resource "aws_vpc_security_group_ingress_rule" "db_from_app" {
  security_group_id            = aws_security_group.database.id
  description                  = "PostgreSQL from the app server"
  ip_protocol                  = "tcp"
  from_port                    = 5432
  to_port                      = 5432
  referenced_security_group_id = aws_security_group.app.id
}

# The port-80 rules existed before staging; keep them as production's.
moved {
  from = aws_vpc_security_group_egress_rule.alb_to_app
  to   = aws_vpc_security_group_egress_rule.alb_to_app["production"]
}

moved {
  from = aws_vpc_security_group_ingress_rule.app_from_alb
  to   = aws_vpc_security_group_ingress_rule.app_from_alb["production"]
}
