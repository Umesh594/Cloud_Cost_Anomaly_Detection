output "adx_cluster_uri" {
  value = azurerm_kusto_cluster.adx.uri
}

output "aks_cluster_name" {
  value = azurerm_kubernetes_cluster.aks.name
}

output "worker_client_id" {
  value = azurerm_user_assigned_identity.worker.client_id
}
