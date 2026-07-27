variable "name_prefix" {
  type        = string
  description = "Short globally unique prefix for Azure resources."
  default     = "finops"
}

variable "resource_group_name" {
  type        = string
  description = "Resource group for the platform."
  default     = "rg-finops-anomaly"
}

variable "location" {
  type        = string
  description = "Azure region."
  default     = "eastus"
}

variable "billing_scope_resource_id" {
  type        = string
  description = "Subscription, management group, billing account, or enrollment scope for Cost Management Reader."
}

variable "kubernetes_namespace" {
  type        = string
  description = "Namespace that runs the cloud cost anomaly worker."
  default     = "default"
}

variable "kubernetes_service_account_name" {
  type        = string
  description = "Kubernetes service account used by the workload identity binding."
  default     = "cloud-cost-anomaly-worker"
}
