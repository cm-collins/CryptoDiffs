# Azure Key Vault Usage Guide

## Quick Reference

### Adding a New Secret

```bash
# Easiest way - automatically detects if it's new or existing
./CryptoDiffs/scripts/manage-keyvault.sh add \
  --vault-name cryptodiffs-kv \
  --secret-name YOUR_NEW_SECRET \
  --secret-value "your-secret-value"
```

### Updating an Existing Secret

```bash
# Update an existing secret
./CryptoDiffs/scripts/manage-keyvault.sh update \
  --vault-name cryptodiffs-kv \
  --secret-name EXISTING_SECRET \
  --secret-value "new-value"
```

### Syncing All Secrets from local.settings.json

```bash
# Sync all secrets (adds new, updates existing) - RECOMMENDED
./CryptoDiffs/scripts/manage-keyvault.sh sync --vault-name cryptodiffs-kv
```

## Common Workflows

### Workflow 1: Add a Single New Environment Variable

1. **Add to local.settings.json**:
   ```json
   {
     "Values": {
       "NEW_SETTING": "new-value"
     }
   }
   ```

2. **Push to Key Vault**:
   ```bash
   ./CryptoDiffs/scripts/manage-keyvault.sh add \
     --vault-name cryptodiffs-kv \
     --secret-name NEW_SETTING \
     --secret-value "new-value"
   ```

### Workflow 2: Update an Existing Secret

1. **Update in local.settings.json** (optional, for reference)

2. **Update in Key Vault**:
   ```bash
   ./CryptoDiffs/scripts/manage-keyvault.sh update \
     --vault-name cryptodiffs-kv \
     --secret-name GRAPH_CLIENT_SECRET \
     --secret-value "new-secret-value"
   ```

### Workflow 3: Sync All Changes from local.settings.json

**Best Practice**: After making changes to `local.settings.json`, sync everything:

```bash
./CryptoDiffs/scripts/manage-keyvault.sh sync --vault-name cryptodiffs-kv
```

This will:
- ✅ Add any new secrets that don't exist in Key Vault
- ✅ Update existing secrets with new values
- ✅ Skip runtime-specific settings (AzureWebJobsStorage, FUNCTIONS_WORKER_RUNTIME)
- ✅ Show a summary of what was added/updated

## All Available Commands

| Command | Description | Example |
|---------|-------------|---------|
| `add` | Add a new secret (or update if exists) | `./manage-keyvault.sh add --vault-name kv --secret-name KEY --secret-value "value"` |
| `update` | Update an existing secret | `./manage-keyvault.sh update --vault-name kv --secret-name KEY --secret-value "value"` |
| `set` | Set/update a secret (same as add/update) | `./manage-keyvault.sh set --vault-name kv --secret-name KEY --secret-value "value"` |
| `get` | Get a secret value | `./manage-keyvault.sh get --vault-name kv --secret-name KEY` |
| `list` | List all secrets | `./manage-keyvault.sh list --vault-name kv` |
| `delete` | Delete a secret | `./manage-keyvault.sh delete --vault-name kv --secret-name KEY` |
| `sync` | Sync all from local.settings.json | `./manage-keyvault.sh sync --vault-name kv` |
| `setup-all` | Initial setup (overwrites all) | `./manage-keyvault.sh setup-all --vault-name kv` |

## Important Notes

1. **Secret Name Conversion**: Secret names with underscores (e.g., `GRAPH_CLIENT_ID`) are automatically converted to hyphens (e.g., `GRAPH-CLIENT-ID`) in Key Vault, as Azure Key Vault only allows alphanumeric characters and hyphens.

2. **The `KeyVaultService` automatically handles this conversion**, so you can use underscores in your code and it will work correctly.

3. **Use `sync` for regular updates**: The `sync` command is recommended for keeping Key Vault in sync with your `local.settings.json` file.

4. **Use `add` for quick additions**: The `add` command automatically detects if a secret is new or existing, making it the easiest way to add or update a single secret.

## Examples

### Example 1: Add a New Database Connection String

```bash
./CryptoDiffs/scripts/manage-keyvault.sh add \
  --vault-name cryptodiffs-kv \
  --secret-name DATABASE_CONNECTION_STRING \
  --secret-value "Server=myserver;Database=mydb;..."
```

### Example 2: Update Email Configuration

```bash
./CryptoDiffs/scripts/manage-keyvault.sh update \
  --vault-name cryptodiffs-kv \
  --secret-name MAIL_TO \
  --secret-value "new-email@example.com"
```

### Example 3: Sync After Editing local.settings.json

```bash
# 1. Edit CryptoDiffs/local.settings.json
# 2. Run sync
./CryptoDiffs/scripts/manage-keyvault.sh sync --vault-name cryptodiffs-kv
```

### Example 4: Check What Secrets Are in Key Vault

```bash
./CryptoDiffs/scripts/manage-keyvault.sh list --vault-name cryptodiffs-kv
```

### Example 5: Verify a Secret Value

```bash
./CryptoDiffs/scripts/manage-keyvault.sh get \
  --vault-name cryptodiffs-kv \
  --secret-name GRAPH_CLIENT_ID
```

