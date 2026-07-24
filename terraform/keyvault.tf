resource "azurerm_key_vault" "this" {
  # Key Vault names must be 3-24 chars, alphanumeric/hyphens, start with a letter.
  name                       = substr("${var.name_prefix}-kv-${random_string.suffix.result}", 0, 24)
  location                   = azurerm_resource_group.this.location
  resource_group_name        = azurerm_resource_group.this.name
  tenant_id                  = data.azurerm_client_config.current.tenant_id
  sku_name                   = "standard"
  rbac_authorization_enabled = true
  purge_protection_enabled   = false
  soft_delete_retention_days = 7
  tags                       = var.tags
}

# GitHub requires the webhook secret up front when configuring the App, so
# Terraform generates it and surfaces it via an output -- copy it into the
# GitHub App's webhook settings during the manual setup step.
resource "random_password" "github_webhook_secret" {
  length  = 48
  special = false
}

# Same rationale as github_webhook_secret above -- GitLab's webhook config UI
# also wants the secret token value up front.
resource "random_password" "gitlab_webhook_secret" {
  count   = var.enable_gitlab ? 1 : 0
  length  = 48
  special = false
}

# Azure DevOps Service Hooks authenticate via HTTP Basic auth rather than a signed/shared secret
# header -- Terraform generates the password half and surfaces it via an output, paired with
# var.azure_devops_webhook_username, to paste into the subscription's Basic authentication fields.
resource "random_password" "azure_devops_webhook_password" {
  count   = var.enable_azure_devops ? 1 : 0
  length  = 48
  special = false
}

# Both the receiver and the agent job fetch secrets from this vault
# themselves at runtime via their managed identity (Orchestrator.ServiceDefaults'
# AddOptionalAzureKeyVault, keyed on the KEY_VAULT_URI env var) -- nothing is
# wired through Container Apps' native secret bindings, so there's no
# apply-order dependency on the secrets pushed out-of-band and never
# referenced from Terraform state. Secret names use "--" where the .NET
# configuration key has a ":" (Key Vault names can't contain colons):
#   az keyvault secret set --name Vendors--GitHub--PrivateKeyPem --file key.pem ...
#   az keyvault secret set --name Vendors--GitLab--AccessToken --value <token>   (if enable_gitlab)
#   az keyvault secret set --name Vendors--AzureDevOps--PersonalAccessToken --value <pat>   (if enable_azure_devops)
resource "azurerm_key_vault_secret" "github_webhook_secret" {
  name         = "Vendors--GitHub--WebhookSecret"
  value        = random_password.github_webhook_secret.result
  key_vault_id = azurerm_key_vault.this.id

  depends_on = [time_sleep.kv_rbac_propagation]
}

resource "azurerm_key_vault_secret" "github_app_id" {
  name         = "Vendors--GitHub--AppId"
  value        = var.github_app_id
  key_vault_id = azurerm_key_vault.this.id

  depends_on = [time_sleep.kv_rbac_propagation]
}

resource "azurerm_key_vault_secret" "gitlab_webhook_secret" {
  count        = var.enable_gitlab ? 1 : 0
  name         = "Vendors--GitLab--WebhookSecret"
  value        = random_password.gitlab_webhook_secret[0].result
  key_vault_id = azurerm_key_vault.this.id

  depends_on = [time_sleep.kv_rbac_propagation]
}

resource "azurerm_key_vault_secret" "azure_devops_webhook_password" {
  count        = var.enable_azure_devops ? 1 : 0
  name         = "Vendors--AzureDevOps--WebhookPassword"
  value        = random_password.azure_devops_webhook_password[0].result
  key_vault_id = azurerm_key_vault.this.id

  depends_on = [time_sleep.kv_rbac_propagation]
}

# RBAC-authorized vaults grant the Terraform caller no data-plane access by
# default; this lets `terraform apply` itself write the secrets above.
resource "azurerm_role_assignment" "terraform_deployer_kv_admin" {
  scope                = azurerm_key_vault.this.id
  role_definition_name = "Key Vault Secrets Officer"
  principal_id         = data.azurerm_client_config.current.object_id
}

# Azure RBAC role assignments are eventually consistent; without this,
# the secret writes above routinely 403 on a fresh vault.
resource "time_sleep" "kv_rbac_propagation" {
  depends_on      = [azurerm_role_assignment.terraform_deployer_kv_admin]
  create_duration = "30s"
}
