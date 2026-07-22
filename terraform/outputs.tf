output "resource_group_name" {
  value = azurerm_resource_group.this.name
}

output "webhook_receiver_fqdn" {
  description = "Set the GitHub App's webhook URL to https://<this>/webhooks/github"
  value       = azurerm_container_app.webhook.ingress[0].fqdn
}

output "agent_job_name" {
  value = azurerm_container_app_job.agent.name
}

output "storage_queue_name" {
  value = azurerm_storage_queue.ai_jobs.name
}

output "key_vault_uri" {
  value = azurerm_key_vault.this.vault_uri
}

output "github_webhook_secret" {
  description = "Paste this into the GitHub App's webhook secret field."
  value       = random_password.github_webhook_secret.result
  sensitive   = true
}
