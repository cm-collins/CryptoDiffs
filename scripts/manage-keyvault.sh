#!/bin/bash

# Azure Key Vault Management Script for CryptoDiffs
# This script helps manage secrets in Azure Key Vault for the CryptoDiffs application
# Usage: ./manage-keyvault.sh [command] [options]

set -e

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Default values
KEY_VAULT_NAME=""
RESOURCE_GROUP=""
LOCATION="eastus"
SUBSCRIPTION_ID=""

# Function to print colored output
print_info() {
    echo -e "${BLUE}[INFO]${NC} $1"
}

print_success() {
    echo -e "${GREEN}[SUCCESS]${NC} $1"
}

print_warning() {
    echo -e "${YELLOW}[WARNING]${NC} $1"
}

print_error() {
    echo -e "${RED}[ERROR]${NC} $1"
}

# Function to show usage
show_usage() {
    cat << EOF
Azure Key Vault Management Script for CryptoDiffs

Usage: $0 [command] [options]

Commands:
    login                    Login to Azure CLI
    create                   Create a new Key Vault
    set                      Set/update a secret in Key Vault
    add                      Add a new secret (alias for set)
    update                   Update an existing secret (alias for set)
    get                      Get a secret from Key Vault
    list                     List all secrets in Key Vault
    delete                   Delete a secret from Key Vault
    setup-all                Set all CryptoDiffs secrets from local.settings.json
    sync                     Sync secrets from local.settings.json (add new, update existing)
    help                     Show this help message

Options:
    --vault-name NAME        Key Vault name (required for most commands)
    --resource-group NAME    Resource group name (required for create)
    --location NAME          Azure location (default: eastus)
    --subscription-id ID     Azure subscription ID
    --secret-name NAME       Secret name (required for set/get/delete)
    --secret-value VALUE     Secret value (required for set)
    --file PATH              Path to local.settings.json (default: CryptoDiffs/local.settings.json)

Examples:
    # Login to Azure
    $0 login

    # Create a new Key Vault
    $0 create --vault-name cryptodiffs-kv --resource-group cryptodiffs-rg --location eastus

    # Add/Update a single secret (easiest way - automatically detects if new or existing)
    $0 add --vault-name cryptodiffs-kv --secret-name GRAPH_CLIENT_ID --secret-value "your-client-id"
    $0 set --vault-name cryptodiffs-kv --secret-name GRAPH_CLIENT_ID --secret-value "new-value"
    $0 update --vault-name cryptodiffs-kv --secret-name GRAPH_CLIENT_ID --secret-value "updated-value"

    # Get a secret
    $0 get --vault-name cryptodiffs-kv --secret-name GRAPH_CLIENT_ID

    # Set all secrets from local.settings.json (initial setup - overwrites existing)
    $0 setup-all --vault-name cryptodiffs-kv

    # Sync secrets from local.settings.json (adds new, updates existing - recommended)
    $0 sync --vault-name cryptodiffs-kv

    # List all secrets
    $0 list --vault-name cryptodiffs-kv

EOF
}

# Function to check if Azure CLI is installed
check_az_cli() {
    if ! command -v az &> /dev/null; then
        print_error "Azure CLI is not installed. Please install it from https://aka.ms/InstallAzureCLI"
        exit 1
    fi
}

# Function to check if logged in
check_login() {
    if ! az account show &> /dev/null; then
        print_warning "Not logged in to Azure. Running login..."
        az login
    fi
}

# Function to login to Azure
cmd_login() {
    print_info "Logging in to Azure..."
    az login
    print_success "Logged in successfully"
    
    # Show current subscription
    SUBSCRIPTION=$(az account show --query id -o tsv)
    print_info "Current subscription: $SUBSCRIPTION"
}

