resource "azurerm_storage_account" "this" {
  # Storage account names must be 3-24 chars, lowercase letters/numbers only.
  name                     = substr("${replace(var.name_prefix, "-", "")}${random_string.suffix.result}", 0, 24)
  resource_group_name      = azurerm_resource_group.this.name
  location                 = azurerm_resource_group.this.location
  account_tier             = "Standard"
  account_replication_type = "LRS"
  min_tls_version          = "TLS1_2"
  tags                     = var.tags
}

resource "azurerm_storage_queue" "ai_jobs" {
  name                 = "ai-jobs"
  storage_account_name = azurerm_storage_account.this.name
}
