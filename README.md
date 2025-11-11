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

- **On-Demand Calculations**: HTTP-triggered price difference computations
- **Scheduled Execution**: Timer-triggered automatic calculations every 5 minutes
- **Excel Reporting**: Generate formatted Excel (.xlsx) reports with metadata
- **Email Integration**: Send reports via Gmail (OAuth2 or App Password)
- **Smart Caching**: Optional in-memory caching to reduce API calls
- **Comprehensive Logging**: Application Insights integration with custom metrics

## ✨ Features

### 1. Triggers & Execution Modes

#### HTTP Trigger (Manual)
- **Endpoint**: `/api/PriceDiffHttpFunction` (default route based on function name)
- **Method**: `GET` or `POST`
- **Authentication**: Function key required (`AuthorizationLevel.Function`)

#### Timer Trigger (Automatic)
- **Schedule**: Every 5 minutes (`0 */5 * * * *` CRON expression)
- **Behavior**: Picks a random symbol from configured list
- **Output**: Logs results, optional email based on configuration

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

- Application Insights traces and custom events
- Custom event: `PriceDiffComputed` with `{ symbol, periods, trigger: http|timer }`
- Metrics: `binance_latency_ms`, `binance_status`, `cache_hit`
- Clear error payloads on failures (invalid symbol, empty data, network issues)

### 7. Validation & Security

- **Symbol Validation**: Whitelist regex `^[A-Z]{3,10}USDT$`
- **Period Limits**: 
  - Maximum 3650 days per period
  - Maximum list length: 10 periods
- **Security**: 
  - HTTP function protected via Function key (or EasyAuth in Azure)
  - Secrets stored in App Settings or Azure Key Vault

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
    "GMAIL_OAUTH_CLIENT_ID": "your-client-id",
    "GMAIL_OAUTH_CLIENT_SECRET": "your-client-secret",
    "GMAIL_OAUTH_REFRESH_TOKEN": "your-refresh-token",
    "ENABLE_CACHE": "true",
    "CACHE_TTL_SECONDS": "300"
  }
}
```

### 3. Gmail OAuth2 Setup (Recommended)

1. Go to [Google Cloud Console](https://console.cloud.google.com/)
2. Create a new project or select existing
3. Enable Gmail API
4. Create OAuth 2.0 credentials (Desktop app type)
5. Download credentials and obtain refresh token
6. Add credentials to `local.settings.json`

### 4. Gmail App Password Setup (Alternative)

1. Enable 2FA on your Gmail account
2. Generate App Password: [Google Account Settings](https://myaccount.google.com/apppasswords)
3. Set `USE_OAUTH=false` and configure:
   - `GMAIL_USER`: Your Gmail address
   - `GMAIL_APP_PASSWORD`: Generated app password

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

**Note**: When `func start` runs, it will display the function keys in the console output. Copy the key for authentication.

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
| `USE_OAUTH` | Use OAuth2 (true) or App Password (false) | `true` | Yes (if email enabled) |
| `GMAIL_OAUTH_CLIENT_ID` | OAuth2 client ID | - | Yes (if OAuth2) |
| `GMAIL_OAUTH_CLIENT_SECRET` | OAuth2 client secret | - | Yes (if OAuth2) |
| `GMAIL_OAUTH_REFRESH_TOKEN` | OAuth2 refresh token | - | Yes (if OAuth2) |
| `GMAIL_USER` | Gmail username | - | Yes (if App Password) |
| `GMAIL_APP_PASSWORD` | Gmail app password | - | Yes (if App Password) |
| `ENABLE_CACHE` | Enable in-memory caching | `true` | No |
| `CACHE_TTL_SECONDS` | Cache TTL in seconds | `300` | No |

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

### Getting Your Function Key

When you run `func start`, the output will include function keys. Look for a line like:
```
PriceDiffHttpFunction: [GET,POST] http://localhost:7071/api/PriceDiffHttpFunction?code=YOUR_FUNCTION_KEY_HERE
```

Copy the `code` value after `?code=` for use in your curl commands.

**Alternative for Local Testing**: You can temporarily change `AuthorizationLevel.Function` to `AuthorizationLevel.Anonymous` in `PriceDiffHttpFunction.cs` to test without a key.

### Basic JSON Response

```bash
# GET request with defaults (requires function key)
curl "http://localhost:7071/api/PriceDiffHttpFunction?code=YOUR_FUNCTION_KEY"

# GET request with custom symbol and periods
curl "http://localhost:7071/api/PriceDiffHttpFunction?code=YOUR_FUNCTION_KEY&symbol=ETHUSDT&periods=30,60,90"

# Using function key in header (alternative method)
curl -H "x-functions-key: YOUR_FUNCTION_KEY" \
  "http://localhost:7071/api/PriceDiffHttpFunction?symbol=ETHUSDT&periods=30,60,90"

