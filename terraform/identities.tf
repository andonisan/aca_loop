resource "azurerm_user_assigned_identity" "webhook" {
  name                = "${var.name_prefix}-webhook-id"
  location            = azurerm_resource_group.this.location
  resource_group_name = azurerm_resource_group.this.name
  tags                = var.tags
}

resource "azurerm_user_assigned_identity" "agent_job" {
  name                = "${var.name_prefix}-agent-job-id"
  location            = azurerm_resource_group.this.location
  resource_group_name = azurerm_resource_group.this.name
  tags                = var.tags
}

# webhook-receiver: reads only the webhook secret, and only ever enqueues.
resource "azurerm_role_assignment" "webhook_kv_secrets_user" {
  scope                = azurerm_key_vault_secret.github_webhook_secret.resource_versionless_id
  role_definition_name = "Key Vault Secrets User"
  principal_id         = azurerm_user_assigned_identity.webhook.principal_id
}

resource "azurerm_role_assignment" "webhook_queue_sender" {
  scope                = azurerm_storage_account.this.id
  role_definition_name = "Storage Queue Data Message Sender"
  principal_id         = azurerm_user_assigned_identity.webhook.principal_id
}

# agent-job: dequeues/deletes queue messages and reads the App private key,
# app id, and LLM provider keys at run time. Vault-scoped because the set of
# provider-key secrets is open-ended (opencode providers are optional and
# pushed out-of-band).
resource "azurerm_role_assignment" "agent_job_queue_processor" {
  scope                = azurerm_storage_account.this.id
  role_definition_name = "Storage Queue Data Message Processor"
  principal_id         = azurerm_user_assigned_identity.agent_job.principal_id
}

resource "azurerm_role_assignment" "agent_job_kv_secrets_user" {
  scope                = azurerm_key_vault.this.id
  role_definition_name = "Key Vault Secrets User"
  principal_id         = azurerm_user_assigned_identity.agent_job.principal_id
}

# The KEDA azure-queue scale rule itself also authenticates as this
# identity (see container_app_job_agent.tf's `identity_id`), so it needs
# read access to the queue length independent of the container's own calls.
resource "azurerm_role_assignment" "agent_job_scaler_queue_reader" {
  scope                = azurerm_storage_account.this.id
  role_definition_name = "Storage Queue Data Reader"
  principal_id         = azurerm_user_assigned_identity.agent_job.principal_id
}

resource "azurerm_role_assignment" "webhook_acr_pull" {
  scope                = var.container_registry_id
  role_definition_name = "AcrPull"
  principal_id         = azurerm_user_assigned_identity.webhook.principal_id
}

resource "azurerm_role_assignment" "agent_job_acr_pull" {
  scope                = var.container_registry_id
  role_definition_name = "AcrPull"
  principal_id         = azurerm_user_assigned_identity.agent_job.principal_id
}
