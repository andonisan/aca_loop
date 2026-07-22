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
    target_port      = 3000
    transport        = "http"

    traffic_weight {
      percentage      = 100
      latest_revision = true
    }
  }

  template {
    min_replicas = 1
    max_replicas = 2

    container {
      name   = "webhook-receiver"
      image  = var.webhook_receiver_image
      cpu    = 0.25
      memory = "0.5Gi"

      env {
        name  = "KEY_VAULT_URI"
        value = azurerm_key_vault.this.vault_uri
      }
      env {
        name  = "STORAGE_ACCOUNT_NAME"
        value = azurerm_storage_account.this.name
      }
      env {
        name  = "QUEUE_NAME"
        value = azurerm_storage_queue.ai_jobs.name
      }
      env {
        name  = "GATE_LABELS"
        value = var.gate_labels
      }
      env {
        name  = "DEFAULT_AGENT_RUNTIME"
        value = var.default_agent_runtime
      }
      env {
        name  = "AGENT_BOT_LOGIN"
        value = var.agent_bot_login
      }
      env {
        name  = "PORT"
        value = "3000"
      }
    }
  }

  depends_on = [
    azurerm_role_assignment.webhook_kv_secrets_user,
    azurerm_role_assignment.webhook_queue_sender,
    azurerm_role_assignment.webhook_acr_pull,
  ]
}
