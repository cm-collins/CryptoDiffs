using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Serilog;
using CryptoDiffs;

var builder = FunctionsApplication.CreateBuilder(args);

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .WriteTo.File("logs/cryptodiffs-.log", rollingInterval: RollingInterval.Day)
    .Enrich.FromLogContext()
    .CreateLogger();

builder.Services.AddLogging(loggingBuilder =>
{
    loggingBuilder.AddSerilog();
});

builder.ConfigureFunctionsWebApplication();

// Configure Functions extensions
builder.Services
    .AddApplicationInsightsTelemetryWorkerService()
    .ConfigureFunctionsApplicationInsights();

// Register KeyVaultService first
builder.Services.AddSingleton<KeyVaultService>(sp =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();
    var logger = sp.GetRequiredService<ILogger<KeyVaultService>>();
    return new KeyVaultService(configuration, logger);
});

// Configure AppSettings with Key Vault support
builder.Services.AddOptions<AppSettings>().PostConfigure<KeyVaultService>((options, keyVault) =>
{
    // Get values from Key Vault with local fallback
    options.BinanceBaseUrl = keyVault.GetValue("BINANCE_BASE_URL", "https://api.binance.com");
    options.DefaultSymbol = keyVault.GetValue("DEFAULT_SYMBOL", "BTCUSDT");
    options.DefaultPeriods = keyVault.GetValue("DEFAULT_PERIODS", "60,90");
    options.DefaultInterval = keyVault.GetValue("DEFAULT_INTERVAL", "1d");
    options.DefaultAggregate = keyVault.GetValue("DEFAULT_AGGREGATE", "close");
    options.RandomSymbols = keyVault.GetValue("RANDOM_SYMBOLS", "BTCUSDT,ETHUSDT,BNBUSDT,SOLUSDT,ADAUSDT,XRPUSDT,DOTUSDT,AVAXUSDT,LTCUSDT");
    options.EmailOnTimer = keyVault.GetBoolValue("EMAIL_ON_TIMER", false);
    options.MailFrom = keyVault.GetValue("MAIL_FROM", string.Empty);
    options.MailTo = keyVault.GetValue("MAIL_TO", string.Empty);
    options.MailCc = keyVault.GetValue("MAIL_CC", string.Empty);
    options.MailBcc = keyVault.GetValue("MAIL_BCC", string.Empty);
    options.GraphClientId = keyVault.GetValue("GRAPH_CLIENT_ID", string.Empty);
    options.GraphClientSecret = keyVault.GetValue("GRAPH_CLIENT_SECRET", string.Empty);
    options.GraphTenantId = keyVault.GetValue("GRAPH_TENANT_ID", string.Empty);
    options.EnableCache = keyVault.GetBoolValue("ENABLE_CACHE", true);
    options.CacheTtlSeconds = keyVault.GetIntValue("CACHE_TTL_SECONDS", 300);
    options.CosmosDbConnectionString = keyVault.GetValue("COSMOS_DB_CONNECTION_STRING", string.Empty);
    options.CosmosDbDatabaseName = keyVault.GetValue("COSMOS_DB_DATABASE_NAME", string.Empty);
    options.CosmosDbContainerName = keyVault.GetValue("COSMOS_DB_CONTAINER_NAME", "execution-logs");
    options.KeyVaultName = keyVault.GetValue("KEY_VAULT_NAME", string.Empty);
});

// Register HttpClient
builder.Services.AddHttpClient();

// Register services
builder.Services.AddSingleton<BinanceService>(sp =>
{
    var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
    var httpClient = httpClientFactory.CreateClient();
    var logger = sp.GetRequiredService<ILogger<BinanceService>>();
    var settings = sp.GetRequiredService<IOptions<AppSettings>>();
    return new BinanceService(httpClient, logger, settings);
});
builder.Services.AddSingleton<CalculationService>();
builder.Services.AddSingleton<ExcelService>();
builder.Services.AddSingleton<EmailService>();
builder.Services.AddSingleton<CosmosDbLoggingService>();

builder.Build().Run();
