using System.Text.RegularExpressions;

namespace CryptoDiffs;

/// <summary>
/// Validation utilities for CryptoDiffs application.
/// Provides validation methods for symbols, periods, dates, and other input parameters.
/// All validation methods return ValidationResult with success status and error messages.
/// </summary>
public static class Validation
{
    #region Constants

    /// <summary>
    /// Maximum number of days allowed per period.
    /// Set to 3650 days (approximately 10 years) to prevent excessive data requests.
    /// </summary>
    public const int MaxPeriodDays = 3650;

    /// <summary>
    /// Maximum number of periods allowed in a single request.
    /// Prevents excessive computation and response size.
    /// </summary>
    public const int MaxPeriodCount = 10;

    /// <summary>
    /// Minimum number of days allowed per period.
    /// Must be at least 1 day.
    /// </summary>
    public const int MinPeriodDays = 1;

    /// <summary>
    /// Regular expression pattern for validating trading pair symbols.
    /// Pattern: ^[A-Z]{3,10}USDT$
    /// - ^ : Start of string
    /// - [A-Z]{3,10} : 3 to 10 uppercase letters (base currency, e.g., BTC, ETH, SOL)
    /// - USDT : Must end with USDT (quote currency)
    /// - $ : End of string
    /// Examples: BTCUSDT, ETHUSDT, SOLUSDT, ADAUSDT
    /// </summary>
    private static readonly Regex SymbolPattern = new(@"^[A-Z]{3,10}USDT$", RegexOptions.Compiled);

