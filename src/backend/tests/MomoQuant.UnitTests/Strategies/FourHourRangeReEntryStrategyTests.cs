using System.Text.Json;
using MomoQuant.Application.Strategies.FourHourRange;
using MomoQuant.Application.Strategies.Implementations;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Indicators;
using MomoQuant.Domain.MarketData;
using MomoQuant.Domain.Strategies;

namespace MomoQuant.UnitTests.Strategies;

public class FourHourRangeServiceTests
{
    [Fact]
    public void GetRangeForCandle_BuildsFirstFourHourNewYorkRange()
    {
        var service = new FourHourRangeService();
        var candles = BuildRangeCandles(Timeframe.M5, new DateTime(2026, 7, 1, 4, 0, 0, DateTimeKind.Utc));

        var range = service.GetRangeForCandle(
            1,
            Timeframe.M5,
            new DateTime(2026, 7, 1, 8, 5, 0, DateTimeKind.Utc),
            candles);

        Assert.True(range.IsValid);
        Assert.True(range.RangeReady);
        Assert.Equal("2026-07-01", range.NewYorkTradingDate);
        Assert.Equal(new DateTime(2026, 7, 1, 4, 0, 0, DateTimeKind.Utc), range.RangeStartUtc);
        Assert.Equal(new DateTime(2026, 7, 1, 8, 0, 0, DateTimeKind.Utc), range.RangeEndUtc);
        Assert.Equal(48, range.ExpectedCandleCount);
        Assert.Equal(48, range.CandleCountUsed);
        Assert.Equal(101m, range.RangeHigh);
        Assert.Equal(99m, range.RangeLow);
    }

