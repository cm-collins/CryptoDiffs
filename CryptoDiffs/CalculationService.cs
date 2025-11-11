using Microsoft.Extensions.Logging;

namespace CryptoDiffs;

/// <summary>
/// Service for calculating price differences and metrics across multiple time periods.
/// Slices klines data by period, computes price changes, volatility, and additional insights.
/// </summary>
public class CalculationService
{
    private readonly ILogger<CalculationService> _logger;

    public CalculationService(ILogger<CalculationService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Calculates price differences for multiple periods from klines data.
    /// Efficiently slices data and computes all metrics for each period.
    /// </summary>
    /// <param name="klines">Historical klines data (must be ordered chronologically)</param>
    /// <param name="periods">List of periods in days to calculate</param>
    /// <param name="aggregate">Price aggregation method (close, open, avg, ohlc4)</param>
    /// <param name="asOf">Reference date (end of all periods, typically last closed candle)</param>
    /// <param name="interval">Candle interval (e.g., "1d") for date formatting</param>
    /// <returns>List of PeriodResult objects, one per requested period</returns>
    public List<PeriodResult> CalculatePeriodResults(
        List<Kline> klines,
        List<int> periods,
        string aggregate,
        DateTime asOf,
        string interval)
    {
        if (klines == null || klines.Count == 0)
        {
            _logger.LogWarning("No klines data provided for calculation");
            return new List<PeriodResult>();
        }

        if (periods == null || periods.Count == 0)
        {
            _logger.LogWarning("No periods specified for calculation");
            return new List<PeriodResult>();
        }

        // Ensure klines are sorted chronologically
        var sortedKlines = klines.OrderBy(k => k.OpenTime).ToList();
        var results = new List<PeriodResult>();

        // Calculate each period
        foreach (var periodDays in periods.OrderByDescending(p => p))
        {
            var result = CalculateSinglePeriod(
                sortedKlines,
                periodDays,
                aggregate,
                asOf,
                interval);

            if (result != null)
            {
                results.Add(result);
            }
        }

        _logger.LogInformation(
            "Calculated {Count} period results from {KlineCount} klines",
            results.Count, sortedKlines.Count);

        return results;
    }

    /// <summary>
    /// Calculates metrics for a single period by slicing klines data.
    /// </summary>
    private PeriodResult? CalculateSinglePeriod(
        List<Kline> klines,
        int periodDays,
        string aggregate,
        DateTime asOf,
        string interval)
    {
        // Find the end candle (closest to asOf date, but not after it)
        var asOfTimestamp = ((DateTimeOffset)asOf).ToUnixTimeMilliseconds();
        var endCandle = klines
            .Where(k => k.CloseTime <= asOfTimestamp)
            .OrderByDescending(k => k.CloseTime)
            .FirstOrDefault();

        if (endCandle == null)
        {
            _logger.LogWarning("No end candle found for period {PeriodDays} days", periodDays);
            return null;
        }

        // Calculate start time (periodDays before end candle)
        var startTimestamp = endCandle.CloseTime - (periodDays * 24L * 60 * 60 * 1000);

        // Find start candle (first candle at or before startTimestamp)
        var startCandle = klines
            .Where(k => k.OpenTime <= startTimestamp && k.CloseTime <= endCandle.CloseTime)
            .OrderBy(k => k.OpenTime)
            .FirstOrDefault();

        if (startCandle == null)
        {
            _logger.LogWarning(
                "No start candle found for period {PeriodDays} days (insufficient data)",
                periodDays);
            return null;
        }

        // Get all klines within the period window
        var periodKlines = klines
            .Where(k => k.OpenTime >= startCandle.OpenTime && k.CloseTime <= endCandle.CloseTime)
            .OrderBy(k => k.OpenTime)
            .ToList();

        if (periodKlines.Count == 0)
        {
            _logger.LogWarning("No klines found within period window");
            return null;
        }

        // Calculate prices using aggregate method
        var startPrice = startCandle.GetAggregatedPrice(aggregate);
        var endPrice = endCandle.GetAggregatedPrice(aggregate);

        // Calculate changes
        var absChange = endPrice - startPrice;
        var pctChange = startPrice != 0
            ? (absChange / startPrice) * 100
            : 0;

        // Find high and low within the period
        var high = periodKlines.Max(k => k.High);
        var low = periodKlines.Min(k => k.Low);

        // Calculate volatility (standard deviation of daily returns)
        var volatility = CalculateVolatility(periodKlines, aggregate);

        // Format dates
        var startDate = DateTimeOffset.FromUnixTimeMilliseconds(startCandle.OpenTime).UtcDateTime;
        var endDate = DateTimeOffset.FromUnixTimeMilliseconds(endCandle.CloseTime).UtcDateTime;

        return new PeriodResult
        {
            Days = periodDays,
            StartCandle = FormatDate(startDate, interval),
            EndCandle = FormatDate(endDate, interval),
            StartPrice = startPrice,
            EndPrice = endPrice,
            AbsChange = absChange,
            PctChange = Math.Round(pctChange, 4),
            High = high,
            Low = low,
            Volatility = volatility
        };
    }

    /// <summary>
    /// Calculates volatility as standard deviation of daily returns.
    /// Returns null if insufficient data for calculation.
    /// </summary>
    private decimal? CalculateVolatility(List<Kline> klines, string aggregate)
    {
        if (klines.Count < 2)
        {
            return null; // Need at least 2 candles to calculate returns
        }

        // Calculate daily returns: (Price_today - Price_yesterday) / Price_yesterday
        var returns = new List<decimal>();
        var sortedKlines = klines.OrderBy(k => k.OpenTime).ToList();

        for (int i = 1; i < sortedKlines.Count; i++)
        {
            var previousPrice = sortedKlines[i - 1].GetAggregatedPrice(aggregate);
            var currentPrice = sortedKlines[i].GetAggregatedPrice(aggregate);

            if (previousPrice != 0)
            {
                var dailyReturn = (currentPrice - previousPrice) / previousPrice;
                returns.Add(dailyReturn);
            }
        }

        if (returns.Count < 2)
        {
            return null; // Need at least 2 returns for standard deviation
        }

        // Calculate mean return
        var meanReturn = returns.Average();

        // Calculate variance: sum of squared deviations from mean
        var variance = returns.Sum(r => (r - meanReturn) * (r - meanReturn)) / returns.Count;

        // Standard deviation (volatility)
        var volatility = (decimal)Math.Sqrt((double)variance);

        // Annualize if daily data (multiply by sqrt(252 trading days))
        // For other intervals, this is a simple volatility proxy
        var annualizedVolatility = volatility * (decimal)Math.Sqrt(252);

        return Math.Round(annualizedVolatility * 100, 4); // Return as percentage
    }

    /// <summary>
    /// Formats date based on interval type.
    /// Returns "YYYY-MM-DD" for daily intervals, includes time for shorter intervals.
    /// </summary>
    private string FormatDate(DateTime date, string interval)
    {
        return interval.ToLower() switch
        {
            "1d" or "3d" or "1w" or "1M" => date.ToString("yyyy-MM-dd"),
            _ => date.ToString("yyyy-MM-dd HH:mm:ss")
        };
    }

    /// <summary>
    /// Validates that klines data is sufficient for the requested periods.
    /// </summary>
    public bool ValidateDataSufficiency(List<Kline> klines, List<int> periods, DateTime asOf)
    {
        if (klines == null || klines.Count == 0)
        {
            return false;
        }

        if (periods == null || periods.Count == 0)
        {
            return true; // No periods to validate
        }

        var maxPeriod = periods.Max();
        var asOfTimestamp = ((DateTimeOffset)asOf).ToUnixTimeMilliseconds();

        // Find oldest candle
        var oldestCandle = klines
            .Where(k => k.CloseTime <= asOfTimestamp)
            .OrderBy(k => k.OpenTime)
            .FirstOrDefault();

        if (oldestCandle == null)
        {
            return false;
        }

        // Calculate days between oldest candle and asOf date
        var oldestDate = DateTimeOffset.FromUnixTimeMilliseconds(oldestCandle.OpenTime).UtcDateTime;
        var daysAvailable = (asOf - oldestDate).TotalDays;

        var isSufficient = daysAvailable >= maxPeriod;

        if (!isSufficient)
        {
            _logger.LogWarning(
                "Insufficient data: {DaysAvailable} days available, {MaxPeriod} days required",
                daysAvailable, maxPeriod);
        }

        return isSufficient;
    }

    /// <summary>
    /// Gets additional insights for a period (max drawdown, best/worst day, etc.).
    /// Can be used for enhanced reporting.
    /// </summary>
    public Dictionary<string, object> GetPeriodInsights(List<Kline> periodKlines, string aggregate)
    {
        var insights = new Dictionary<string, object>();

        if (periodKlines == null || periodKlines.Count == 0)
        {
            return insights;
        }

        var sortedKlines = periodKlines.OrderBy(k => k.OpenTime).ToList();
        var prices = sortedKlines.Select(k => k.GetAggregatedPrice(aggregate)).ToList();

        // Max drawdown: largest peak-to-trough decline
        var maxDrawdown = CalculateMaxDrawdown(prices);
        insights["maxDrawdown"] = Math.Round(maxDrawdown, 4);

        // Best and worst single-day change
        if (sortedKlines.Count >= 2)
        {
            var dailyChanges = new List<(DateTime date, decimal change, decimal pctChange)>();
            for (int i = 1; i < sortedKlines.Count; i++)
            {
                var prevPrice = sortedKlines[i - 1].GetAggregatedPrice(aggregate);
                var currPrice = sortedKlines[i].GetAggregatedPrice(aggregate);
                var change = currPrice - prevPrice;
                var pctChange = prevPrice != 0 ? (change / prevPrice) * 100 : 0;

                var date = DateTimeOffset.FromUnixTimeMilliseconds(sortedKlines[i].OpenTime).UtcDateTime;
                dailyChanges.Add((date, change, pctChange));
            }

            if (dailyChanges.Any())
            {
                var bestDay = dailyChanges.OrderByDescending(d => d.pctChange).First();
                var worstDay = dailyChanges.OrderBy(d => d.pctChange).First();

                insights["bestDay"] = new
                {
                    date = bestDay.date.ToString("yyyy-MM-dd"),
                    change = Math.Round(bestDay.change, 2),
                    pctChange = Math.Round(bestDay.pctChange, 4)
                };

                insights["worstDay"] = new
                {
                    date = worstDay.date.ToString("yyyy-MM-dd"),
                    change = Math.Round(worstDay.change, 2),
                    pctChange = Math.Round(worstDay.pctChange, 4)
                };
            }
        }

        // Average daily volume
        var avgVolume = sortedKlines.Average(k => k.Volume);
        insights["avgVolume"] = Math.Round(avgVolume, 2);

        return insights;
    }

    /// <summary>
    /// Calculates maximum drawdown: the largest peak-to-trough decline in price.
    /// </summary>
    private decimal CalculateMaxDrawdown(List<decimal> prices)
    {
        if (prices == null || prices.Count < 2)
        {
            return 0;
        }

        decimal maxDrawdown = 0;
        decimal peak = prices[0];

        foreach (var price in prices)
        {
            if (price > peak)
            {
                peak = price;
            }

            var drawdown = (peak - price) / peak * 100;
            if (drawdown > maxDrawdown)
            {
                maxDrawdown = drawdown;
            }
        }

        return maxDrawdown;
    }
}

