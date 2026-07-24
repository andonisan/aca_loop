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

output "gitlab_webhook_secret" {
  description = "Paste this into the GitLab project/group webhook's secret token field. Empty when enable_gitlab = false."
  value       = var.enable_gitlab ? random_password.gitlab_webhook_secret[0].result : null
  sensitive   = true
}

output "jira_webhook_secret" {
  description = "Paste this into the Jira webhook's secret field so Atlassian signs bodies with it. The adapter rejects unsigned requests, so leaving the webhook's secret blank breaks dispatch rather than merely weakening it. Empty when enable_jira = false."
  value       = var.enable_jira ? random_password.jira_webhook_secret[0].result : null
  sensitive   = true
}

output "azure_devops_webhook_password" {
  description = "Paste this, paired with var.azure_devops_webhook_username, into the Azure DevOps Service Hooks subscription's Basic authentication fields. Empty when enable_azure_devops = false."
  value       = var.enable_azure_devops ? random_password.azure_devops_webhook_password[0].result : null
  sensitive   = true
}
