resource "azurerm_container_app_job" "agent" {
  name                         = "${var.name_prefix}-agent-job"
  location                     = azurerm_resource_group.this.location
  resource_group_name          = azurerm_resource_group.this.name
  container_app_environment_id = azurerm_container_app_environment.this.id
  workload_profile_name        = "Consumption"
  replica_timeout_in_seconds   = var.replica_timeout_seconds

  # Agent runs are not idempotent-retryable: a failed run flips the issue to
  # status:blocked (handled inside the entrypoint) instead of being retried
  # at the Azure job layer. The storage-queue visibility timeout still
  # covers entrypoint-level crashes, capped by the poison-message guard.
  replica_retry_limit = 0

  tags = var.tags

  identity {
    type         = "UserAssigned"
    identity_ids = [azurerm_user_assigned_identity.agent_job.id]
  }

  registry {
    server   = var.container_registry_login_server
    identity = azurerm_user_assigned_identity.agent_job.id
  }

  event_trigger_config {
    parallelism              = 1
    replica_completion_count = 1

    scale {
      min_executions              = 0
      max_executions              = var.max_concurrent_agents
      polling_interval_in_seconds = var.queue_polling_interval_seconds

      rules {
        name             = "queue-scaler"
        custom_rule_type = "azure-queue"
        metadata = {
          queueName   = azurerm_storage_queue.ai_jobs.name
          accountName = azurerm_storage_account.this.name
          # Target 1 agent execution per queued work item.
          queueLength = "1"
        }
        # Managed-identity auth for the scale rule itself -- no storage
        # connection string secret needed anywhere.
        identity_id = azurerm_user_assigned_identity.agent_job.id
      }
    }
  }

  template {
    container {
      name   = "agent"
      image  = var.agent_image
      cpu    = 1.0
      memory = "2Gi"

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
        name  = "GITHUB_APP_INSTALLATION_ID"
        value = var.github_app_installation_id
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
        name  = "AGENT_MAX_TURNS"
        value = tostring(var.agent_max_turns)
      }
      env {
        name  = "REPLICA_TIMEOUT_SECONDS"
        value = tostring(var.replica_timeout_seconds)
      }
    }
  }

  depends_on = [
    azurerm_role_assignment.agent_job_queue_processor,
    azurerm_role_assignment.agent_job_kv_secrets_user,
    azurerm_role_assignment.agent_job_scaler_queue_reader,
    azurerm_role_assignment.agent_job_acr_pull,
  ]
}
