namespace CryptoDiffs;

/// <summary>
/// Data Transfer Objects (DTOs) for the CryptoDiffs application.
/// Contains all request, response, and data models used throughout the application.
/// </summary>

#region Request Models

/// <summary>
/// Request model for HTTP trigger function.
/// Represents the input parameters for calculating price differences.
/// Can be populated from query string (GET) or JSON body (POST).
/// </summary>
public class PriceDiffRequest
{
    /// <summary>
    /// Trading pair symbol (e.g., "BTCUSDT", "ETHUSDT").
    /// Must match pattern: ^[A-Z]{3,10}USDT$
    /// </summary>
    public string? Symbol { get; set; }

    /// <summary>
    /// Comma-separated list of periods in days (e.g., "60,90").
    /// Each period represents the number of days to look back from the asOf date.
    /// Maximum 10 periods, each period max 3650 days.
    /// </summary>
    public string? Periods { get; set; }

    /// <summary>
    /// ISO 8601 UTC timestamp for the reference date (e.g., "2025-11-11T00:00:00Z").
    /// If not provided, uses the last fully closed daily candle.
    /// All calculations are based on UTC to avoid timezone issues.
    /// </summary>
    public string? AsOf { get; set; }

    /// <summary>
    /// Candle interval for Binance API (e.g., "1d", "1h", "4h").
    /// Default: "1d" (daily candles).
    /// Common values: 1m, 3m, 5m, 15m, 30m, 1h, 2h, 4h, 6h, 8h, 12h, 1d, 3d, 1w, 1M
    /// </summary>
    public string? Interval { get; set; }

    /// <summary>
    /// Price aggregation method to use for calculations.
    /// Options: "close" (default), "open", "avg", "ohlc4"
    /// - close: Closing price of the candle
    /// - open: Opening price of the candle
    /// - avg: Average of high and low prices
    /// - ohlc4: (Open + High + Low + Close) / 4
    /// </summary>
    public string? Aggregate { get; set; }

    /// <summary>
    /// Whether to send the report via email.
    /// true: Send email with Excel attachment
    /// false: Return response only (default)
    /// </summary>
    public bool? Email { get; set; }

    /// <summary>
    /// Report format to return.
    /// Options: "json" (default), "excel", "none"
    /// - json: Return JSON response with calculated metrics
    /// - excel: Return Excel file (.xlsx) as binary
    /// - none: No report (useful when only email is needed)
    /// </summary>
    public string? Report { get; set; }
}

#endregion

#region Response Models

/// <summary>
/// Main response model for price difference calculations.
/// Contains metadata about the calculation and an array of results for each period.
/// </summary>
public class PriceDiffResponse
{
    /// <summary>
    /// Trading pair symbol that was analyzed (e.g., "BTCUSDT").
    /// </summary>
    public string Symbol { get; set; } = string.Empty;

    /// <summary>
    /// ISO 8601 UTC timestamp of the reference date used for calculations.
    /// Represents the "as of" date - typically the last fully closed candle.
    /// </summary>
    public DateTime AsOf { get; set; }

    /// <summary>
    /// Candle interval used for fetching data (e.g., "1d").
    /// </summary>
    public string Interval { get; set; } = string.Empty;

    /// <summary>
    /// Price aggregation method used (e.g., "close", "avg").
    /// </summary>
    public string Aggregate { get; set; } = string.Empty;

    /// <summary>
    /// Array of calculation results, one entry per requested period.
    /// Each result contains metrics for that specific time period.
    /// </summary>
    public List<PeriodResult> Results { get; set; } = new();

    /// <summary>
    /// Optional informational notes about the calculation.
    /// May include warnings, data quality notes, or methodology explanations.
    /// </summary>
    public List<string> Notes { get; set; } = new();
}

/// <summary>
/// Result for a single period calculation.
/// Contains all metrics calculated for a specific time period (e.g., 60 days, 90 days).
/// </summary>
public class PeriodResult
{
    /// <summary>
    /// Number of days in this period (e.g., 60, 90).
    /// </summary>
    public int Days { get; set; }

    /// <summary>
    /// Date string of the start candle (beginning of the period).
    /// Format: "YYYY-MM-DD"
    /// </summary>
    public string StartCandle { get; set; } = string.Empty;

    /// <summary>
    /// Date string of the end candle (end of the period, typically the asOf date).
    /// Format: "YYYY-MM-DD"
    /// </summary>
    public string EndCandle { get; set; } = string.Empty;

    /// <summary>
    /// Price at the beginning of the period (start candle).
    /// Value depends on the aggregate method used.
    /// </summary>
    public decimal StartPrice { get; set; }

