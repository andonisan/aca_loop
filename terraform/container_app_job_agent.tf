resource "azurerm_container_app_job" "agent" {
  name                         = "${var.name_prefix}-agent-job"
  location                     = azurerm_resource_group.this.location
  resource_group_name          = azurerm_resource_group.this.name
  container_app_environment_id = azurerm_container_app_environment.this.id
  workload_profile_name        = "Consumption"
  replica_timeout_in_seconds   = var.replica_timeout_seconds

  # Agent runs are not idempotent-retryable: a failed run flips the item to
  # its mapped on_failure_state (handled inside ExecutionService) instead of
  # being retried at the Azure job layer. The storage-queue visibility
  # timeout still covers entrypoint-level crashes, capped by the
  # poison-message guard.
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

      # Bootstraps Orchestrator.ServiceDefaults.AddOptionalAzureKeyVault -- every secret in the
      # vault becomes a configuration key (Vendors--GitHub--PrivateKeyPem -> Vendors:GitHub:PrivateKeyPem).
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
      env {
        name  = "Vendors__GitHub__InstallationId"
        value = var.github_app_installation_id
      }
      env {
        name  = "Vendors__GitHub__BotLogin"
        value = var.github_bot_login
      }
      env {
        name  = "Execution__MaxTurns"
        value = tostring(var.agent_max_turns)
      }
      env {
        name  = "Execution__ReplicaTimeoutSeconds"
        value = tostring(var.replica_timeout_seconds)
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
    }
  }

  depends_on = [
    azurerm_role_assignment.agent_job_queue_processor,
    azurerm_role_assignment.agent_job_kv_secrets_user,
    azurerm_role_assignment.agent_job_scaler_queue_reader,
    azurerm_role_assignment.agent_job_acr_pull,
  ]
}
