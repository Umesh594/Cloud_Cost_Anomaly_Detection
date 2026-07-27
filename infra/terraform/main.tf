terraform {
  required_version = ">= 1.7.0"

  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 4.0"
    }
    azuread = {
      source  = "hashicorp/azuread"
      version = "~> 3.0"
    }
  }
}

provider "azurerm" {
  features {}
}

resource "azurerm_resource_group" "platform" {
  name     = var.resource_group_name
  location = var.location

  tags = local.tags
}

resource "azurerm_kubernetes_cluster" "aks" {
  name                = "${var.name_prefix}-aks"
  location            = azurerm_resource_group.platform.location
  resource_group_name = azurerm_resource_group.platform.name
  dns_prefix          = "${var.name_prefix}-finops"

  oidc_issuer_enabled       = true
  workload_identity_enabled = true

  default_node_pool {
    name       = "system"
    node_count = 2
    vm_size    = "Standard_D4s_v5"
  }

  identity {
    type = "SystemAssigned"
  }

  tags = local.tags
}

resource "azurerm_kusto_cluster" "adx" {
  name                = "${var.name_prefix}adx"
  location            = azurerm_resource_group.platform.location
  resource_group_name = azurerm_resource_group.platform.name
  sku {
    name     = "Dev(No SLA)_Standard_E2a_v4"
    capacity = 1
  }

  identity {
    type = "SystemAssigned"
  }

  tags = local.tags
}

resource "azurerm_kusto_database" "cloud_cost" {
  name                = "CloudCost"
  resource_group_name = azurerm_resource_group.platform.name
  location            = azurerm_resource_group.platform.location
  cluster_name        = azurerm_kusto_cluster.adx.name
  hot_cache_period    = "P7D"
  soft_delete_period  = "P365D"
}

resource "azurerm_user_assigned_identity" "worker" {
  name                = "${var.name_prefix}-worker-mi"
  location            = azurerm_resource_group.platform.location
  resource_group_name = azurerm_resource_group.platform.name

  tags = local.tags
}

resource "azurerm_federated_identity_credential" "worker" {
  name                = "${var.name_prefix}-worker-fic"
  resource_group_name = azurerm_resource_group.platform.name
  parent_id           = azurerm_user_assigned_identity.worker.id
  audience            = ["api://AzureADTokenExchange"]
  issuer              = azurerm_kubernetes_cluster.aks.oidc_issuer_url
  subject             = "system:serviceaccount:${var.kubernetes_namespace}:${var.kubernetes_service_account_name}"
}

resource "azurerm_role_assignment" "cost_reader" {
  scope                = var.billing_scope_resource_id
  role_definition_name = "Cost Management Reader"
  principal_id         = azurerm_user_assigned_identity.worker.principal_id
}

resource "azurerm_role_assignment" "adx_contributor" {
  scope                = azurerm_kusto_cluster.adx.id
  role_definition_name = "Contributor"
  principal_id         = azurerm_user_assigned_identity.worker.principal_id
}
