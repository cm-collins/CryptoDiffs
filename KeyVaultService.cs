using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Serilog;

namespace CryptoDiffs;

/// <summary>
/// Service for retrieving configuration values from Azure Key Vault only (no local fallback).
/// All secrets must be stored in Azure Key Vault when KEY_VAULT_NAME is configured.
/// Provides secure secret management for production deployments.
/// </summary>
public class KeyVaultService
{
    private readonly SecretClient? _secretClient;
    private readonly IConfiguration _configuration;
    private readonly bool _enabled;
    private readonly string? _keyVaultName;

    public KeyVaultService(IConfiguration configuration, ILogger<KeyVaultService> logger)
    {
        _configuration = configuration;
        _keyVaultName = configuration["KEY_VAULT_NAME"];

        // Enable Key Vault if name is provided
        _enabled = !string.IsNullOrWhiteSpace(_keyVaultName);

        if (_enabled)
        {
            try
            {
                var keyVaultUri = $"https://{_keyVaultName}.vault.azure.net/";
                
                // Configure DefaultAzureCredential to prefer Azure CLI for local development
                // This avoids trying Managed Identity first (which fails locally)
                var credentialOptions = new DefaultAzureCredentialOptions
                {
                    ExcludeManagedIdentityCredential = true, // Skip Managed Identity for local dev
                    ExcludeEnvironmentCredential = false,
                    ExcludeVisualStudioCodeCredential = false,
                    ExcludeVisualStudioCredential = false,
                    ExcludeAzureCliCredential = false, // Use Azure CLI
                    ExcludeAzurePowerShellCredential = false,
                    ExcludeInteractiveBrowserCredential = false
                };
                
                var credential = new DefaultAzureCredential(credentialOptions);
                _secretClient = new SecretClient(new Uri(keyVaultUri), credential);
                Log.Information("Key Vault service initialized. Vault: {KeyVaultName}", _keyVaultName);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to initialize Key Vault client. Key Vault will be disabled. All secrets will use default values.");
                _enabled = false;
            }
        }
        else
        {
            Log.Warning("Key Vault is not configured. Set KEY_VAULT_NAME to enable Key Vault integration. All secrets will use default values.");
        }
    }

    /// <summary>
    /// Gets a configuration value from Key Vault only (no fallback to local config).
    /// </summary>
    /// <param name="key">Configuration key name</param>
    /// <param name="defaultValue">Default value if not found in Key Vault</param>
    /// <returns>Configuration value from Key Vault or default</returns>
    public string GetValue(string key, string? defaultValue = null)
    {
        // Only use Key Vault if enabled
        if (_enabled && _secretClient != null)
        {
            try
            {
                // Azure Key Vault secret names can only contain alphanumeric and hyphens
                // Convert underscores to hyphens for Key Vault lookup
                var keyVaultKey = key.Replace("_", "-");
                
                var secret = _secretClient.GetSecret(keyVaultKey);
                if (secret?.Value?.Value != null)
                {
                    Log.Debug("Retrieved {Key} from Key Vault (stored as {KeyVaultKey})", key, keyVaultKey);
                    return secret.Value.Value;
                }
            }
            catch (Azure.RequestFailedException ex) when (ex.Status == 404)
            {
                // Secret not found in Key Vault
                Log.Warning("Secret {Key} not found in Key Vault. Using default value.", key);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error retrieving {Key} from Key Vault. Using default value.", key);
            }
        }
        else if (!_enabled)
        {
            Log.Warning("Key Vault is not enabled. Secret {Key} will use default value.", key);
        }

        // Return default value only (no local config fallback)
        return defaultValue ?? string.Empty;
    }

    /// <summary>
    /// Gets a boolean configuration value from Key Vault only.
    /// </summary>
    public bool GetBoolValue(string key, bool defaultValue = false)
    {
        var value = GetValue(key);
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        return bool.TryParse(value, out var result) && result;
    }

    /// <summary>
    /// Gets an integer configuration value from Key Vault only.
    /// </summary>
    public int GetIntValue(string key, int defaultValue = 0)
    {
        var value = GetValue(key);
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        return int.TryParse(value, out var result) ? result : defaultValue;
    }

    /// <summary>
    /// Checks if Key Vault is enabled and accessible.
    /// </summary>
    public bool IsEnabled => _enabled && _secretClient != null;
}

