resource "azurerm_container_app" "webhook" {
  name                         = "${var.name_prefix}-webhook"
  container_app_environment_id = azurerm_container_app_environment.this.id
  resource_group_name          = azurerm_resource_group.this.name
  revision_mode                = "Single"
  workload_profile_name        = "Consumption"
  tags                         = var.tags

  identity {
    type         = "UserAssigned"
    identity_ids = [azurerm_user_assigned_identity.webhook.id]
  }

  registry {
    server   = var.container_registry_login_server
    identity = azurerm_user_assigned_identity.webhook.id
  }

  ingress {
    external_enabled = true
    # Matches the Receiver Dockerfile's ASPNETCORE_URLS=http://+:3000.
    target_port = 3000
    transport   = "http"

    traffic_weight {
      percentage      = 100
      latest_revision = true
    }
  }

  template {
    min_replicas = 1
    max_replicas = 2

    container {
      name   = "receiver"
      image  = var.webhook_receiver_image
      cpu    = 0.25
      memory = "0.5Gi"

      # Bootstraps Orchestrator.ServiceDefaults.AddOptionalAzureKeyVault -- every secret in the
      # vault becomes a configuration key (Vendors--GitHub--WebhookSecret -> Vendors:GitHub:WebhookSecret).
      env {
        name  = "KEY_VAULT_URI"
        value = azurerm_key_vault.this.vault_uri
      }
      env {
        name  = "Queue__AccountName"
        value = azurerm_storage_account.this.name
      }
      env {
        name  = "Queue__QueueName"
        value = azurerm_storage_queue.ai_jobs.name
      }
      # Non-secret; the App private key and webhook secret come from Key Vault only.
      env {
        name  = "Vendors__GitHub__InstallationId"
        value = var.github_app_installation_id
      }
      env {
        name  = "Vendors__GitHub__BotLogin"
        value = var.github_bot_login
      }

      dynamic "env" {
        for_each = var.enable_gitlab ? [1] : []
        content {
          name  = "Vendors__GitLab__BotUsername"
          value = var.gitlab_bot_username
        }
      }
      dynamic "env" {
        for_each = var.enable_gitlab ? [1] : []
        content {
          name  = "Vendors__GitLab__BaseUrl"
          value = var.gitlab_base_url
        }
      }

      dynamic "env" {
        for_each = var.enable_jira ? [1] : []
        content {
          name  = "Vendors__Jira__BaseUrl"
          value = var.jira_base_url
        }
      }
      dynamic "env" {
        for_each = var.enable_jira ? [1] : []
        content {
          name  = "Vendors__Jira__Deployment"
          value = var.jira_deployment
        }
      }
      dynamic "env" {
        for_each = var.enable_jira ? [1] : []
        content {
          name  = "Vendors__Jira__AccountEmail"
          value = var.jira_account_email
        }
      }
      dynamic "env" {
        for_each = var.enable_jira ? [1] : []
        content {
          name  = "Vendors__Jira__BotAccountId"
          value = var.jira_bot_account_id
        }
      }

      dynamic "env" {
        for_each = var.enable_azure_devops ? [1] : []
        content {
          name  = "Vendors__AzureDevOps__Organization"
          value = var.azure_devops_organization
        }
      }
      dynamic "env" {
        for_each = var.enable_azure_devops ? [1] : []
        content {
          name  = "Vendors__AzureDevOps__BotIdentity"
          value = var.azure_devops_bot_identity
        }
      }
      dynamic "env" {
        for_each = var.enable_azure_devops ? [1] : []
        content {
          name  = "Vendors__AzureDevOps__WebhookUsername"
          value = var.azure_devops_webhook_username
        }
      }
    }
  }

  depends_on = [
    azurerm_role_assignment.webhook_kv_secrets_user,
    azurerm_role_assignment.webhook_queue_sender,
    azurerm_role_assignment.webhook_acr_pull,
  ]
}