    /// <summary>
    /// Price at the end of the period (end candle).
    /// Value depends on the aggregate method used.
    /// </summary>
    public decimal EndPrice { get; set; }

    /// <summary>
    /// Absolute change in price: EndPrice - StartPrice.
    /// Positive value indicates price increase, negative indicates decrease.
    /// </summary>
    public decimal AbsChange { get; set; }

    /// <summary>
    /// Percentage change: ((EndPrice - StartPrice) / StartPrice) * 100.
    /// Positive value indicates price increase, negative indicates decrease.
    /// </summary>
    public decimal PctChange { get; set; }

    /// <summary>
    /// Highest price within the period window.
    /// Useful for understanding the maximum price reached during the period.
    /// </summary>
    public decimal High { get; set; }

    /// <summary>
    /// Lowest price within the period window.
    /// Useful for understanding the minimum price reached during the period.
    /// </summary>
    public decimal Low { get; set; }

    /// <summary>
    /// Optional: Standard deviation of daily returns (volatility proxy).
    /// Higher values indicate more price volatility during the period.
    /// </summary>
    public decimal? Volatility { get; set; }
}

/// <summary>
/// Error response model for API errors.
/// Provides structured error information to clients.
/// </summary>
public class ErrorResponse
{
    /// <summary>
    /// Error message describing what went wrong.
    /// Should be user-friendly and actionable.
    /// </summary>
    public string Error { get; set; } = string.Empty;

    /// <summary>
    /// Optional detailed error information.
    /// May include stack traces in development, or additional context in production.
    /// </summary>
    public string? Details { get; set; }

    /// <summary>
    /// Optional error code for programmatic error handling.
    /// Examples: "INVALID_SYMBOL", "BINANCE_API_ERROR", "VALIDATION_ERROR"
    /// </summary>
    public string? ErrorCode { get; set; }
}

#endregion

#region Binance API Models

/// <summary>
/// Represents a single kline (candlestick) from Binance API.
/// Contains OHLCV (Open, High, Low, Close, Volume) data for a specific time period.
/// </summary>
public class Kline
{
    /// <summary>
    /// Opening timestamp of the candle (Unix milliseconds).
    /// </summary>
    public long OpenTime { get; set; }

    /// <summary>
    /// Opening price of the candle.
    /// </summary>
    public decimal Open { get; set; }

    /// <summary>
    /// Highest price during the candle period.
    /// </summary>
    public decimal High { get; set; }

    /// <summary>
    /// Lowest price during the candle period.
    /// </summary>
    public decimal Low { get; set; }

    /// <summary>
    /// Closing price of the candle.
    /// </summary>
    public decimal Close { get; set; }

    /// <summary>
    /// Trading volume during the candle period.
    /// </summary>
    public decimal Volume { get; set; }

    /// <summary>
    /// Closing timestamp of the candle (Unix milliseconds).
    /// </summary>
    public long CloseTime { get; set; }

    /// <summary>
    /// Quote asset volume (volume in USDT for USDT pairs).
    /// </summary>
    public decimal QuoteVolume { get; set; }

    /// <summary>
    /// Number of trades during the candle period.
    /// </summary>
    public int TradeCount { get; set; }

    /// <summary>
    /// Taker buy base asset volume.
    /// </summary>
    public decimal TakerBuyBaseVolume { get; set; }

    /// <summary>
    /// Taker buy quote asset volume.
    /// </summary>
    public decimal TakerBuyQuoteVolume { get; set; }

    /// <summary>
    /// Gets the aggregated price based on the specified method.
    /// </summary>
    /// <param name="aggregate">Aggregation method: "close", "open", "avg", "ohlc4"</param>
    /// <returns>Aggregated price value</returns>
    public decimal GetAggregatedPrice(string aggregate)
    {
        return aggregate.ToLower() switch
        {
            "close" => Close,
            "open" => Open,
            "avg" => (High + Low) / 2,
            "ohlc4" => (Open + High + Low + Close) / 4,
            _ => Close // Default to close price
        };
    }
}

/// <summary>
/// Response model from Binance Klines API.
/// Contains an array of klines for the requested symbol and time range.
/// </summary>
public class BinanceKlinesResponse
{
    /// <summary>
    /// Array of kline data points.
    /// Ordered chronologically from oldest to newest.
    /// </summary>
    public List<Kline> Klines { get; set; } = new();

    /// <summary>
    /// Symbol that was queried (e.g., "BTCUSDT").
    /// </summary>
    public string Symbol { get; set; } = string.Empty;

    /// <summary>
    /// Interval used for the klines (e.g., "1d").
    /// </summary>
    public string Interval { get; set; } = string.Empty;
}

#endregion

#region Email Models