# POST request with JSON body
curl -X POST "http://localhost:7071/api/PriceDiffHttpFunction?code=YOUR_FUNCTION_KEY" \
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
# Get Excel report as download
curl "http://localhost:7071/api/PriceDiffHttpFunction?code=YOUR_FUNCTION_KEY&symbol=BTCUSDT&report=excel" \
  --output report.xlsx

# Send Excel report via email
curl "http://localhost:7071/api/PriceDiffHttpFunction?code=YOUR_FUNCTION_KEY&symbol=BTCUSDT&report=excel&email=true"
```

### Custom Date Range

```bash
# Calculate as of specific date
curl "http://localhost:7071/api/PriceDiffHttpFunction?code=YOUR_FUNCTION_KEY&symbol=BTCUSDT&asOf=2025-11-01T00:00:00Z"
```

### Different Aggregation Methods

```bash
# Use average price
curl "http://localhost:7071/api/PriceDiffHttpFunction?code=YOUR_FUNCTION_KEY&symbol=BTCUSDT&aggregate=avg"

# Use OHLC4
curl "http://localhost:7071/api/PriceDiffHttpFunction?code=YOUR_FUNCTION_KEY&symbol=BTCUSDT&aggregate=ohlc4"
```

### Production (Azure)

```bash
# Include function key in query string
curl "https://your-function-app.azurewebsites.net/api/PriceDiffHttpFunction?code=YOUR_FUNCTION_KEY&symbol=BTCUSDT"

# Or use header
curl -H "x-functions-key: YOUR_FUNCTION_KEY" \
  "https://your-function-app.azurewebsites.net/api/PriceDiffHttpFunction?symbol=BTCUSDT"
```

### Quick Test Script

Save this as `test-function.sh` and make it executable:

```bash
#!/bin/bash
# Replace YOUR_FUNCTION_KEY with the actual key from func start output

FUNCTION_KEY="YOUR_FUNCTION_KEY"
BASE_URL="http://localhost:7071/api/PriceDiffHttpFunction"

echo "Testing with defaults..."
curl "${BASE_URL}?code=${FUNCTION_KEY}"

echo -e "\n\nTesting with ETHUSDT..."
curl "${BASE_URL}?code=${FUNCTION_KEY}&symbol=ETHUSDT&periods=30,60"

echo -e "\n\nTesting POST request..."
curl -X POST "${BASE_URL}?code=${FUNCTION_KEY}" \
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
├── EmailService.cs                    # Gmail OAuth2 / App Password via SMTP
├── Models.cs                          # DTOs: requests, responses, klines, results
├── Validation.cs                      # Symbol/period validators
├── Settings.cs                        # IOptions<> bindings for app settings
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
builder.Services.AddSingleton<BinanceService>();
builder.Services.AddSingleton<CalculationService>();
builder.Services.AddSingleton<ExcelService>();
builder.Services.AddSingleton<EmailService>();
builder.Services.Configure<AppSettings>(builder.Configuration);
```

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
- **OAuth2**: Verify refresh token is valid and not expired
- **App Password**: Ensure 2FA is enabled and app password is correct
- Check SMTP settings: Port 587 with TLS
- Verify `MAIL_TO` and `MAIL_FROM` are valid Gmail addresses
- Check Application Insights logs for detailed error messages

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

#### 6. Function Key Authentication

**Problem**: `401 Unauthorized` or `403 Forbidden` error

**Solution**:
- **Local Development**: 
  - Get the function key from `func start` console output
  - Include in query string: `?code=YOUR_KEY`
  - Or use header: `-H "x-functions-key: YOUR_KEY"`
  - For easier testing, temporarily change `AuthorizationLevel.Function` to `AuthorizationLevel.Anonymous` in `PriceDiffHttpFunction.cs`
- **Production**:
  - Include function key in request: `?code=YOUR_KEY`
  - Or use `x-functions-key` header
  - Verify key in Azure Portal → Function App → Functions → Your Function → Function Keys
  - Master key works for all functions, function-specific keys only work for that function

### Debugging Tips

1. **Enable Detailed Logging**: Set log level to `Debug` in `host.json`
2. **Check Application Insights**: View traces and custom events
3. **Test Individual Services**: Unit test services independently
4. **Validate Configuration**: Ensure all required settings are present

## ✅ Acceptance Criteria

- [x] HTTP call with defaults returns JSON including per-period diffs
- [x] HTTP call with `report=excel&email=true` sends an email with .xlsx attached
- [x] Timer runs every 5 minutes, logs a result, and (if enabled) sends an email
- [x] Uses last fully closed daily candle (no partial day bias)
- [x] Handles network errors with retries; returns helpful messages on failure
- [x] Works end-to-end inside devcontainer with Azurite up (even if not used yet)

## 📝 License

[Specify your license here]

## 🤝 Contributing

[Add contribution guidelines if applicable]

## 📧 Support

For issues, questions, or contributions, please [open an issue](link-to-issues) or contact the maintainers.

---

**Last Updated**: 2025-01-XX