    [Fact]
    public void GetRangeForCandle_ReturnsInvalidBeforeRangeClose()
    {
        var service = new FourHourRangeService();
        var candles = BuildRangeCandles(Timeframe.M5, new DateTime(2026, 7, 1, 4, 0, 0, DateTimeKind.Utc))
            .Take(40)
            .ToList();

        var range = service.GetRangeForCandle(
            1,
            Timeframe.M5,
            new DateTime(2026, 7, 1, 7, 30, 0, DateTimeKind.Utc),
            candles);

        Assert.False(range.IsValid);
        Assert.Contains("not closed", range.InvalidReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetRangeForCandle_ReturnsInvalidWhenCandlesAreMissing()
    {
        var service = new FourHourRangeService();
        var candles = BuildRangeCandles(Timeframe.M5, new DateTime(2026, 7, 1, 4, 0, 0, DateTimeKind.Utc))
            .Take(30)
            .ToList();

        var range = service.GetRangeForCandle(
            1,
            Timeframe.M5,
            new DateTime(2026, 7, 1, 8, 5, 0, DateTimeKind.Utc),
            candles);

        Assert.False(range.IsValid);
        Assert.Contains("Not enough candles", range.InvalidReason, StringComparison.OrdinalIgnoreCase);
    }

    internal static List<Candle> BuildRangeCandles(Timeframe timeframe, DateTime rangeStartUtc)
    {
        var candles = new List<Candle>();
        var minutes = (int)timeframe;
        var expected = 240 / minutes;
        for (var i = 0; i < expected; i++)
        {
            candles.Add(CreateCandle(
                id: i + 1,
                timeframe,
                rangeStartUtc.AddMinutes(i * minutes),
                open: 100m,
                close: 100m,
                high: i == 10 ? 101m : 100.7m,
                low: i == 20 ? 99m : 99.3m));
        }

        return candles;
    }

    internal static Candle CreateCandle(
        long id,
        Timeframe timeframe,
        DateTime openTimeUtc,
        decimal open,
        decimal close,
        decimal high,
        decimal low,
        bool isClosed = true) => new()
    {
        Id = id,
        ExchangeId = 1,
        SymbolId = 1,
        Timeframe = timeframe,
        OpenTimeUtc = openTimeUtc,
        CloseTimeUtc = openTimeUtc.AddMinutes((int)timeframe),
        Open = open,
        Close = close,
        High = high,
        Low = low,
        Volume = 1000m,
        IsClosed = isClosed,
        CreatedAtUtc = DateTime.UtcNow
    };
}

public class FourHourRangeReEntryStrategyTests
{
    private readonly FourHourRangeReEntryStrategy _strategy = new();

    [Fact]
    public void Evaluate_ReturnsNoTradeBeforeRangeCloses()
    {
        var candles = FourHourRangeServiceTests.BuildRangeCandles(Timeframe.M5, RangeStartUtc).Take(40).ToList();
        var result = _strategy.Evaluate(BuildContext(candles));

        Assert.Equal(SignalType.NoTrade, result.SignalType);
        Assert.Equal("First 4H NY range has not closed yet.", result.Reason);
    }

    [Fact]
    public void Evaluate_ReturnsNoTradeWhenRangeCandlesAreMissing()
    {
        var candles = FourHourRangeServiceTests.BuildRangeCandles(Timeframe.M5, RangeStartUtc).Take(30).ToList();
        candles.Add(Candle(200, RangeEndUtc, 100m, 100.4m, 100.6m, 99.8m));

        var result = _strategy.Evaluate(BuildContext(candles));

        Assert.Equal(SignalType.NoTrade, result.SignalType);
        Assert.Equal("Not enough candles to build first 4H range.", result.Reason);
    }

    [Fact]
    public void Evaluate_IgnoresWickOnlyBreakoutAboveRange()
    {
        var candles = BaseRange();
        candles.Add(Candle(200, RangeEndUtc, 100.2m, 100.8m, 101.5m, 100.1m));

        var result = _strategy.Evaluate(BuildContext(candles));

        Assert.Equal(SignalType.NoTrade, result.SignalType);
        Assert.Equal("Wick crossed range high, but candle did not close outside.", result.Reason);
    }

    [Fact]
    public void Evaluate_IgnoresWickOnlyBreakoutBelowRange()
    {
        var candles = BaseRange();
        candles.Add(Candle(200, RangeEndUtc, 99.8m, 99.2m, 100.1m, 98.5m));

        var result = _strategy.Evaluate(BuildContext(candles));

        Assert.Equal(SignalType.NoTrade, result.SignalType);
        Assert.Equal("Wick crossed range low, but candle did not close outside.", result.Reason);
    }

    [Fact]
    public void Evaluate_WaitsForShortReentryAfterCloseAboveRange()
    {
        var candles = BaseRange();
        candles.Add(Candle(200, RangeEndUtc, 100.7m, 101.2m, 101.3m, 100.5m));

        var result = _strategy.Evaluate(BuildContext(candles));

        Assert.Equal(SignalType.NoTrade, result.SignalType);
        Assert.Equal("Breakout above range detected; waiting for close back inside.", result.Reason);
        Assert.Equal("AwaitingShortReentry", ReadRawString(result, "state"));
    }

    [Fact]
    public void Evaluate_ReturnsShortSignalOnCloseBackInside()
    {
        var candles = BaseRange();
        candles.Add(Candle(200, RangeEndUtc, 100.7m, 101.2m, 101.3m, 100.5m));
        candles.Add(Candle(201, RangeEndUtc.AddMinutes(5), 101.2m, 100.8m, 101.4m, 100.7m));

        var result = _strategy.Evaluate(BuildContext(candles));

        Assert.Equal(SignalType.Entry, result.SignalType);
        Assert.Equal(TradeDirection.Short, result.Direction);
        Assert.Equal(100.8m, result.EntryPrice);
        Assert.True(result.SuggestedStopLoss > 101.4m);
        Assert.True(result.SuggestedTakeProfit < result.EntryPrice);
        Assert.Equal("ShortSignalReady", ReadRawString(result, "state"));
        Assert.Equal("Above", ReadRawString(result, "breakoutDirection"));
        Assert.InRange(result.Strength, 0m, 100m);
        Assert.InRange(result.ConfidenceContribution, 0m, 100m);
    }

    [Fact]
    public void Evaluate_ReturnsLongSignalOnCloseBackInside()
    {
        var candles = BaseRange();
        candles.Add(Candle(200, RangeEndUtc, 99.2m, 98.8m, 99.4m, 98.6m));
        candles.Add(Candle(201, RangeEndUtc.AddMinutes(5), 98.8m, 99.2m, 99.3m, 98.5m));

        var result = _strategy.Evaluate(BuildContext(candles));

        Assert.Equal(SignalType.Entry, result.SignalType);
        Assert.Equal(TradeDirection.Long, result.Direction);
        Assert.Equal(99.2m, result.EntryPrice);
        Assert.True(result.SuggestedStopLoss < 98.5m);
        Assert.True(result.SuggestedTakeProfit > result.EntryPrice);
        Assert.Equal("LongSignalReady", ReadRawString(result, "state"));
        Assert.Equal("Below", ReadRawString(result, "breakoutDirection"));
        AssertNoOrdersOrRisk(result);
    }

    [Fact]
    public void Evaluate_UsesTwoRewardRiskTargetByDefault()
    {
        var candles = BaseRange();
        candles.Add(Candle(200, RangeEndUtc, 100.7m, 101.2m, 101.3m, 100.5m));
        candles.Add(Candle(201, RangeEndUtc.AddMinutes(5), 101.2m, 100.8m, 101.4m, 100.7m));

        var result = _strategy.Evaluate(BuildContext(candles));

        var risk = result.SuggestedStopLoss!.Value - result.EntryPrice!.Value;
        var reward = result.EntryPrice.Value - result.SuggestedTakeProfit!.Value;
        Assert.Equal(2m, decimal.Round(reward / risk, 4));
    }

    [Fact]
    public void Evaluate_RejectsExcessiveStructureStop()
    {
        var candles = BaseRange();
        candles.Add(Candle(200, RangeEndUtc, 100.7m, 101.2m, 110m, 100.5m));
        candles.Add(Candle(201, RangeEndUtc.AddMinutes(5), 101.2m, 100.8m, 109m, 100.7m));

        var result = _strategy.Evaluate(BuildContext(candles));

        Assert.Equal(SignalType.NoTrade, result.SignalType);
        Assert.Equal("Price re-entered, but stop distance is too large.", result.Reason);
    }

    [Fact]
    public void Evaluate_ResetsForNextNewYorkDay()
    {
        var candles = BaseRange();
        candles.Add(Candle(200, RangeEndUtc, 100.7m, 101.2m, 101.3m, 100.5m));
        candles.Add(Candle(201, RangeEndUtc.AddMinutes(5), 101.2m, 100.8m, 101.4m, 100.7m));
        candles.Add(Candle(300, new DateTime(2026, 7, 2, 4, 0, 0, DateTimeKind.Utc), 100m, 100m, 100.4m, 99.6m));

        var result = _strategy.Evaluate(BuildContext(candles));

        Assert.Equal(SignalType.NoTrade, result.SignalType);
        Assert.Equal("First 4H NY range has not closed yet.", result.Reason);
    }

    private static readonly DateTime RangeStartUtc = new(2026, 7, 1, 4, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime RangeEndUtc = new(2026, 7, 1, 8, 0, 0, DateTimeKind.Utc);

    private static List<Candle> BaseRange() =>
        FourHourRangeServiceTests.BuildRangeCandles(Timeframe.M5, RangeStartUtc);

    private static Candle Candle(long id, DateTime openTimeUtc, decimal open, decimal close, decimal high, decimal low) =>
        FourHourRangeServiceTests.CreateCandle(id, Timeframe.M5, openTimeUtc, open, close, high, low);

    private static StrategyContext BuildContext(IReadOnlyList<Candle> candles) => new()
    {
        SymbolId = 1,
        Symbol = "BNBUSDT",
        ExchangeId = 1,
        Timeframe = Timeframe.M5,
        HigherTimeframe = Timeframe.M15,
        MarketRegime = MarketRegime.Ranging,
        Candles = candles,
        IndicatorSnapshot = new IndicatorSnapshot
        {
            SymbolId = 1,
            Timeframe = Timeframe.M5,
            CandleId = candles[^1].Id,
            VolumeSma20 = 900m,
            CalculatedAtUtc = candles[^1].CloseTimeUtc,
            CreatedAtUtc = DateTime.UtcNow
        },
        RecentIndicatorSnapshots = [],
        StrategyParameters = new Dictionary<string, string>(),
        EvaluatedAtUtc = candles[^1].CloseTimeUtc
    };

    private static string? ReadRawString(StrategySignalResult result, string propertyName)
    {
        Assert.False(string.IsNullOrWhiteSpace(result.RawDataJson));
        using var document = JsonDocument.Parse(result.RawDataJson!);
        return document.RootElement.TryGetProperty(propertyName, out var property)
            ? property.GetString()
            : null;
    }

    private static void AssertNoOrdersOrRisk(StrategySignalResult result)
    {
        Assert.NotEqual(SignalType.Exit, result.SignalType);
        Assert.NotNull(result.RawDataJson);
    }
}
