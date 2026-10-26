# Lab 6 Part B, encryption in transit. HTTPS without buying a domain: CloudFront gives
# https://<id>.cloudfront.net with AWS's certificate, and http:// is answered with a
# 301 redirect to https:// (the API refuses plain HTTP outright with 403).
#
#   phone --HTTPS--> CloudFront --HTTP + secret header--> load balancer --> app server
#
# The CloudFront -> load balancer leg stays on AWS's network but is not TLS: that needs a
# certificate on the load balancer, which needs a domain. Documented as a known gap.
#
# Caching: nginx sends Cache-Control (hashed /assets/ for a year, index.html and sw.js
# no-cache), so CloudFront follows it. /api/* is never cached and passes every header.

locals {
  cache_disabled            = "4135ea2d-6df8-44a3-9df3-4b5a84be39ad" # Managed-CachingDisabled
  cache_use_origin_headers  = "83da9c7e-98b4-4e11-a168-04f0df8e2c65" # Managed-UseOriginCacheControlHeaders
  origin_all_viewer_no_host = "b689b0a8-53d0-40ab-baf2-68738e2966ac" # Managed-AllViewerExceptHostHeader
  security_headers          = "67f7725c-6f97-4210-82d7-5512b31e9d03" # Managed-SecurityHeadersPolicy (HSTS etc.)
}

resource "aws_cloudfront_distribution" "app" {
  enabled         = true
  comment         = "HealthConnect: HTTPS in front of the load balancer"
  is_ipv6_enabled = true
  http_version    = "http2and3"
  # PriceClass_200 includes the edge locations in Africa (Lagos, Johannesburg, Nairobi...).
  price_class = "PriceClass_200"

  origin {
    origin_id   = "alb"
    domain_name = aws_lb.main.dns_name

    custom_header {
      name  = "X-Origin-Verify"
      value = random_password.origin_header.result
    }

    custom_origin_config {
      http_port              = 80
      https_port             = 443
      origin_protocol_policy = "http-only"
      origin_ssl_protocols   = ["TLSv1.2"]
      origin_read_timeout    = 60
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
    response_headers_policy_id = local.security_headers
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
    response_headers_policy_id = local.security_headers
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
}