# Function to create Key Vault
cmd_create() {
    if [ -z "$KEY_VAULT_NAME" ] || [ -z "$RESOURCE_GROUP" ]; then
        print_error "Key Vault name and resource group are required for create command"
        show_usage
        exit 1
    fi

    check_az_cli
    check_login

    print_info "Creating Key Vault: $KEY_VAULT_NAME in resource group: $RESOURCE_GROUP"

    # Create resource group if it doesn't exist
    if ! az group show --name "$RESOURCE_GROUP" &> /dev/null; then
        print_info "Creating resource group: $RESOURCE_GROUP"
        az group create --name "$RESOURCE_GROUP" --location "$LOCATION"
        print_success "Resource group created"
    else
        print_info "Resource group already exists"
    fi

    # Create Key Vault
    if az keyvault show --name "$KEY_VAULT_NAME" --resource-group "$RESOURCE_GROUP" &> /dev/null; then
        print_warning "Key Vault already exists"
    else
        az keyvault create \
            --name "$KEY_VAULT_NAME" \
            --resource-group "$RESOURCE_GROUP" \
            --location "$LOCATION" \
            --enabled-for-deployment false \
            --enabled-for-template-deployment true \
            --enabled-for-disk-encryption false \
            --sku standard

        print_success "Key Vault created successfully"
    fi

    # Show Key Vault URI
    VAULT_URI=$(az keyvault show --name "$KEY_VAULT_NAME" --resource-group "$RESOURCE_GROUP" --query properties.vaultUri -o tsv)
    print_info "Key Vault URI: $VAULT_URI"
    print_info "Add this to your local.settings.json: KEY_VAULT_NAME=$KEY_VAULT_NAME"
}

# Function to set a secret (add or update)
cmd_set() {
    if [ -z "$KEY_VAULT_NAME" ] || [ -z "$SECRET_NAME" ] || [ -z "$SECRET_VALUE" ]; then
        print_error "Key Vault name, secret name, and secret value are required"
        show_usage
        exit 1
    fi

    check_az_cli
    check_login

    # Convert underscores to hyphens for Key Vault compatibility
    KEY_VAULT_SECRET_NAME=$(echo "$SECRET_NAME" | tr '_' '-')

    # Check if secret already exists
    if az keyvault secret show --vault-name "$KEY_VAULT_NAME" --name "$KEY_VAULT_SECRET_NAME" &>/dev/null; then
        print_info "Updating existing secret: $SECRET_NAME (stored as $KEY_VAULT_SECRET_NAME)"
        ACTION="updated"
    else
        print_info "Adding new secret: $SECRET_NAME (stored as $KEY_VAULT_SECRET_NAME)"
        ACTION="added"
    fi

    az keyvault secret set \
        --vault-name "$KEY_VAULT_NAME" \
        --name "$KEY_VAULT_SECRET_NAME" \
        --value "$SECRET_VALUE" \
        --output none

    if [ $? -eq 0 ]; then
        print_success "Secret '$SECRET_NAME' $ACTION successfully"
    else
        print_error "Failed to set secret '$SECRET_NAME'"
        exit 1
    fi
}

# Alias for add command
cmd_add() {
    cmd_set
}

# Alias for update command
cmd_update() {
    cmd_set
}

# Function to get a secret
cmd_get() {
    if [ -z "$KEY_VAULT_NAME" ] || [ -z "$SECRET_NAME" ]; then
        print_error "Key Vault name and secret name are required"
        show_usage
        exit 1
    fi

    check_az_cli
    check_login

    # Convert underscores to hyphens for Key Vault compatibility
    KEY_VAULT_SECRET_NAME=$(echo "$SECRET_NAME" | tr '_' '-')

    print_info "Getting secret: $SECRET_NAME (stored as $KEY_VAULT_SECRET_NAME) from Key Vault: $KEY_VAULT_NAME"
    SECRET_VALUE=$(az keyvault secret show \
        --vault-name "$KEY_VAULT_NAME" \
        --name "$KEY_VAULT_SECRET_NAME" \
        --query value -o tsv 2>/dev/null)

    if [ -z "$SECRET_VALUE" ]; then
        print_error "Secret '$SECRET_NAME' not found in Key Vault"
        exit 1
    fi

    echo "$SECRET_VALUE"
}

