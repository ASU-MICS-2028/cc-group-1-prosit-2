# HTTPS without buying a domain: CloudFront gives https://<id>.cloudfront.net with AWS's
# certificate, and http:// is answered with a 301 redirect to https:// (the API refuses plain
# HTTP outright with 403).
#
#   phone --HTTPS--> CloudFront --VPC origin (private)--> internal load balancer --> app server
#
# The VPC origin is a CloudFront-managed network interface inside our VPC, so the load
# balancer needs no public address. The CloudFront -> load balancer leg is HTTP on AWS's
# private network; TLS on that leg needs a certificate, which needs a domain (known gap).
#
# Two distributions, one per environment (alb.tf): production and staging. Staging adds a
# header that the load balancer routes on, and tells search engines not to index it.
#
# Caching: nginx sends Cache-Control (hashed /assets/ for a year, index.html and sw.js
# no-cache), so CloudFront follows it. /api/* is never cached and passes every header.

locals {
  cache_disabled            = "4135ea2d-6df8-44a3-9df3-4b5a84be39ad" # Managed-CachingDisabled
  cache_use_origin_headers  = "83da9c7e-98b4-4e11-a168-04f0df8e2c65" # Managed-UseOriginCacheControlHeaders
  origin_all_viewer_no_host = "b689b0a8-53d0-40ab-baf2-68738e2966ac" # Managed-AllViewerExceptHostHeader
  security_headers          = "67f7725c-6f97-4210-82d7-5512b31e9d03" # Managed-SecurityHeadersPolicy (HSTS etc.)
}

resource "aws_cloudfront_vpc_origin" "alb" {
  vpc_origin_endpoint_config {
    name                   = "healthconnect-alb"
    arn                    = aws_lb.main.arn
    http_port              = 80
    https_port             = 443
    origin_protocol_policy = "http-only"

    origin_ssl_protocols {
      items    = ["TLSv1.2"]
      quantity = 1
    }
  }
}

# Staging only: keep it out of search results.
resource "aws_cloudfront_response_headers_policy" "staging" {
  name    = "healthconnect-staging"
  comment = "Security headers plus noindex for the staging site"

  security_headers_config {
    strict_transport_security {
      access_control_max_age_sec = 31536000
      include_subdomains         = false
      override                   = true
    }
    content_type_options {
      override = true
    }
    frame_options {
      frame_option = "SAMEORIGIN"
      override     = true
    }
    referrer_policy {
      referrer_policy = "strict-origin-when-cross-origin"
      override        = true
    }
    xss_protection {
      protection = true
      mode_block = true
      override   = true
    }
  }

  custom_headers_config {
    items {
      header   = "X-Robots-Tag"
      value    = "noindex, nofollow"
      override = true
    }
  }
}

resource "aws_cloudfront_distribution" "app" {
  for_each = local.env_ports

  enabled         = true
  comment         = "HealthConnect ${each.key}"
  is_ipv6_enabled = true
  http_version    = "http2and3"
  # PriceClass_200 includes the edge locations in Africa (Lagos, Johannesburg, Nairobi...).
  price_class = "PriceClass_200"

  origin {
    origin_id   = "alb"
    domain_name = aws_lb.main.dns_name

    vpc_origin_config {
      vpc_origin_id            = aws_cloudfront_vpc_origin.alb.id
      origin_keepalive_timeout = 5
      origin_read_timeout      = 60
    }

    dynamic "custom_header" {
      for_each = each.key == "staging" ? [1] : []
      content {
        name  = "X-HC-Env"
        value = "staging"
      }
    }
  }

  # The app: pages, scripts, icons.
  default_cache_behavior {
    target_origin_id           = "alb"
    viewer_protocol_policy     = "redirect-to-https"
    allowed_methods            = ["GET", "HEAD", "OPTIONS"]
    cached_methods             = ["GET", "HEAD"]
    compress                   = true
    cache_policy_id            = local.cache_use_origin_headers
    origin_request_policy_id   = local.origin_all_viewer_no_host
    response_headers_policy_id = each.key == "staging" ? aws_cloudfront_response_headers_policy.staging.id : local.security_headers
  }

  # The API: never cached, never over plain HTTP (403 instead of a redirect, so a client that
  # sends patient data over http:// fails loudly instead of having it redirected).
  ordered_cache_behavior {
    path_pattern               = "/api/*"
    target_origin_id           = "alb"
    viewer_protocol_policy     = "https-only"
    allowed_methods            = ["GET", "HEAD", "OPTIONS", "PUT", "POST", "PATCH", "DELETE"]
    cached_methods             = ["GET", "HEAD"]
    compress                   = true
    cache_policy_id            = local.cache_disabled
    origin_request_policy_id   = local.origin_all_viewer_no_host
    response_headers_policy_id = each.key == "staging" ? aws_cloudfront_response_headers_policy.staging.id : local.security_headers
  }

  ordered_cache_behavior {
    path_pattern             = "/health"
    target_origin_id         = "alb"
    viewer_protocol_policy   = "redirect-to-https"
    allowed_methods          = ["GET", "HEAD"]
    cached_methods           = ["GET", "HEAD"]
    cache_policy_id          = local.cache_disabled
    origin_request_policy_id = local.origin_all_viewer_no_host
  }

  restrictions {
    geo_restriction {
      restriction_type = "none"
    }
  }

  viewer_certificate {
    cloudfront_default_certificate = true
  }

  tags = { Environment = each.key }
}

# The production distribution existed before staging: keep it (and its URL, which the Lab 6
# report quotes) instead of replacing it.
moved {
  from = aws_cloudfront_distribution.app
  to   = aws_cloudfront_distribution.app["production"]
}
