# CryptoDiffs

A lightweight serverless Azure Functions (.NET) utility that computes cryptocurrency price differences over configurable periods and can email Excel reports. Optimized for devcontainers with Azurite running via supervisord. Minimal folder layout; core files live at the project root.

## 📋 Table of Contents

- [Overview](#overview)
- [Features](#features)
- [Architecture](#architecture)
- [Prerequisites](#prerequisites)
- [Installation & Setup](#installation--setup)
- [Configuration](#configuration)
- [API Documentation](#api-documentation)
- [Usage Examples](#usage-examples)
- [Project Structure](#project-structure)
- [Development](#development)
- [Troubleshooting](#troubleshooting)
- [Acceptance Criteria](#acceptance-criteria)

## 🎯 Overview

CryptoDiffs provides on-demand and scheduled calculations of cryptocurrency price changes (absolute and percentage) for random or specified trading pairs using Binance public market data. The service can generate Excel reports and email them automatically.

### Key Capabilities

- **On-Demand Calculations**: HTTP-triggered price difference computations with custom parameters
- **Scheduled Execution**: Timer-triggered automatic calculations every 3 minutes using default settings
- **Excel Reporting**: Generate formatted Excel (.xlsx) reports with metadata
- **Email Integration**: Send reports via Microsoft Graph API with To, CC, and BCC support
- **Smart Caching**: Optional in-memory caching to reduce API calls
- **Comprehensive Logging**: Cosmos DB logging for all executions, stages, and errors
- **Azure Key Vault Integration**: Secure secret management with local fallback

## ✨ Features

### 1. Triggers & Execution Modes

#### HTTP Trigger (Manual)
- **Endpoint**: `/api/PriceDiffHttpFunction` (default route based on function name)
- **Method**: `GET` or `POST`
- **Authentication**: No authentication required (`AuthorizationLevel.Anonymous`) - suitable for local development and internal networks
- **⚠️ Production Note**: For production deployments, consider changing to `AuthorizationLevel.Function` or using Azure App Service Authentication

#### Timer Trigger (Automatic)
- **Schedule**: Every 3 minutes (`0 */3 * * * *` CRON expression)
- **Behavior**: Picks a random symbol from configured list, uses default periods and settings
- **Output**: Logs results to Cosmos DB, optional email based on configuration
- **Defaults**: Uses `DEFAULT_PERIODS` and `DEFAULT_INTERVAL` from configuration

### 2. Market Data Integration (Binance)

- Uses Binance public Klines API for historical OHLCV data
- Aggregates a single fetch covering maximum period window, slices in memory
- **Data Correctness**:
  - Always uses last fully closed candle (no partial day bias)
  - UTC-only timestamps
  - Automatic backoff & retry for 429/5xx errors

### 3. Calculations

For each configured period (e.g., 60, 90 days):

- **Basic Metrics**:
  - `startPrice`: Price at the beginning of the period
  - `endPrice`: Price at the end of the period
  - `absChange`: Absolute change (`endPrice - startPrice`)
  - `pctChange`: Percentage change (`(endPrice - startPrice) / startPrice * 100`)

- **Optional Enrichments**:
  - `high`: Highest price within the period
  - `low`: Lowest price within the period
  - Simple volatility proxy (standard deviation of daily returns)

### 4. Reporting

- **JSON Response** (default): Structured JSON with all calculated metrics
- **Excel Report**: Single-sheet workbook with:
  - Header row with column names
  - One row per period
  - Metadata section (symbol, asOf date, interval)
- **Email**: Send Excel report as attachment via Gmail

### 5. Caching (Optional)

- In-memory cache of last kline fetch for ≤ 5 minutes
- Reduces API calls during frequent timer executions
- Configurable via `ENABLE_CACHE` and `CACHE_TTL_SECONDS`

### 6. Logging & Observability

- **Cosmos DB Logging**: Comprehensive execution logging to Azure Cosmos DB
  - Execution logs: Full request/response tracking, duration, status
  - Stage logs: Individual operation stages (RequestParsing, Validation, BinanceFetch, Calculation, ExcelGeneration, EmailSending)
  - Error tracking: Detailed error information with stack traces
  - Email logging: Complete recipient information (To, CC, BCC), MessageId, attachment details
- **Serilog Integration**: Structured logging to console and daily rolling files
- **Application Insights**: Custom events and metrics
- **Metrics**: `binance_latency_ms`, `binance_status`, `cache_hit`

### 7. Validation & Security

- **Symbol Validation**: Whitelist regex `^[A-Z]{3,10}USDT$`
- **Period Limits**: 
  - Maximum 3650 days per period
  - Maximum list length: 10 periods
- **Security**: 
  - HTTP function protected via Function key (or EasyAuth in Azure)
  - **Azure Key Vault Integration**: Secure secret management with automatic fallback to local configuration
  - Secrets prioritized: Key Vault → Local Configuration → Defaults

## 🏗️ Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                    Azure Functions Host                      │
├─────────────────────────────────────────────────────────────┤
│  HTTP Trigger          │  Timer Trigger (5 min)             │
│  /api/price-diff       │  Random symbol selection           │
└──────────┬─────────────┴──────────────┬─────────────────────┘
           │                            │
           └────────────┬───────────────┘
                        │
           ┌────────────▼────────────┐
           │   CalculationService    │
           │  (Period slicing,       │
           │   metrics calculation)  │
           └────────────┬────────────┘
                        │
           ┌────────────▼────────────┐
           │     BinanceService      │
           │  (Klines API, caching)  │
           └────────────┬────────────┘
                        │
           ┌────────────▼────────────┐
           │   ExcelService (opt)    │
           │   EmailService (opt)    │
           └────────────┬────────────┘
                        │
           ┌────────────▼────────────┐
           │  CosmosDbLoggingService  │
           │  (Execution & stage     │
           │   logging to Cosmos DB) │
           └─────────────────────────┘
                        │
           ┌────────────▼────────────┐
           │    KeyVaultService       │
           │  (Secure config with     │
           │   local fallback)       │
           └─────────────────────────┘
```

## 📦 Prerequisites

- **.NET 8 SDK** (isolated worker process or in-process)
- **Azure Functions Core Tools** v4
- **Azure Storage Emulator (Azurite)** (for local development)
- **Gmail Account** (for email functionality)
  - Either OAuth2 credentials or App Password (with 2FA enabled)

## 🚀 Installation & Setup

### 1. Clone and Navigate

```bash
git clone <repository-url>
cd cryptodiffs
```

### 2. Configure Local Settings

Create `local.settings.json` in the project root (not committed to repo):

```json
{
  "IsEncrypted": false,
  "Values": {
    "AzureWebJobsStorage": "UseDevelopmentStorage=true",
    "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",
    "BINANCE_BASE_URL": "https://api.binance.com",
    "DEFAULT_SYMBOL": "BTCUSDT",
    "DEFAULT_PERIODS": "60,90",
    "DEFAULT_INTERVAL": "1d",
    "DEFAULT_AGGREGATE": "close",
    "RANDOM_SYMBOLS": "BTCUSDT,ETHUSDT,BNBUSDT,SOLUSDT,ADAUSDT,XRPUSDT,DOTUSDT,AVAXUSDT,LTCUSDT",
    "EMAIL_ON_TIMER": "false",
    "MAIL_TO": "your-email@gmail.com",
    "MAIL_FROM": "your-email@gmail.com",
    "USE_OAUTH": "true",
    "GRAPH_CLIENT_ID": "your-client-id",
    "GRAPH_CLIENT_SECRET": "your-client-secret",
    "GRAPH_TENANT_ID": "your-tenant-id",
    "ENABLE_CACHE": "true",
    "CACHE_TTL_SECONDS": "300",
    "COSMOS_DB_CONNECTION_STRING": "AccountEndpoint=https://your-account.documents.azure.com:443/;AccountKey=your-key;",
    "COSMOS_DB_DATABASE_NAME": "cryptodiffs-logs",
    "COSMOS_DB_CONTAINER_NAME": "execution-logs",
    "KEY_VAULT_NAME": "your-keyvault-name"
  }
}
```

### 3. Microsoft Graph Setup (Email)

1. Go to [Azure Portal](https://portal.azure.com/)
2. Register an Azure AD application
3. Grant `Mail.Send` permission (Application permission)
4. Create a client secret
5. Add credentials to `local.settings.json`:
   - `GRAPH_CLIENT_ID`: Application (client) ID
   - `GRAPH_CLIENT_SECRET`: Client secret value
   - `GRAPH_TENANT_ID`: Directory (tenant) ID

### 4. Azure Key Vault Setup (Optional but Recommended)

1. Install Azure CLI: [Install Azure CLI](https://aka.ms/InstallAzureCLI)
2. Login to Azure:
   ```bash
   ./scripts/manage-keyvault.sh login
   ```
3. Create Key Vault:
   ```bash
   ./scripts/manage-keyvault.sh create --vault-name cryptodiffs-kv --resource-group cryptodiffs-rg --location eastus
   ```
4. Set all secrets from local.settings.json:
   ```bash
   ./scripts/manage-keyvault.sh setup-all --vault-name cryptodiffs-kv
   ```
5. Add `KEY_VAULT_NAME` to your Function App settings in Azure Portal

### 5. Run Locally

```bash
# Navigate to the CryptoDiffs project directory
cd CryptoDiffs

# Start Azurite (if using devcontainer, supervisord handles this)
# In a separate terminal:
azurite --silent --location ./azurite --debug ./azurite/debug.log

# Run the function app
func start

# The output will show:
# Functions:
#   PriceDiffHttpFunction: [GET,POST] http://localhost:7071/api/PriceDiffHttpFunction
#   PriceDiffTimerFunction: timerTrigger
#
# For detailed API information, including function keys, check the output above.
# Function keys are displayed in the format: ?code=YOUR_FUNCTION_KEY
```

**Note**: The HTTP function uses `AuthorizationLevel.Anonymous`, so no function key is required for local testing. For production, consider enabling authentication.

## ⚙️ Configuration

### Application Settings

All settings can be configured via `local.settings.json` (local) or Azure App Settings / Key Vault (production).

| Setting | Description | Default | Required |
|---------|-------------|---------|----------|
| `BINANCE_BASE_URL` | Binance API base URL | `https://api.binance.com` | No |
| `DEFAULT_SYMBOL` | Default trading pair | `BTCUSDT` | No |
| `DEFAULT_PERIODS` | Comma-separated list of days | `60,90` | No |
| `DEFAULT_INTERVAL` | Candle interval | `1d` | No |
| `DEFAULT_AGGREGATE` | Price aggregation method | `close` | No |
| `RANDOM_SYMBOLS` | Comma-separated symbols for timer | `BTCUSDT,ETHUSDT,...` | No |
| `EMAIL_ON_TIMER` | Enable email on timer trigger | `false` | No |
| `MAIL_TO` | Recipient email address | - | Yes (if email enabled) |
| `MAIL_FROM` | Sender email address | - | Yes (if email enabled) |
| `MAIL_CC` | CC recipient email addresses (comma-separated) | - | No |
| `MAIL_BCC` | BCC recipient email addresses (comma-separated) | - | No |
| `GRAPH_CLIENT_ID` | Microsoft Graph client ID | - | Yes (if email enabled) |
| `GRAPH_CLIENT_SECRET` | Microsoft Graph client secret | - | Yes (if email enabled) |
| `GRAPH_TENANT_ID` | Microsoft Graph tenant ID | - | Yes (if email enabled) |
| `ENABLE_CACHE` | Enable in-memory caching | `true` | No |
| `CACHE_TTL_SECONDS` | Cache TTL in seconds | `300` | No |
| `COSMOS_DB_CONNECTION_STRING` | Cosmos DB connection string | - | No (if logging enabled) |
| `COSMOS_DB_DATABASE_NAME` | Cosmos DB database name | - | No (if logging enabled) |
| `COSMOS_DB_CONTAINER_NAME` | Cosmos DB container name | `execution-logs` | No |
| `KEY_VAULT_NAME` | Azure Key Vault name | - | No (if using Key Vault) |

### Aggregate Methods

- `close`: Closing price (default)
- `open`: Opening price
- `avg`: Average of high and low
- `ohlc4`: (Open + High + Low + Close) / 4

## 📚 API Documentation

### HTTP Trigger: `/api/PriceDiffHttpFunction`

Calculate price differences for a specified symbol and periods.

**Base URL (Local)**: `http://localhost:7071`  
**Base URL (Production)**: `https://your-function-app.azurewebsites.net`

**Note**: The endpoint name matches the function name. To use a custom route like `/api/price-diff`, add a `Route` attribute to the `HttpTrigger` in `PriceDiffHttpFunction.cs`.

#### Request Parameters

| Parameter | Type | Location | Description | Default |
|-----------|------|----------|-------------|---------|
| `symbol` | string | Query/Body | Trading pair symbol (e.g., BTCUSDT) | `BTCUSDT` |
| `periods` | string | Query/Body | Comma-separated list of days (e.g., `60,90`) | `60,90` |
| `asOf` | string | Query/Body | ISO 8601 UTC timestamp | Last closed daily candle |
| `interval` | string | Query/Body | Candle interval (`1d`, `1h`, etc.) | `1d` |
| `aggregate` | string | Query/Body | Price aggregation method | `close` |
| `email` | boolean | Query/Body | Send email report | `false` |
| `report` | string | Query/Body | Report format: `none`, `excel`, `json` | `json` |

#### Response Format

**Success (200 OK):**

```json
{
  "symbol": "BTCUSDT",
  "asOf": "2025-11-11T00:00:00Z",
  "interval": "1d",
  "aggregate": "close",
  "results": [
    {
      "days": 60,
      "startCandle": "2025-09-12",
      "endCandle": "2025-11-10",
      "startPrice": 45230.50,
      "endPrice": 47890.25,
      "absChange": 2659.75,
      "pctChange": 5.88,
      "high": 48500.00,
      "low": 44800.00
    },
    {
      "days": 90,
      "startCandle": "2025-08-12",
      "endCandle": "2025-11-10",
      "startPrice": 43800.00,
      "endPrice": 47890.25,
      "absChange": 4090.25,
      "pctChange": 9.34,
      "high": 49000.00,
      "low": 43500.00
    }
  ],
  "notes": [
    "Uses last fully closed daily candle.",
    "Binance public klines."
  ]
}
```

**Error (400 Bad Request):**

```json
{
  "error": "Invalid symbol format. Must match pattern: ^[A-Z]{3,10}USDT$"
}
```

**Error (500 Internal Server Error):**

```json
{
  "error": "Failed to fetch data from Binance API",
  "details": "Network timeout after 3 retries"
}
```

## 💡 Usage Examples

### No Function Key Required!

The HTTP function is configured with `AuthorizationLevel.Anonymous`, so **no function key is needed** for local testing. You can call it directly:

```bash
# Simple GET request - no key needed!
curl "http://localhost:7071/api/PriceDiffHttpFunction"

# With custom parameters - no key needed!
curl "http://localhost:7071/api/PriceDiffHttpFunction?symbol=ETHUSDT&periods=30,60,90"
```

**Example Output from `func start`:**
```
Functions:
    PriceDiffHttpFunction: [GET,POST] http://localhost:7071/api/PriceDiffHttpFunction

Host started
```

⚠️ **Production Security**: For production deployments exposed to the internet, consider:
- Changing to `AuthorizationLevel.Function` in `PriceDiffHttpFunction.cs`
- Using Azure App Service Authentication
- Implementing API Management with authentication
- Using network restrictions (VNet integration)

### Basic JSON Response

```bash
# GET request with defaults (no key needed!)
# Note: Timer trigger uses defaults, but HTTP allows custom parameters
curl "http://localhost:7071/api/PriceDiffHttpFunction"

# GET request with custom symbol and periods (HTTP trigger allows customization)
curl "http://localhost:7071/api/PriceDiffHttpFunction?symbol=ETHUSDT&periods=30,60,90"

# GET request with custom interval and aggregate method
curl "http://localhost:7071/api/PriceDiffHttpFunction?symbol=BTCUSDT&periods=7,30&interval=1h&aggregate=avg"

# POST request with JSON body (no key needed!)
curl -X POST "http://localhost:7071/api/PriceDiffHttpFunction" \
  -H "Content-Type: application/json" \
  -d '{
    "symbol": "SOLUSDT",
    "periods": "7,30,90",
    "interval": "1d",
    "aggregate": "close"
  }'
```

### Generate Excel Report

```bash
# Get Excel report as download (no key needed!)
curl "http://localhost:7071/api/PriceDiffHttpFunction?symbol=BTCUSDT&report=excel" \
  --output report.xlsx

# Send Excel report via email
curl "http://localhost:7071/api/PriceDiffHttpFunction?symbol=BTCUSDT&report=excel&email=true"
```

### Custom Date Range

```bash
# Calculate as of specific date
curl "http://localhost:7071/api/PriceDiffHttpFunction?symbol=BTCUSDT&asOf=2025-11-01T00:00:00Z"
```

### Different Aggregation Methods

```bash
# Use average price
curl "http://localhost:7071/api/PriceDiffHttpFunction?symbol=BTCUSDT&aggregate=avg"

# Use OHLC4
curl "http://localhost:7071/api/PriceDiffHttpFunction?symbol=BTCUSDT&aggregate=ohlc4"
```

### Production (Azure)

⚠️ **Important**: For production, you should enable authentication. Options:
1. Change to `AuthorizationLevel.Function` and use function keys
2. Use Azure App Service Authentication
3. Use API Management with authentication

```bash
# If using AuthorizationLevel.Function in production:
curl "https://your-function-app.azurewebsites.net/api/PriceDiffHttpFunction?code=YOUR_FUNCTION_KEY&symbol=BTCUSDT"
```

### Quick Test Script

Save this as `test-function.sh` and make it executable:

```bash
#!/bin/bash
# No function key needed - AuthorizationLevel.Anonymous

BASE_URL="http://localhost:7071/api/PriceDiffHttpFunction"

echo "Testing with defaults..."
curl "${BASE_URL}"

echo -e "\n\nTesting with ETHUSDT..."
curl "${BASE_URL}?symbol=ETHUSDT&periods=30,60"

echo -e "\n\nTesting POST request..."
curl -X POST "${BASE_URL}" \
  -H "Content-Type: application/json" \
  -d '{"symbol": "SOLUSDT", "periods": "7,30"}'
```

Run with: `chmod +x test-function.sh && ./test-function.sh`

## 📁 Project Structure

```
/ (project root)
├── host.json                          # Functions host configuration
├── local.settings.json                # Local settings (not committed)
├── CryptoDiffs.csproj                 # .NET project file
├── Program.cs                         # Functions host builder, DI registrations
├── PriceDiffHttpFunction.cs           # HTTP endpoint implementation
├── PriceDiffTimerFunction.cs          # 5-minute CRON trigger
├── BinanceService.cs                  # Binance API client, klines fetch, close-candle logic
├── CalculationService.cs              # Period slicing, metrics calculation
├── ExcelService.cs                    # Excel report generation (in-memory .xlsx)
├── EmailService.cs                    # Microsoft Graph email sending
├── CosmosDbLoggingService.cs          # Cosmos DB execution logging
├── KeyVaultService.cs                 # Azure Key Vault integration
├── Models.cs                          # DTOs: requests, responses, klines, results, log entries
├── Validation.cs                      # Symbol/period validators
├── Settings.cs                        # IOptions<> bindings for app settings
├── scripts/
│   └── manage-keyvault.sh             # Key Vault management script
└── README.md                          # This file
```

## 🔧 Development

### Building the Project

```bash
dotnet build
```

### Running Tests

```bash
dotnet test
```

### Debugging Locally

1. Set breakpoints in your IDE
2. Run `func start` with debugger attached
3. Get the function key from the console output
4. Make HTTP requests to `http://localhost:7071/api/PriceDiffHttpFunction?code=YOUR_FUNCTION_KEY`

### Dependency Injection

Services are registered in `Program.cs`:

```csharp
builder.Services.AddSingleton<KeyVaultService>();
builder.Services.AddSingleton<BinanceService>();
builder.Services.AddSingleton<CalculationService>();
builder.Services.AddSingleton<ExcelService>();
builder.Services.AddSingleton<EmailService>();
builder.Services.AddSingleton<CosmosDbLoggingService>();
builder.Services.Configure<AppSettings>(...); // With Key Vault support
```

### Azure Key Vault Management

Use the provided script to manage secrets:

```bash
# Login to Azure
./scripts/manage-keyvault.sh login

# Create a new Key Vault
./scripts/manage-keyvault.sh create --vault-name cryptodiffs-kv --resource-group cryptodiffs-rg

# Set a single secret
./scripts/manage-keyvault.sh set --vault-name cryptodiffs-kv --secret-name GRAPH_CLIENT_ID --secret-value "your-value"

# Set all secrets from local.settings.json
./scripts/manage-keyvault.sh setup-all --vault-name cryptodiffs-kv

# List all secrets
./scripts/manage-keyvault.sh list --vault-name cryptodiffs-kv

# Get a secret value
./scripts/manage-keyvault.sh get --vault-name cryptodiffs-kv --secret-name GRAPH_CLIENT_ID
```

**Key Vault Priority**: The application checks Key Vault first, then falls back to local configuration, then defaults.

### Adding New Features

1. **New Service**: Add to `Program.cs` DI registration
2. **New Endpoint**: Create new function class with `[Function]` attribute
3. **New Model**: Add to `Models.cs`
4. **New Setting**: Add to `Settings.cs` and `local.settings.json`

## 🐛 Troubleshooting

### Common Issues

#### 1. Binance API Errors

**Problem**: `429 Too Many Requests` or `500 Internal Server Error`

**Solution**: 
- The service automatically retries with exponential backoff
- Enable caching to reduce API calls: `ENABLE_CACHE=true`
- Check Binance API status: [Binance Status Page](https://www.binance.com/en/support/announcement)

#### 2. Email Not Sending

**Problem**: Emails not being delivered

**Solutions**:
- **Microsoft Graph**: Verify client ID, secret, and tenant ID are correct
- Ensure the Azure AD app has `Mail.Send` permission (Application permission)
- Grant admin consent for the permission
- Verify `MAIL_TO`, `MAIL_FROM`, `MAIL_CC`, and `MAIL_BCC` are valid email addresses
- Check Cosmos DB logs for detailed error messages
- Verify the sender account has permission to send emails

#### 3. Invalid Symbol Error

**Problem**: `Invalid symbol format` error

**Solution**: 
- Symbols must match pattern: `^[A-Z]{3,10}USDT$`
- Examples: `BTCUSDT`, `ETHUSDT`, `SOLUSDT`
- Ensure symbol exists on Binance

#### 4. Timezone Issues

**Problem**: Dates/times not matching expectations

**Solution**:
- All timestamps are UTC
- `asOf` parameter must be ISO 8601 UTC format
- Last closed candle is calculated in UTC

#### 5. Azurite Connection Issues

**Problem**: Cannot connect to Azurite storage emulator

**Solution**:
- Ensure Azurite is running: `azurite --silent --location ./azurite`
- Check `AzureWebJobsStorage` setting: `UseDevelopmentStorage=true`
- In devcontainer, supervisord should start Azurite automatically

#### 6. Authentication (Production)

**Problem**: Need to secure the HTTP endpoint in production

**Solutions**:
- **Current Setup**: HTTP function uses `AuthorizationLevel.Anonymous` (no authentication required)
- **For Production Security**:
  1. Change `AuthorizationLevel.Anonymous` to `AuthorizationLevel.Function` in `PriceDiffHttpFunction.cs`
  2. Get function key from Azure Portal → Function App → Functions → PriceDiffHttpFunction → Function Keys
  3. Include key in requests: `?code=YOUR_KEY` or header `x-functions-key: YOUR_KEY`
  4. Alternative: Use Azure App Service Authentication
  5. Alternative: Use API Management with authentication policies
  6. Alternative: Use VNet integration to restrict network access

#### 7. Key Vault Access Issues

**Problem**: Cannot access secrets from Key Vault

**Solutions**:
- Ensure `KEY_VAULT_NAME` is set in Function App settings
- Verify the Function App's managed identity has "Key Vault Secrets User" role on the Key Vault
- For local development, ensure you're logged in via `az login`
- Check that secrets exist in Key Vault using `./scripts/manage-keyvault.sh list`
- Application will fall back to local configuration if Key Vault is unavailable

#### 8. Cosmos DB Logging Not Working

**Problem**: Logs not appearing in Cosmos DB

**Solutions**:
- Verify `COSMOS_DB_CONNECTION_STRING`, `COSMOS_DB_DATABASE_NAME`, and `COSMOS_DB_CONTAINER_NAME` are set
- Ensure the Cosmos DB account and database exist
- Check that the connection string is valid and has proper permissions
- Application will continue to work even if Cosmos DB logging fails (logs to Serilog only)

### Debugging Tips

1. **Enable Detailed Logging**: Set log level to `Debug` in `host.json`
2. **Check Application Insights**: View traces and custom events
3. **Test Individual Services**: Unit test services independently
4. **Validate Configuration**: Ensure all required settings are present

## ✅ Acceptance Criteria

- [x] HTTP call with defaults returns JSON including per-period diffs
- [x] HTTP call allows custom parameters (symbol, periods, interval, aggregate)
- [x] HTTP call with `report=excel&email=true` sends an email with .xlsx attached
- [x] Timer runs every 3 minutes, uses default settings, logs to Cosmos DB, and (if enabled) sends an email
- [x] Uses last fully closed daily candle (no partial day bias)
- [x] Handles network errors with retries; returns helpful messages on failure
- [x] Comprehensive Cosmos DB logging for all executions and stages
- [x] Azure Key Vault integration with local fallback
- [x] Works end-to-end inside devcontainer with Azurite up (even if not used yet)

## 📝 License

[Specify your license here]

## 🤝 Contributing

[Add contribution guidelines if applicable]

## 📧 Support

For issues, questions, or contributions, please [open an issue](link-to-issues) or contact the maintainers.

---

**Last Updated**: 2025-01-11

## 🔐 Security & Configuration

### Configuration Priority

The application uses the following priority order for configuration values:

1. **Azure Key Vault** (if `KEY_VAULT_NAME` is set)
2. **Local Configuration** (`local.settings.json` or environment variables)
3. **Default Values** (hardcoded in code)

This allows secure production deployments while maintaining local development flexibility.

### Timer vs HTTP Behavior

- **Timer Trigger**: Always uses default settings from configuration (`DEFAULT_PERIODS`, `DEFAULT_INTERVAL`, etc.)
- **HTTP Trigger**: Allows full customization via query parameters or JSON body

This design ensures consistent automated reports while providing flexibility for manual testing and custom requests.
