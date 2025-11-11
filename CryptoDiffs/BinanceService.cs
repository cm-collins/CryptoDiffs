using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace CryptoDiffs;

/// <summary>
/// Service for fetching historical klines (OHLCV) data from Binance API.
/// Handles retries, caching, and ensures UTC-only timestamps with last closed candle logic.
/// </summary>
public class BinanceService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<BinanceService> _logger;
    private readonly IConfiguration _configuration;
    private readonly ConcurrentDictionary<string, KlineCacheEntry> _cache;
    private readonly bool _cacheEnabled;
    private readonly int _cacheTtlSeconds;
    private const int MaxRetries = 3;
    private const int BaseDelayMs = 1000;

    public BinanceService(
        HttpClient httpClient,
        ILogger<BinanceService> logger,
        IConfiguration configuration)
    {
        _httpClient = httpClient;
        _logger = logger;
        _configuration = configuration;
        _cache = new ConcurrentDictionary<string, KlineCacheEntry>();

        // Configure base URL
        var baseUrl = _configuration["BINANCE_BASE_URL"] ?? "https://api.binance.com";
        _httpClient.BaseAddress = new Uri(baseUrl);

        // Cache configuration
        _cacheEnabled = bool.Parse(_configuration["ENABLE_CACHE"] ?? "true");
        _cacheTtlSeconds = int.Parse(_configuration["CACHE_TTL_SECONDS"] ?? "300");
    }

    /// <summary>
    /// Fetches klines for the specified symbol and interval, covering the maximum period.
    /// Returns only fully closed candles (excludes the current incomplete candle).
    /// </summary>
    /// <param name="symbol">Trading pair symbol (e.g., "BTCUSDT")</param>
    /// <param name="interval">Candle interval (e.g., "1d")</param>
    /// <param name="maxPeriodDays">Maximum period in days to fetch (determines lookback window)</param>
    /// <param name="asOf">Optional reference date (UTC). If null, uses last closed candle.</param>
    /// <returns>List of klines ordered chronologically, oldest to newest</returns>
    public async Task<List<Kline>> GetKlinesAsync(
        string symbol,
        string interval,
        int maxPeriodDays,
        DateTime? asOf = null)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var cacheKey = $"{symbol}_{interval}";

        try
        {
            // Check cache first
            if (_cacheEnabled && _cache.TryGetValue(cacheKey, out var cachedEntry))
            {
                if (cachedEntry.IsValid(_cacheTtlSeconds))
                {
                    _logger.LogInformation("Cache hit for {Symbol} {Interval}", symbol, interval);
                    LogMetric("cache_hit", 1);
                    return cachedEntry.Klines;
                }
                _cache.TryRemove(cacheKey, out _);
            }

            // Determine end time: use last fully closed candle (UTC)
            var endTime = asOf ?? GetLastClosedCandleTime(interval);
            var startTime = endTime.AddDays(-maxPeriodDays - 1); // Add buffer for safety

            // Fetch from Binance API with retry logic
            var klines = await FetchKlinesWithRetryAsync(symbol, interval, startTime, endTime);

            // Filter to only fully closed candles (exclude current incomplete candle)
            var closedKlines = FilterClosedCandles(klines, endTime, interval);

            // Update cache
            if (_cacheEnabled && closedKlines.Count > 0)
            {
                _cache[cacheKey] = new KlineCacheEntry
                {
                    Symbol = symbol,
                    Interval = interval,
                    Klines = closedKlines,
                    CachedAt = DateTime.UtcNow
                };
            }

            stopwatch.Stop();
            LogMetric("binance_latency_ms", stopwatch.ElapsedMilliseconds);
            LogMetric("binance_status", 200);

            _logger.LogInformation(
                "Fetched {Count} closed klines for {Symbol} {Interval} in {ElapsedMs}ms",
                closedKlines.Count, symbol, interval, stopwatch.ElapsedMilliseconds);

            return closedKlines;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            LogMetric("binance_status", 500);
            _logger.LogError(ex, "Failed to fetch klines for {Symbol} {Interval}", symbol, interval);
            throw;
        }
    }

    /// <summary>
    /// Calculates the timestamp of the last fully closed candle for the given interval.
    /// For daily candles, this is yesterday at 00:00 UTC.
    /// </summary>
    private DateTime GetLastClosedCandleTime(string interval)
    {
        var now = DateTime.UtcNow;

        return interval.ToLower() switch
        {
            "1d" => new DateTime(now.Year, now.Month, now.Day, 0, 0, 0, DateTimeKind.Utc).AddDays(-1),
            "1h" => new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, DateTimeKind.Utc).AddHours(-1),
            "4h" => new DateTime(now.Year, now.Month, now.Day, (now.Hour / 4) * 4, 0, 0, DateTimeKind.Utc).AddHours(-4),
            _ => now.AddDays(-1) // Default: yesterday
        };
    }

    /// <summary>
    /// Filters klines to include only fully closed candles up to (but not including) the asOf time.
    /// </summary>
    private List<Kline> FilterClosedCandles(List<Kline> klines, DateTime asOf, string interval)
    {
        var asOfTimestamp = ((DateTimeOffset)asOf).ToUnixTimeMilliseconds();

        return klines
            .Where(k => k.CloseTime <= asOfTimestamp)
            .OrderBy(k => k.OpenTime)
            .ToList();
    }

    /// <summary>
    /// Fetches klines from Binance API with exponential backoff retry for 429/5xx errors.
    /// </summary>
    private async Task<List<Kline>> FetchKlinesWithRetryAsync(
        string symbol,
        string interval,
        DateTime startTime,
        DateTime endTime)
    {
        var startTimestamp = ((DateTimeOffset)startTime).ToUnixTimeMilliseconds();
        var endTimestamp = ((DateTimeOffset)endTime).ToUnixTimeMilliseconds();

        for (int attempt = 0; attempt < MaxRetries; attempt++)
        {
            try
            {
                var url = $"/api/v3/klines?symbol={symbol}&interval={interval}" +
                         $"&startTime={startTimestamp}&endTime={endTimestamp}&limit=1000";

                _logger.LogDebug("Fetching klines: {Url} (attempt {Attempt})", url, attempt + 1);

                var response = await _httpClient.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var klines = ParseKlinesResponse(json, symbol, interval);
                    return klines;
                }

                // Handle rate limiting (429) and server errors (5xx)
                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests ||
                    ((int)response.StatusCode >= 500 && (int)response.StatusCode < 600))
                {
                    if (attempt < MaxRetries - 1)
                    {
                        var delay = BaseDelayMs * (int)Math.Pow(2, attempt); // Exponential backoff
                        _logger.LogWarning(
                            "Binance API returned {StatusCode}, retrying in {DelayMs}ms (attempt {Attempt}/{MaxRetries})",
                            response.StatusCode, delay, attempt + 1, MaxRetries);

                        await Task.Delay(delay);
                        continue;
                    }
                }

                // Non-retryable error or max retries reached
                var errorContent = await response.Content.ReadAsStringAsync();
                throw new HttpRequestException(
                    $"Binance API error: {response.StatusCode}. Response: {errorContent}");
            }
            catch (HttpRequestException) when (attempt < MaxRetries - 1)
            {
                // Network errors: retry with exponential backoff
                var delay = BaseDelayMs * (int)Math.Pow(2, attempt);
                _logger.LogWarning(
                    "Network error fetching klines, retrying in {DelayMs}ms (attempt {Attempt}/{MaxRetries})",
                    delay, attempt + 1, MaxRetries);

                await Task.Delay(delay);
            }
        }

        throw new HttpRequestException($"Failed to fetch klines after {MaxRetries} attempts");
    }

    /// <summary>
    /// Parses Binance API response (array of arrays) into List&lt;Kline&gt;.
    /// Binance returns: [[openTime, open, high, low, close, volume, closeTime, ...], ...]
    /// </summary>
    private List<Kline> ParseKlinesResponse(string json, string symbol, string interval)
    {
        try
        {
            var jsonArray = JsonDocument.Parse(json).RootElement;
            var klines = new List<Kline>();

            foreach (var item in jsonArray.EnumerateArray())
            {
                var arr = item.EnumerateArray().ToArray();
                if (arr.Length < 11) continue;

                var kline = new Kline
                {
                    OpenTime = arr[0].GetInt64(),
                    Open = decimal.Parse(arr[1].GetString() ?? "0"),
                    High = decimal.Parse(arr[2].GetString() ?? "0"),
                    Low = decimal.Parse(arr[3].GetString() ?? "0"),
                    Close = decimal.Parse(arr[4].GetString() ?? "0"),
                    Volume = decimal.Parse(arr[5].GetString() ?? "0"),
                    CloseTime = arr[6].GetInt64(),
                    QuoteVolume = decimal.Parse(arr[7].GetString() ?? "0"),
                    TradeCount = arr[8].GetInt32(),
                    TakerBuyBaseVolume = decimal.Parse(arr[9].GetString() ?? "0"),
                    TakerBuyQuoteVolume = decimal.Parse(arr[10].GetString() ?? "0")
                };

                klines.Add(kline);
            }

            return klines;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse Binance klines response for {Symbol}", symbol);
            throw new InvalidOperationException($"Failed to parse Binance API response: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Logs a metric value for Application Insights.
    /// </summary>
    private void LogMetric(string metricName, double value)
    {
        // Application Insights will automatically track these if configured
        _logger.LogInformation("Metric: {MetricName} = {Value}", metricName, value);
    }
}

