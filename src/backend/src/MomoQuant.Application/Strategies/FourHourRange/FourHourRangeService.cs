using System.Collections.Concurrent;
using System.Globalization;
using MomoQuant.Application.MarketData;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.MarketData;

namespace MomoQuant.Application.Strategies.FourHourRange;

public interface IFourHourRangeService
{
    Task<FourHourRangeDto> GetRangeForCandleAsync(
        long symbolId,
        Timeframe timeframe,
        DateTime candleTimeUtc,
        IReadOnlyList<Candle> visibleCandles,
        FourHourRangeReEntryParameters? parameters = null,
        CancellationToken cancellationToken = default);

    FourHourRangeDto GetRangeForCandle(
        long symbolId,
        Timeframe timeframe,
        DateTime candleTimeUtc,
        IReadOnlyList<Candle> visibleCandles,
        FourHourRangeReEntryParameters? parameters = null);

    IReadOnlyDictionary<string, FourHourRangeDto> BuildRangesFromCandles(
        long symbolId,
        Timeframe timeframe,
        IReadOnlyList<Candle> candles,
        FourHourRangeReEntryParameters? parameters = null);
}

public sealed class FourHourRangeService : IFourHourRangeService
{
    private readonly ConcurrentDictionary<string, FourHourRangeDto> _rangeCache = new(StringComparer.Ordinal);

    public Task<FourHourRangeDto> GetRangeForCandleAsync(
        long symbolId,
        Timeframe timeframe,
        DateTime candleTimeUtc,
        IReadOnlyList<Candle> visibleCandles,
        FourHourRangeReEntryParameters? parameters = null,
        CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        return Task.FromResult(GetRangeForCandle(symbolId, timeframe, candleTimeUtc, visibleCandles, parameters));
    }

    public FourHourRangeDto GetRangeForCandle(
        long symbolId,
        Timeframe timeframe,
        DateTime candleTimeUtc,
        IReadOnlyList<Candle> visibleCandles,
        FourHourRangeReEntryParameters? parameters = null)
    {
        parameters ??= new FourHourRangeReEntryParameters();
        var timezone = ResolveTimezone(parameters.AnchorTimezone);
        var candleUtc = EnsureUtc(candleTimeUtc);
        var candleNewYork = TimeZoneInfo.ConvertTimeFromUtc(candleUtc, timezone);
        var tradingDate = candleNewYork.Date;
        var rangeStartLocal = DateTime.SpecifyKind(tradingDate.AddHours(parameters.RangeStartHour), DateTimeKind.Unspecified);
        var rangeEndUtc = TimeZoneInfo.ConvertTimeToUtc(rangeStartLocal.AddHours(parameters.RangeDurationHours), timezone);
        var dayEndUtc = TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(tradingDate.AddDays(1), DateTimeKind.Unspecified),
            timezone);
        var expectedCount = Math.Max(1, parameters.RangeDurationHours * 60 / Math.Max((int)timeframe, 1));

        if (candleUtc < rangeEndUtc)
        {
            return BuildInvalid(
                symbolId,
                timeframe,
                tradingDate,
                TimeZoneInfo.ConvertTimeToUtc(rangeStartLocal, timezone),
                rangeEndUtc,
                dayEndUtc,
                expectedCount,
                0,
                "First 4H New York range has not closed yet.");
        }

        var cacheKey = BuildCacheKey(symbolId, timeframe, tradingDate, parameters);
        if (_rangeCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        var computed = ComputeRange(symbolId, timeframe, tradingDate, timezone, visibleCandles, parameters);
        _rangeCache[cacheKey] = computed;
        return computed;
    }

    public IReadOnlyDictionary<string, FourHourRangeDto> BuildRangesFromCandles(
        long symbolId,
        Timeframe timeframe,
        IReadOnlyList<Candle> candles,
        FourHourRangeReEntryParameters? parameters = null)
    {
        parameters ??= new FourHourRangeReEntryParameters();
        var timezone = ResolveTimezone(parameters.AnchorTimezone);
        var result = new Dictionary<string, FourHourRangeDto>(StringComparer.Ordinal);

        var tradingDates = candles
            .Where(candle => candle.SymbolId == symbolId && candle.Timeframe == timeframe && candle.IsClosed)
            .Select(candle => TimeZoneInfo.ConvertTimeFromUtc(EnsureUtc(candle.CloseTimeUtc), timezone).Date)
            .Distinct()
            .OrderBy(date => date);

        foreach (var tradingDate in tradingDates)
        {
            var cacheKey = BuildCacheKey(symbolId, timeframe, tradingDate, parameters);
            var range = ComputeRange(symbolId, timeframe, tradingDate, timezone, candles, parameters);
            _rangeCache[cacheKey] = range;
            result[tradingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)] = range;
        }