# Function to list all secrets
cmd_list() {
    if [ -z "$KEY_VAULT_NAME" ]; then
        print_error "Key Vault name is required"
        show_usage
        exit 1
    fi

    check_az_cli
    check_login

    print_info "Listing secrets in Key Vault: $KEY_VAULT_NAME"
    az keyvault secret list --vault-name "$KEY_VAULT_NAME" --query "[].name" -o table
}

# Function to delete a secret
cmd_delete() {
    if [ -z "$KEY_VAULT_NAME" ] || [ -z "$SECRET_NAME" ]; then
        print_error "Key Vault name and secret name are required"
        show_usage
        exit 1
    fi

    check_az_cli
    check_login

    # Convert underscores to hyphens for Key Vault compatibility
    KEY_VAULT_SECRET_NAME=$(echo "$SECRET_NAME" | tr '_' '-')

    print_warning "Deleting secret: $SECRET_NAME (stored as $KEY_VAULT_SECRET_NAME) from Key Vault: $KEY_VAULT_NAME"
    read -p "Are you sure? (y/N): " -n 1 -r
    echo
    if [[ $REPLY =~ ^[Yy]$ ]]; then
        az keyvault secret delete \
            --vault-name "$KEY_VAULT_NAME" \
            --name "$KEY_VAULT_SECRET_NAME" \
            --output none
        print_success "Secret '$SECRET_NAME' deleted successfully"
    else
        print_info "Cancelled"
    fi
}

# Function to set all secrets from local.settings.json
cmd_setup_all() {
    if [ -z "$KEY_VAULT_NAME" ]; then
        print_error "Key Vault name is required"
        show_usage
        exit 1
    fi

    local SETTINGS_FILE="${SETTINGS_FILE:-CryptoDiffs/local.settings.json}"

    if [ ! -f "$SETTINGS_FILE" ]; then
        print_error "Settings file not found: $SETTINGS_FILE"
        exit 1
    fi

    check_az_cli
    check_login

    print_info "Reading secrets from: $SETTINGS_FILE"
    print_info "Setting secrets in Key Vault: $KEY_VAULT_NAME"

    # Parse JSON and extract Values section
    # Using jq if available, otherwise use a simple grep/sed approach
    if command -v jq &> /dev/null; then
        # Use jq to parse JSON
        while IFS= read -r line; do
            KEY=$(echo "$line" | jq -r '.key')
            VALUE=$(echo "$line" | jq -r '.value')
            
            if [ "$KEY" != "null" ] && [ "$VALUE" != "null" ] && [ -n "$VALUE" ]; then
                # Skip AzureWebJobsStorage and FUNCTIONS_WORKER_RUNTIME as they're runtime-specific
                if [[ "$KEY" != "AzureWebJobsStorage" && "$KEY" != "FUNCTIONS_WORKER_RUNTIME" ]]; then
                    # Azure Key Vault secret names can only contain alphanumeric and hyphens
                    # Replace underscores with hyphens for Key Vault compatibility
                    KEY_VAULT_NAME_CLEAN=$(echo "$KEY" | tr '_' '-')
                    print_info "Setting: $KEY (as $KEY_VAULT_NAME_CLEAN in Key Vault)"
                    az keyvault secret set \
                        --vault-name "$KEY_VAULT_NAME" \
                        --name "$KEY_VAULT_NAME_CLEAN" \
                        --value "$VALUE" \
                        --output none 2>/dev/null || print_warning "Failed to set $KEY"
                fi
            fi
        done < <(jq -c '.Values | to_entries[]' "$SETTINGS_FILE")
    else
        # Fallback: simple grep/sed approach for basic key-value pairs
        print_warning "jq not found. Using basic parsing. Install jq for better JSON support."
        grep -E '^\s*"[^"]+":\s*"[^"]*",?\s*$' "$SETTINGS_FILE" | while IFS= read -r line; do
            KEY=$(echo "$line" | sed -E 's/^\s*"([^"]+)":.*/\1/')
            VALUE=$(echo "$line" | sed -E 's/.*:\s*"([^"]*)".*/\1/')
            
            # Skip AzureWebJobsStorage and FUNCTIONS_WORKER_RUNTIME
            if [[ "$KEY" != "AzureWebJobsStorage" && "$KEY" != "FUNCTIONS_WORKER_RUNTIME" && -n "$VALUE" ]]; then
                # Azure Key Vault secret names can only contain alphanumeric and hyphens
                # Replace underscores with hyphens for Key Vault compatibility
                KEY_VAULT_NAME_CLEAN=$(echo "$KEY" | tr '_' '-')
                print_info "Setting: $KEY (as $KEY_VAULT_NAME_CLEAN in Key Vault)"
                az keyvault secret set \
                    --vault-name "$KEY_VAULT_NAME" \
                    --name "$KEY_VAULT_NAME_CLEAN" \
                    --value "$VALUE" \
                    --output none 2>/dev/null || print_warning "Failed to set $KEY"
            fi
        done
    fi

    print_success "All secrets set successfully"
    print_info "Remember to set KEY_VAULT_NAME=$KEY_VAULT_NAME in your Function App settings"
}