/// <summary>
/// Model for email sending operations.
/// Contains all information needed to send an email with an Excel attachment.
/// </summary>
public class EmailRequest
{
    /// <summary>
    /// Recipient email address(es). Comma-separated for multiple recipients.
    /// Must be valid email format(s).
    /// </summary>
    public string To { get; set; } = string.Empty;

    /// <summary>
    /// CC (Carbon Copy) email address(es). Comma-separated for multiple recipients.
    /// Optional.
    /// </summary>
    public string? Cc { get; set; }

    /// <summary>
    /// BCC (Blind Carbon Copy) email address(es). Comma-separated for multiple recipients.
    /// Optional.
    /// </summary>
    public string? Bcc { get; set; }

    /// <summary>
    /// Sender email address.
    /// Must match the authenticated Microsoft account.
    /// </summary>
    public string From { get; set; } = string.Empty;

    /// <summary>
    /// Email subject line.
    /// Example: "CryptoDiffs: BTCUSDT (60d,90d) – 2025-11-11 UTC"
    /// </summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>
    /// Email body text (plain text or HTML).
    /// </summary>
    public string Body { get; set; } = string.Empty;

    /// <summary>
    /// Excel file attachment as byte array.
    /// Maximum size: 25 MB (Microsoft Graph limit).
    /// </summary>
    public byte[]? Attachment { get; set; }

    /// <summary>
    /// Attachment filename (e.g., "cryptodiffs-report.xlsx").
    /// </summary>
    public string? AttachmentFileName { get; set; }
}

/// <summary>
/// Response model for email sending operations.
/// </summary>
public class EmailResponse
{
    /// <summary>
    /// Whether the email was sent successfully.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Microsoft Graph message ID if email was sent successfully.
    /// Can be used for tracking or logging purposes.
    /// </summary>
    public string? MessageId { get; set; }

    /// <summary>
    /// Error message if sending failed.
    /// </summary>
    public string? Error { get; set; }
}

#endregion

#region Cache Models

/// <summary>
/// Cache entry for Binance klines data.
/// Used to reduce API calls by caching recent kline fetches.
/// </summary>
public class KlineCacheEntry
{
    /// <summary>
    /// Symbol that was cached (e.g., "BTCUSDT").
    /// </summary>
    public string Symbol { get; set; } = string.Empty;

    /// <summary>
    /// Interval used (e.g., "1d").
    /// </summary>
    public string Interval { get; set; } = string.Empty;

    /// <summary>
    /// Cached klines data.
    /// </summary>
    public List<Kline> Klines { get; set; } = new();

    /// <summary>
    /// Timestamp when this cache entry was created.
    /// Used to determine if cache is still valid based on TTL.
    /// </summary>
    public DateTime CachedAt { get; set; }

    /// <summary>
    /// Checks if the cache entry is still valid based on TTL.
    /// </summary>
    /// <param name="ttlSeconds">Time-to-live in seconds (default: 300 = 5 minutes)</param>
    /// <returns>True if cache is still valid, false if expired</returns>
    public bool IsValid(int ttlSeconds = 300)
    {
        return DateTime.UtcNow.Subtract(CachedAt).TotalSeconds < ttlSeconds;
    }
}

#endregion

#region Application Event Models

/// <summary>
/// Model for Application Insights custom events.
/// Used for tracking and observability.
/// </summary>
public class PriceDiffComputedEvent
{
    /// <summary>
    /// Symbol that was analyzed.
    /// </summary>
    public string Symbol { get; set; } = string.Empty;

    /// <summary>
    /// List of periods that were calculated.
    /// </summary>
    public List<int> Periods { get; set; } = new();

    /// <summary>
    /// Trigger type: "http" or "timer".
    /// Indicates how the calculation was initiated.
    /// </summary>
    public string Trigger { get; set; } = string.Empty;

    /// <summary>
    /// Timestamp when the calculation was performed.
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

#endregion

#region Cosmos DB Logging Models

/// <summary>
/// Base log entry for Cosmos DB.
/// All log entries inherit from this for consistent structure.
/// </summary>
public abstract class CosmosLogEntry
{
    /// <summary>
    /// Unique ID for the log entry (Cosmos DB document ID).
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Tenant ID for Cosmos DB partition key.
    /// </summary>
    public string TenantId { get; set; } = "default";

    /// <summary>
    /// Type of log entry (e.g., "ExecutionLog", "StageLog", "ErrorLog").
    /// </summary>
    public string LogType { get; set; } = string.Empty;