        return result;
    }

    private static FourHourRangeDto ComputeRange(
        long symbolId,
        Timeframe timeframe,
        DateTime tradingDate,
        TimeZoneInfo timezone,
        IReadOnlyList<Candle> visibleCandles,
        FourHourRangeReEntryParameters parameters)
    {
        var rangeStartLocal = DateTime.SpecifyKind(tradingDate.AddHours(parameters.RangeStartHour), DateTimeKind.Unspecified);
        var rangeEndLocal = rangeStartLocal.AddHours(parameters.RangeDurationHours);
        var dayEndLocal = DateTime.SpecifyKind(tradingDate.AddDays(1), DateTimeKind.Unspecified);
        var rangeStartUtc = TimeZoneInfo.ConvertTimeToUtc(rangeStartLocal, timezone);
        var rangeEndUtc = TimeZoneInfo.ConvertTimeToUtc(rangeEndLocal, timezone);
        var dayEndUtc = TimeZoneInfo.ConvertTimeToUtc(dayEndLocal, timezone);
        var expectedCount = Math.Max(1, parameters.RangeDurationHours * 60 / Math.Max((int)timeframe, 1));

        var rangeCandles = new List<Candle>(expectedCount);
        foreach (var candle in visibleCandles)
        {
            if (candle.SymbolId != symbolId || candle.Timeframe != timeframe || !candle.IsClosed)
            {
                continue;
            }

            var openUtc = EnsureUtc(candle.OpenTimeUtc);
            var closeUtc = EnsureUtc(candle.CloseTimeUtc);
            if (openUtc >= rangeStartUtc && closeUtc <= rangeEndUtc)
            {
                rangeCandles.Add(candle);
            }
        }

        rangeCandles.Sort(static (left, right) => EnsureUtc(left.OpenTimeUtc).CompareTo(EnsureUtc(right.OpenTimeUtc)));

        if (rangeCandles.Count < expectedCount)
        {
            return BuildInvalid(
                symbolId,
                timeframe,
                tradingDate,
                rangeStartUtc,
                rangeEndUtc,
                dayEndUtc,
                expectedCount,
                rangeCandles.Count,
                "Not enough candles to build first 4H New York range.");
        }

        var rangeHigh = rangeCandles.Max(candle => candle.High);
        var rangeLow = rangeCandles.Min(candle => candle.Low);
        var midpoint = (rangeHigh + rangeLow) / 2m;
        var rangePercent = midpoint > 0m ? (rangeHigh - rangeLow) / midpoint * 100m : 0m;

        return new FourHourRangeDto
        {
            SymbolId = symbolId,
            Timeframe = TimeframeParser.ToApiString(timeframe),
            NewYorkTradingDate = tradingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            RangeStartUtc = rangeStartUtc,
            RangeEndUtc = rangeEndUtc,
            NewYorkDayEndUtc = dayEndUtc,
            RangeHigh = rangeHigh,
            RangeLow = rangeLow,
            RangePercent = rangePercent,
            CandleCountUsed = rangeCandles.Count,
            ExpectedCandleCount = expectedCount,
            RangeReady = true,
            IsValid = true
        };
    }

    private static string BuildCacheKey(
        long symbolId,
        Timeframe timeframe,
        DateTime tradingDate,
        FourHourRangeReEntryParameters parameters) =>
        $"{symbolId}:{(int)timeframe}:{tradingDate:yyyy-MM-dd}:{parameters.RangeStartHour}:{parameters.RangeDurationHours}:{parameters.AnchorTimezone}";

    public static TimeZoneInfo ResolveTimezone(string timezoneId)
    {
        var candidates = new[]
        {
            timezoneId,
            string.Equals(timezoneId, "America/New_York", StringComparison.OrdinalIgnoreCase)
                ? "Eastern Standard Time"
                : "America/New_York"
        };

        foreach (var candidate in candidates.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(candidate);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        throw new TimeZoneNotFoundException($"Timezone '{timezoneId}' could not be resolved.");
    }

    public static DateTime EnsureUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc
            ? value
            : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static FourHourRangeDto BuildInvalid(
        long symbolId,
        Timeframe timeframe,
        DateTime tradingDate,
        DateTime rangeStartUtc,
        DateTime rangeEndUtc,
        DateTime dayEndUtc,
        int expectedCount,
        int candleCount,
        string reason) => new()
    {
        SymbolId = symbolId,
        Timeframe = TimeframeParser.ToApiString(timeframe),
        NewYorkTradingDate = tradingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        RangeStartUtc = rangeStartUtc,
        RangeEndUtc = rangeEndUtc,
        NewYorkDayEndUtc = dayEndUtc,
        ExpectedCandleCount = expectedCount,
        CandleCountUsed = candleCount,
        RangeReady = false,
        IsValid = false,
        InvalidReason = reason
    };
}