# Function to sync secrets from local.settings.json (add new, update existing)
cmd_sync() {
    if [ -z "$KEY_VAULT_NAME" ]; then
        print_error "Key Vault name is required"
        show_usage
        exit 1
    fi

    local SETTINGS_FILE="${SETTINGS_FILE:-CryptoDiffs/local.settings.json}"

    if [ ! -f "$SETTINGS_FILE" ]; then
        print_error "Settings file not found: $SETTINGS_FILE"
        exit 1
    fi

    check_az_cli
    check_login

    print_info "Syncing secrets from: $SETTINGS_FILE to Key Vault: $KEY_VAULT_NAME"
    
    # Use temporary file for counters to avoid subshell issues
    TEMP_COUNTERS=$(mktemp)
    echo "ADDED=0" > "$TEMP_COUNTERS"
    echo "UPDATED=0" >> "$TEMP_COUNTERS"
    echo "FAILED=0" >> "$TEMP_COUNTERS"
    echo "SKIPPED=0" >> "$TEMP_COUNTERS"

    # Function to update counter
    update_counter() {
        local counter_name=$1
        local current=$(grep "^${counter_name}=" "$TEMP_COUNTERS" | cut -d'=' -f2)
        local new=$((current + 1))
        sed -i "s/^${counter_name}=.*/${counter_name}=${new}/" "$TEMP_COUNTERS"
    }

    # Parse JSON and extract Values section
    if command -v jq &> /dev/null; then
        while IFS= read -r line; do
            KEY=$(echo "$line" | jq -r '.key')
            VALUE=$(echo "$line" | jq -r '.value')
            
            if [ "$KEY" != "null" ] && [ "$VALUE" != "null" ] && [ -n "$VALUE" ]; then
                # Skip AzureWebJobsStorage and FUNCTIONS_WORKER_RUNTIME as they're runtime-specific
                if [[ "$KEY" != "AzureWebJobsStorage" && "$KEY" != "FUNCTIONS_WORKER_RUNTIME" ]]; then
                    KEY_VAULT_SECRET_NAME=$(echo "$KEY" | tr '_' '-')
                    
                    # Check if secret exists
                    if az keyvault secret show --vault-name "$KEY_VAULT_NAME" --name "$KEY_VAULT_SECRET_NAME" &>/dev/null; then
                        print_info "Updating: $KEY (existing)"
                        STATUS="updated"
                        update_counter "UPDATED"
                    else
                        print_info "Adding: $KEY (new)"
                        STATUS="added"
                        update_counter "ADDED"
                    fi
                    
                    if az keyvault secret set \
                        --vault-name "$KEY_VAULT_NAME" \
                        --name "$KEY_VAULT_SECRET_NAME" \
                        --value "$VALUE" \
                        --output none 2>/dev/null; then
                        print_success "  ✓ $KEY $STATUS"
                    else
                        print_warning "  ✗ Failed to $STATUS $KEY"
                        update_counter "FAILED"
                    fi
                else
                    update_counter "SKIPPED"
                fi
            fi
        done < <(jq -c '.Values | to_entries[]' "$SETTINGS_FILE")
    else
        print_warning "jq not found. Using basic parsing. Install jq for better JSON support."
        while IFS= read -r line; do
            KEY=$(echo "$line" | sed -E 's/^\s*"([^"]+)":.*/\1/')
            VALUE=$(echo "$line" | sed -E 's/.*:\s*"([^"]*)".*/\1/')
            
            if [[ "$KEY" != "AzureWebJobsStorage" && "$KEY" != "FUNCTIONS_WORKER_RUNTIME" && -n "$VALUE" ]]; then
                KEY_VAULT_SECRET_NAME=$(echo "$KEY" | tr '_' '-')
                
                if az keyvault secret show --vault-name "$KEY_VAULT_NAME" --name "$KEY_VAULT_SECRET_NAME" &>/dev/null; then
                    print_info "Updating: $KEY (existing)"
                    STATUS="updated"
                    update_counter "UPDATED"
                else
                    print_info "Adding: $KEY (new)"
                    STATUS="added"
                    update_counter "ADDED"
                fi
                
                if az keyvault secret set \
                    --vault-name "$KEY_VAULT_NAME" \
                    --name "$KEY_VAULT_SECRET_NAME" \
                    --value "$VALUE" \
                    --output none 2>/dev/null; then
                    print_success "  ✓ $KEY $STATUS"
                else
                    print_warning "  ✗ Failed to $STATUS $KEY"
                    update_counter "FAILED"
                fi
            else
                update_counter "SKIPPED"
            fi
        done < <(grep -E '^\s*"[^"]+":\s*"[^"]*",?\s*$' "$SETTINGS_FILE")
    fi

    # Read final counters
    ADDED=$(grep "^ADDED=" "$TEMP_COUNTERS" | cut -d'=' -f2)
    UPDATED=$(grep "^UPDATED=" "$TEMP_COUNTERS" | cut -d'=' -f2)
    FAILED=$(grep "^FAILED=" "$TEMP_COUNTERS" | cut -d'=' -f2)
    SKIPPED=$(grep "^SKIPPED=" "$TEMP_COUNTERS" | cut -d'=' -f2)
    rm -f "$TEMP_COUNTERS"

    echo ""
    print_success "Sync completed!"
    print_info "Summary: $ADDED added, $UPDATED updated, $FAILED failed, $SKIPPED skipped"
}