    /// <summary>
    /// Valid price aggregation methods.
    /// </summary>
    private static readonly HashSet<string> ValidAggregateMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "close",  // Closing price (default)
        "open",   // Opening price
        "avg",    // Average of high and low
        "ohlc4"   // (Open + High + Low + Close) / 4
    };

    /// <summary>
    /// Valid Binance candle intervals.
    /// Common intervals supported by Binance Klines API.
    /// </summary>
    private static readonly HashSet<string> ValidIntervals = new(StringComparer.OrdinalIgnoreCase)
    {
        "1m", "3m", "5m", "15m", "30m",  // Minutes
        "1h", "2h", "4h", "6h", "8h", "12h",  // Hours
        "1d", "3d",  // Days
        "1w",  // Week
        "1M"   // Month
    };

    /// <summary>
    /// Valid report formats.
    /// </summary>
    private static readonly HashSet<string> ValidReportFormats = new(StringComparer.OrdinalIgnoreCase)
    {
        "json",   // JSON response (default)
        "excel",  // Excel file download
        "none"    // No report (email only)
    };

    #endregion

    #region Validation Result Model

    /// <summary>
    /// Result of a validation operation.
    /// Contains success status and optional error message.
    /// </summary>
    public class ValidationResult
    {
        /// <summary>
        /// Whether the validation passed.
        /// </summary>
        public bool IsValid { get; set; }

        /// <summary>
        /// Error message if validation failed.
        /// Null or empty if validation passed.
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// Creates a successful validation result.
        /// </summary>
        public static ValidationResult Success() => new() { IsValid = true };

        /// <summary>
        /// Creates a failed validation result with error message.
        /// </summary>
        public static ValidationResult Failure(string errorMessage) => new()
        {
            IsValid = false,
            ErrorMessage = errorMessage
        };
    }

    #endregion

    #region Symbol Validation

    /// <summary>
    /// Validates a trading pair symbol.
    /// Checks format, length, and ensures it ends with USDT.
    /// </summary>
    /// <param name="symbol">Symbol to validate (e.g., "BTCUSDT")</param>
    /// <returns>ValidationResult indicating success or failure with error message</returns>
    /// <example>
    /// ValidateSymbol("BTCUSDT") → Success
    /// ValidateSymbol("btcusdt") → Failure (must be uppercase)
    /// ValidateSymbol("BTC") → Failure (must end with USDT)
    /// ValidateSymbol("") → Failure (empty)
    /// </example>
    public static ValidationResult ValidateSymbol(string? symbol)
    {
        // Check if symbol is provided
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return ValidationResult.Failure("Symbol is required and cannot be empty.");
        }

        // Trim whitespace
        symbol = symbol.Trim().ToUpperInvariant();

        // Check length (minimum: 7 chars like "BTCUSDT", maximum: 14 chars like "ABCDEFGHIJUSDT")
        if (symbol.Length < 7 || symbol.Length > 14)
        {
            return ValidationResult.Failure(
                $"Symbol length must be between 7 and 14 characters. Received: {symbol} ({symbol.Length} chars)");
        }

        // Validate against regex pattern
        if (!SymbolPattern.IsMatch(symbol))
        {
            return ValidationResult.Failure(
                $"Invalid symbol format: '{symbol}'. Symbol must match pattern: ^[A-Z]{{3,10}}USDT$. " +
                $"Examples: BTCUSDT, ETHUSDT, SOLUSDT");
        }

        return ValidationResult.Success();
    }

    #endregion

    #region Period Validation

    /// <summary>
    /// Validates and parses a comma-separated list of periods.
    /// Each period must be a positive integer between MinPeriodDays and MaxPeriodDays.
    /// Maximum MaxPeriodCount periods allowed.
    /// </summary>
    /// <param name="periodsString">Comma-separated periods (e.g., "60,90" or "30,60,90")</param>
    /// <param name="parsedPeriods">Output: List of validated period integers</param>
    /// <returns>ValidationResult indicating success or failure with error message</returns>
    /// <example>
    /// ValidatePeriods("60,90", out periods) → Success, periods = [60, 90]
    /// ValidatePeriods("30,60,90,120", out periods) → Success, periods = [30, 60, 90, 120]
    /// ValidatePeriods("0,60", out periods) → Failure (period must be >= 1)
    /// ValidatePeriods("60,4000", out periods) → Failure (period exceeds max 3650 days)
    /// ValidatePeriods("1,2,3,4,5,6,7,8,9,10,11", out periods) → Failure (max 10 periods)
    /// </example>
    public static ValidationResult ValidatePeriods(string? periodsString, out List<int> parsedPeriods)
    {
        parsedPeriods = new List<int>();

        // Check if periods string is provided
        if (string.IsNullOrWhiteSpace(periodsString))
        {
            return ValidationResult.Failure("Periods are required and cannot be empty.");
        }

        // Split by comma and trim each value
        var periodStrings = periodsString.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim())
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .ToArray();

        // Check if we have any periods after splitting
        if (periodStrings.Length == 0)
        {
            return ValidationResult.Failure("At least one period must be specified.");
        }

        // Check maximum period count
        if (periodStrings.Length > MaxPeriodCount)
        {
            return ValidationResult.Failure(
                $"Maximum {MaxPeriodCount} periods allowed. Received {periodStrings.Length} periods.");
        }

        // Validate and parse each period
        var errors = new List<string>();
        var periods = new List<int>();

        for (int i = 0; i < periodStrings.Length; i++)
        {
            var periodStr = periodStrings[i];

            // Try to parse as integer
            if (!int.TryParse(periodStr, out int period))
            {
                errors.Add($"Period at position {i + 1} ('{periodStr}') is not a valid integer.");
                continue;
            }

            // Validate period range
            if (period < MinPeriodDays)
            {
                errors.Add($"Period at position {i + 1} ({period}) must be at least {MinPeriodDays} day(s).");
                continue;
            }

            if (period > MaxPeriodDays)
            {
                errors.Add(
                    $"Period at position {i + 1} ({period}) exceeds maximum of {MaxPeriodDays} days (approximately 10 years).");
                continue;
            }

            // Check for duplicates
            if (periods.Contains(period))
            {
                errors.Add($"Duplicate period {period} found. Each period should be unique.");
                continue;
            }

            periods.Add(period);
        }

        // If there were any errors, return failure
        if (errors.Count > 0)
        {
            return ValidationResult.Failure(string.Join(" ", errors));
        }

        // Sort periods for consistent output
        periods.Sort();
        parsedPeriods = periods;

        return ValidationResult.Success();
    }

    #endregion

    #region Date/Time Validation

    /// <summary>
    /// Validates and parses an ISO 8601 UTC timestamp string.
    /// Accepts formats like "2025-11-11T00:00:00Z" or "2025-11-11T00:00:00.000Z".
    /// </summary>
    /// <param name="dateTimeString">ISO 8601 UTC timestamp string</param>
    /// <param name="parsedDateTime">Output: Parsed DateTime in UTC</param>
    /// <returns>ValidationResult indicating success or failure with error message</returns>
    /// <example>
    /// ValidateAsOf("2025-11-11T00:00:00Z", out dt) → Success
    /// ValidateAsOf("2025-11-11T00:00:00.000Z", out dt) → Success
    /// ValidateAsOf("2025-11-11", out dt) → Failure (must include time)
    /// ValidateAsOf("invalid", out dt) → Failure (invalid format)
    /// </example>
    public static ValidationResult ValidateAsOf(string? dateTimeString, out DateTime parsedDateTime)
    {
        parsedDateTime = DateTime.MinValue;

        // If not provided, that's okay - we'll use last closed candle
        if (string.IsNullOrWhiteSpace(dateTimeString))
        {
            return ValidationResult.Success();
        }

        // Try parsing as ISO 8601 UTC format
        // Accept formats: "2025-11-11T00:00:00Z" or "2025-11-11T00:00:00.000Z"
        if (DateTime.TryParse(dateTimeString, null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime dt))
        {
            // Ensure it's in UTC
            if (dt.Kind == DateTimeKind.Unspecified)
            {
                // Assume UTC if not specified
                parsedDateTime = DateTime.SpecifyKind(dt, DateTimeKind.Utc);
            }
            else if (dt.Kind == DateTimeKind.Local)
            {
                // Convert local to UTC
                parsedDateTime = dt.ToUniversalTime();
            }
            else
            {
                parsedDateTime = dt;
            }

            // Validate date is not too far in the future (reasonable check)
            if (parsedDateTime > DateTime.UtcNow.AddDays(1))
            {
                return ValidationResult.Failure(
                    $"AsOf date cannot be more than 1 day in the future. Received: {dateTimeString}");
            }

            // Validate date is not too far in the past (Binance has data limits)
            if (parsedDateTime < DateTime.UtcNow.AddYears(-10))
            {
                return ValidationResult.Failure(
                    $"AsOf date cannot be more than 10 years in the past. Received: {dateTimeString}");
            }

            return ValidationResult.Success();
        }

        return ValidationResult.Failure(
            $"Invalid date format: '{dateTimeString}'. Expected ISO 8601 UTC format (e.g., '2025-11-11T00:00:00Z').");
    }

    #endregion

    #region Aggregate Method Validation

    /// <summary>
    /// Validates a price aggregation method.
    /// Must be one of: "close", "open", "avg", "ohlc4" (case-insensitive).
    /// </summary>
    /// <param name="aggregate">Aggregation method to validate</param>
    /// <returns>ValidationResult indicating success or failure with error message</returns>
    /// <example>
    /// ValidateAggregate("close") → Success
    /// ValidateAggregate("CLOSE") → Success (case-insensitive)
    /// ValidateAggregate("invalid") → Failure
    /// </example>
    public static ValidationResult ValidateAggregate(string? aggregate)
    {
        // If not provided, that's okay - we'll use default
        if (string.IsNullOrWhiteSpace(aggregate))
        {
            return ValidationResult.Success();
        }

        if (!ValidAggregateMethods.Contains(aggregate))
        {
            return ValidationResult.Failure(
                $"Invalid aggregate method: '{aggregate}'. Valid options are: {string.Join(", ", ValidAggregateMethods)}");
        }

        return ValidationResult.Success();
    }

    #endregion

    #region Interval Validation

    /// <summary>
    /// Validates a Binance candle interval.
    /// Must be a valid Binance interval (e.g., "1d", "1h", "4h").
    /// </summary>
    /// <param name="interval">Interval to validate</param>
    /// <returns>ValidationResult indicating success or failure with error message</returns>
    /// <example>
    /// ValidateInterval("1d") → Success
    /// ValidateInterval("1h") → Success
    /// ValidateInterval("invalid") → Failure
    /// </example>
    public static ValidationResult ValidateInterval(string? interval)
    {
        // If not provided, that's okay - we'll use default
        if (string.IsNullOrWhiteSpace(interval))
        {
            return ValidationResult.Success();
        }

        if (!ValidIntervals.Contains(interval))
        {
            return ValidationResult.Failure(
                $"Invalid interval: '{interval}'. Valid options are: {string.Join(", ", ValidIntervals.OrderBy(x => x))}");
        }

        return ValidationResult.Success();
    }

    #endregion

    #region Report Format Validation

    /// <summary>
    /// Validates a report format.
    /// Must be one of: "json", "excel", "none" (case-insensitive).
    /// </summary>
    /// <param name="report">Report format to validate</param>
    /// <returns>ValidationResult indicating success or failure with error message</returns>
    public static ValidationResult ValidateReportFormat(string? report)
    {
        // If not provided, that's okay - we'll use default
        if (string.IsNullOrWhiteSpace(report))
        {
            return ValidationResult.Success();
        }

        if (!ValidReportFormats.Contains(report))
        {
            return ValidationResult.Failure(
                $"Invalid report format: '{report}'. Valid options are: {string.Join(", ", ValidReportFormats)}");
        }

        return ValidationResult.Success();
    }

    #endregion

    #region Email Validation

    /// <summary>
    /// Validates an email address format.
    /// Uses a simple regex pattern to check basic email format.
    /// For production, consider using a more robust email validation library.
    /// </summary>
    /// <param name="email">Email address to validate</param>
    /// <returns>ValidationResult indicating success or failure with error message</returns>
    /// <example>
    /// ValidateEmail("user@example.com") → Success
    /// ValidateEmail("invalid") → Failure
    /// ValidateEmail("") → Failure
    /// </example>
    public static ValidationResult ValidateEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return ValidationResult.Failure("Email address is required and cannot be empty.");
        }

        // Simple email regex pattern
        // Pattern: basic email format with @ and domain
        var emailPattern = new Regex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        if (!emailPattern.IsMatch(email))
        {
            return ValidationResult.Failure($"Invalid email format: '{email}'. Expected format: user@example.com");
        }

        // Additional length check (RFC 5321 limit is 320 characters)
        if (email.Length > 320)
        {
            return ValidationResult.Failure($"Email address exceeds maximum length of 320 characters.");
        }

        return ValidationResult.Success();
    }

    #endregion

    #region Comprehensive Request Validation

    /// <summary>
    /// Validates a complete PriceDiffRequest object.
    /// Performs all individual validations and returns combined results.
    /// </summary>
    /// <param name="request">Request object to validate</param>
    /// <param name="parsedPeriods">Output: Parsed and validated periods list</param>
    /// <param name="parsedAsOf">Output: Parsed and validated asOf DateTime (if provided)</param>
    /// <returns>ValidationResult indicating success or failure with all error messages</returns>
    public static ValidationResult ValidateRequest(
        PriceDiffRequest request,
        out List<int> parsedPeriods,
        out DateTime? parsedAsOf)
    {
        parsedPeriods = new List<int>();
        parsedAsOf = null;

        var errors = new List<string>();

        // Validate symbol
        var symbolResult = ValidateSymbol(request.Symbol);
        if (!symbolResult.IsValid)
        {
            errors.Add(symbolResult.ErrorMessage!);
        }

        // Validate periods
        var periodsResult = ValidatePeriods(request.Periods, out parsedPeriods);
        if (!periodsResult.IsValid)
        {
            errors.Add(periodsResult.ErrorMessage!);
        }

        // Validate asOf date
        var asOfResult = ValidateAsOf(request.AsOf, out DateTime asOf);
        if (!asOfResult.IsValid)
        {
            errors.Add(asOfResult.ErrorMessage!);
        }
        else if (!string.IsNullOrWhiteSpace(request.AsOf))
        {
            parsedAsOf = asOf;
        }

        // Validate interval
        var intervalResult = ValidateInterval(request.Interval);
        if (!intervalResult.IsValid)
        {
            errors.Add(intervalResult.ErrorMessage!);
        }

        // Validate aggregate
        var aggregateResult = ValidateAggregate(request.Aggregate);
        if (!aggregateResult.IsValid)
        {
            errors.Add(aggregateResult.ErrorMessage!);
        }

        // Validate report format
        var reportResult = ValidateReportFormat(request.Report);
        if (!reportResult.IsValid)
        {
            errors.Add(reportResult.ErrorMessage!);
        }

        // If email is requested, validate email addresses (if provided in request)
        // Note: Actual email addresses come from configuration, not request

        // Return combined result
        if (errors.Count > 0)
        {
            return ValidationResult.Failure(string.Join(" ", errors));
        }

        return ValidationResult.Success();
    }

    #endregion
}

