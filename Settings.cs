namespace CryptoDiffs;

/// <summary>
/// Application settings configuration.
/// Binds to configuration values from local.settings.json or Azure App Settings.
/// </summary>
public class AppSettings
{
    // Binance Configuration
    public string BinanceBaseUrl { get; set; } = "https://api.binance.com";

    // Default Request Parameters
    public string DefaultSymbol { get; set; } = "BTCUSDT";
    public string DefaultPeriods { get; set; } = "60,90";
    public string DefaultInterval { get; set; } = "1d";
    public string DefaultAggregate { get; set; } = "close";

    // Random Symbols for Timer
    public string RandomSymbols { get; set; } = "BTCUSDT,ETHUSDT,BNBUSDT,SOLUSDT,ADAUSDT,XRPUSDT,DOTUSDT,AVAXUSDT,LTCUSDT";

    // Email Configuration
    public bool EmailOnTimer { get; set; } = false;
    public string MailFrom { get; set; } = string.Empty;
    public string MailTo { get; set; } = string.Empty;
    public string MailCc { get; set; } = string.Empty;
    public string MailBcc { get; set; } = string.Empty;

    // Microsoft Graph Configuration
    public string GraphClientId { get; set; } = string.Empty;
    public string GraphClientSecret { get; set; } = string.Empty;
    public string GraphTenantId { get; set; } = string.Empty;

    // Cache Configuration
    public bool EnableCache { get; set; } = true;
    public int CacheTtlSeconds { get; set; } = 300;

    // Cosmos DB Logging Configuration
    public string CosmosDbConnectionString { get; set; } = string.Empty;
    public string CosmosDbDatabaseName { get; set; } = string.Empty;
    public string CosmosDbContainerName { get; set; } = "execution-logs";

    // Azure Key Vault Configuration
    public string KeyVaultName { get; set; } = string.Empty;
}