# Parse command line arguments
COMMAND="${1:-help}"

while [[ $# -gt 0 ]]; do
    case $1 in
        --vault-name)
            KEY_VAULT_NAME="$2"
            shift 2
            ;;
        --resource-group)
            RESOURCE_GROUP="$2"
            shift 2
            ;;
        --location)
            LOCATION="$2"
            shift 2
            ;;
        --subscription-id)
            SUBSCRIPTION_ID="$2"
            shift 2
            ;;
        --secret-name)
            SECRET_NAME="$2"
            shift 2
            ;;
        --secret-value)
            SECRET_VALUE="$2"
            shift 2
            ;;
        --file)
            SETTINGS_FILE="$2"
            shift 2
            ;;
        login|create|set|add|update|get|list|delete|setup-all|sync|help)
            COMMAND="$1"
            shift
            ;;
        *)
            print_error "Unknown option: $1"
            show_usage
            exit 1
            ;;
    esac
done

# Set subscription if provided
if [ -n "$SUBSCRIPTION_ID" ]; then
    az account set --subscription "$SUBSCRIPTION_ID"
fi

# Execute command
case $COMMAND in
    login)
        cmd_login
        ;;
    create)
        cmd_create
        ;;
    set)
        cmd_set
        ;;
    add)
        cmd_add
        ;;
    update)
        cmd_update
        ;;
    get)
        cmd_get
        ;;
    list)
        cmd_list
        ;;
    delete)
        cmd_delete
        ;;
    setup-all)
        cmd_setup_all
        ;;
    sync)
        cmd_sync
        ;;
    help|*)
        show_usage
        ;;
esac

