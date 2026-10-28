variable "region" {
  description = "AWS region: Cape Town, the closest AWS region to Ghana (same as AgroConnect)."
  type        = string
  default     = "af-south-1"
}

variable "vpc_cidr" {
  description = "Address range of the HealthConnect network (AgroConnect uses 10.20.0.0/16)."
  type        = string
  default     = "10.30.0.0/16"
}

variable "app_instance_type" {
  description = "The app server. Graviton (t4g) by default; CI builds arm64 images to match."
  type        = string
  default     = "t4g.small"
}

variable "app_running" {
  description = "false stops paying for the app server between demos (the instance is stopped, not destroyed)."
  type        = bool
  default     = true
}

variable "nat_instance_type" {
  description = "Size of the fck-nat instance that gives the private server outbound internet."
  type        = string
  default     = "t4g.nano"
}

variable "rds_instance_class" {
  description = "Graviton db.t4g.micro: 12% cheaper than db.t3.micro in af-south-1."
  type        = string
  default     = "db.t4g.micro"
}

variable "alert_emails" {
  description = "Who receives alarms (site down, errors, database full). Defaults to budget_emails."
  type        = list(string)
  default     = []
}

variable "nightly_stop" {
  description = "Stop the app server, NAT and database every night (save ~35%). Off by default: turn on once the demo is recorded."
  type        = bool
  default     = false
}

variable "nightly_stop_hours" {
  description = "When the nightly stop runs, Ghana time (GMT): stop at the first hour, start at the second."
  type        = object({ stop = number, start = number })
  default     = { stop = 23, start = 7 }
}

variable "budget_limit_usd" {
  description = "Monthly cost budget. Alerts at 50%, 80%, 100% actual and 100% forecast."
  type        = number
  default     = 20
}

variable "budget_emails" {
  description = "Who receives budget alerts."
  type        = list(string)
}

variable "github_repo" {
  description = "Repository allowed to deploy through GitHub OIDC."
  type        = string
  default     = "ASU-MICS-2028/cc-group-1-prosit-2"
}