    /// <summary>
    /// Timestamp when the log entry was created (UTC).
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Execution ID that groups related log entries together.
    /// </summary>
    public string ExecutionId { get; set; } = Guid.NewGuid().ToString();
}

/// <summary>
/// Main execution log entry for HTTP or Timer triggers.
/// Tracks the entire execution flow from start to finish.
/// </summary>
public class ExecutionLogEntry : CosmosLogEntry
{
    public ExecutionLogEntry()
    {
        LogType = "ExecutionLog";
    }

    /// <summary>
    /// Trigger type: "http" or "timer".
    /// </summary>
    public string Trigger { get; set; } = string.Empty;

    /// <summary>
    /// HTTP method if triggered via HTTP (GET, POST).
    /// </summary>
    public string? HttpMethod { get; set; }

    /// <summary>
    /// Request parameters received.
    /// </summary>
    public PriceDiffRequest? Request { get; set; }

    /// <summary>
    /// Overall execution status: "Success", "Failed", "Partial".
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// Total execution duration in milliseconds.
    /// </summary>
    public long DurationMs { get; set; }

    /// <summary>
    /// Symbol that was processed.
    /// </summary>
    public string? Symbol { get; set; }

    /// <summary>
    /// Periods that were calculated.
    /// </summary>
    public List<int>? Periods { get; set; }

    /// <summary>
    /// Final response data (if successful).
    /// </summary>
    public PriceDiffResponse? Response { get; set; }

    /// <summary>
    /// Error information if execution failed.
    /// </summary>
    public ErrorInfo? Error { get; set; }

    /// <summary>
    /// List of stage log IDs for this execution.
    /// </summary>
    public List<string> StageLogIds { get; set; } = new();

    /// <summary>
    /// Email sending information.
    /// </summary>
    public EmailLogInfo? EmailInfo { get; set; }

    /// <summary>
    /// Additional metadata for this execution.
    /// </summary>
    public Dictionary<string, object>? Metadata { get; set; }
}

/// <summary>
/// Stage log entry for individual operation stages.
/// Tracks each step in the execution pipeline.
/// </summary>
public class StageLogEntry : CosmosLogEntry
{
    public StageLogEntry()
    {
        LogType = "StageLog";
    }

    /// <summary>
    /// Stage name (e.g., "RequestParsing", "Validation", "BinanceFetch", "Calculation", "ExcelGeneration", "EmailSending").
    /// </summary>
    public string StageName { get; set; } = string.Empty;

    /// <summary>
    /// Stage status: "Success", "Failed", "Skipped".
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// Stage duration in milliseconds.
    /// </summary>
    public long DurationMs { get; set; }

    /// <summary>
    /// Input data for this stage.
    /// </summary>
    public Dictionary<string, object>? InputData { get; set; }

    /// <summary>
    /// Output data from this stage.
    /// </summary>
    public Dictionary<string, object>? OutputData { get; set; }

    /// <summary>
    /// Error information if stage failed.
    /// </summary>
    public ErrorInfo? Error { get; set; }

    /// <summary>
    /// Additional metadata for this stage.
    /// </summary>
    public Dictionary<string, object>? Metadata { get; set; }
}

/// <summary>
/// Error information structure.
/// </summary>
public class ErrorInfo
{
    /// <summary>
    /// Error message.
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Error type/exception name.
    /// </summary>
    public string? ErrorType { get; set; }

    /// <summary>
    /// Stack trace (if available).
    /// </summary>
    public string? StackTrace { get; set; }

    /// <summary>
    /// Inner error information.
    /// </summary>
    public ErrorInfo? InnerError { get; set; }
}

/// <summary>
/// Email logging information.
/// </summary>
public class EmailLogInfo
{
    /// <summary>
    /// Recipient email addresses (To).
    /// </summary>
    public string To { get; set; } = string.Empty;

    /// <summary>
    /// CC recipient email addresses.
    /// </summary>
    public string? Cc { get; set; }

    /// <summary>
    /// BCC recipient email addresses.
    /// </summary>
    public string? Bcc { get; set; }

    /// <summary>
    /// Sender email address.
    /// </summary>
    public string From { get; set; } = string.Empty;

    /// <summary>
    /// Email subject.
    /// </summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>
    /// Whether email was sent successfully.
    /// </summary>
    public bool Sent { get; set; }

    /// <summary>
    /// Microsoft Graph message ID (if sent).
    /// </summary>
    public string? MessageId { get; set; }

    /// <summary>
    /// Attachment filename.
    /// </summary>
    public string? AttachmentFileName { get; set; }

    /// <summary>
    /// Attachment size in bytes.
    /// </summary>
    public long? AttachmentSizeBytes { get; set; }

    /// <summary>
    /// Error message if email sending failed.
    /// </summary>
    public string? Error { get; set; }
}

#endregion

