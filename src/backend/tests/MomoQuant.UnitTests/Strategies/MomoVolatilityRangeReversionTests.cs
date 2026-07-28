using MomoQuant.Application.Strategies.Implementations;
using MomoQuant.Application.Strategies.MomoRange;
using MomoQuant.Domain.Constants;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.MarketData;
using MomoQuant.Domain.Strategies;

namespace MomoQuant.UnitTests.Strategies;

/// <summary>
/// Milestone 23.1A — MOMO Volatility Range Reversion Strategy Unit Tests.
/// </summary>
public sealed class MomoVolatilityRangeReversionTests
{
    [Fact]
    public void Strategy_HasCorrectCode()
    {
        var strategy = new MomoVolatilityRangeReversionStrategy();
        Assert.Equal(StrategyCode.MomoVolatilityRangeReversion, strategy.Code);
    }

    [Fact]
    public void Strategy_SupportsRangingAndReversalRegimes()
    {
        var strategy = new MomoVolatilityRangeReversionStrategy();
        Assert.Contains(MarketRegime.Ranging, strategy.SupportedRegimes);
        Assert.Contains(MarketRegime.Reversal, strategy.SupportedRegimes);
    }

    [Fact]
    public void Strategy_SupportsIntradayTimeframes()
    {
        var strategy = new MomoVolatilityRangeReversionStrategy();
        Assert.Contains(Timeframe.M5, strategy.SupportedTimeframes);
        Assert.Contains(Timeframe.M15, strategy.SupportedTimeframes);
        Assert.Contains(Timeframe.M30, strategy.SupportedTimeframes);
        Assert.Contains(Timeframe.H1, strategy.SupportedTimeframes);
    }

    [Fact]
    public void Evaluate_InsufficientCandles_RejectsInsufficientData()
    {
        var strategy = new MomoVolatilityRangeReversionStrategy();
        var context = new StrategyContext
        {
            SymbolId = 1,
            Symbol = "ETHUSDT",
            Timeframe = Timeframe.M5,
            HigherTimeframe = Timeframe.H1,
            MarketRegime = MarketRegime.Ranging,
            Candles = Array.Empty<Candle>(),
            IndicatorSnapshot = null,
            EvaluatedAtUtc = DateTime.UtcNow
        };

        var result = strategy.Evaluate(context);

        Assert.Equal(TradeDirection.None, result.Direction);
        Assert.Contains(MomoVolatilityRangeRejectionCodes.InsufficientData, result.Reason ?? string.Empty);
    }

    [Fact]
    public void Evaluate_UnsupportedTimeframe_RejectsInsufficientData()
    {
        var strategy = new MomoVolatilityRangeReversionStrategy();
        var context = new StrategyContext
        {
            SymbolId = 1,
            Symbol = "ETHUSDT",
            Timeframe = Timeframe.H4,
            HigherTimeframe = Timeframe.D1,
            MarketRegime = MarketRegime.Ranging,
            Candles = BuildMinimalCandles(200),
            IndicatorSnapshot = null,
            EvaluatedAtUtc = DateTime.UtcNow
        };

        var result = strategy.Evaluate(context);

        Assert.Equal(TradeDirection.None, result.Direction);
        Assert.Contains(MomoVolatilityRangeRejectionCodes.InsufficientData, result.Reason ?? string.Empty);
    }

    [Fact]
    public void Evaluate_UnsupportedRegime_RejectsTrendFilterFailed()
    {
        var strategy = new MomoVolatilityRangeReversionStrategy();
        var context = new StrategyContext
        {
            SymbolId = 1,
            Symbol = "ETHUSDT",
            Timeframe = Timeframe.M5,
            HigherTimeframe = Timeframe.H1,
            MarketRegime = MarketRegime.Trending,
            Candles = BuildMinimalCandles(200),
            IndicatorSnapshot = null,
            EvaluatedAtUtc = DateTime.UtcNow
        };

        var result = strategy.Evaluate(context);

        Assert.Equal(TradeDirection.None, result.Direction);
        Assert.Contains(MomoVolatilityRangeRejectionCodes.TrendFilterFailed, result.Reason ?? string.Empty);
    }

    [Fact]
    public void Evaluate_NoDuplicateSetup_WhenSeenFingerprintsEmpty()
    {
        var strategy = new MomoVolatilityRangeReversionStrategy();
        var candles = BuildMinimalCandles(200);

        var context = new StrategyContext
        {
            SymbolId = 1,
            Symbol = "ETHUSDT",
            Timeframe = Timeframe.M5,
            HigherTimeframe = Timeframe.H1,
            MarketRegime = MarketRegime.Ranging,
            Candles = candles,
            IndicatorSnapshot = null,
            StrategyParameters = new Dictionary<string, string>(),
            EvaluatedAtUtc = DateTime.UtcNow
        };

        var result = strategy.Evaluate(context);
        
        Assert.NotEqual(MomoVolatilityRangeRejectionCodes.DuplicateSetup, result.Reason);
    }

    [Fact]
    public void Parameters_HaveReasonableDefaults()
    {
        var parameters = MomoVolatilityRangeReversionParameters.Read(new Dictionary<string, string>());

        Assert.Equal(48, parameters.RangeLookback);
        Assert.Equal(3.0m, parameters.MinRangeWidthAtr);
        Assert.Equal(12.0m, parameters.MaxRangeWidthAtr);
        Assert.Equal(20, parameters.FastEmaPeriod);
        Assert.Equal(50, parameters.SlowEmaPeriod);
        Assert.Equal(14, parameters.RsiPeriod);
        Assert.Equal(35m, parameters.RsiOversold);
        Assert.Equal(65m, parameters.RsiOverbought);
        Assert.Equal(1.25m, parameters.MinimumRewardRisk);
        Assert.Equal("RangeMidpoint", parameters.TargetMode);
        Assert.Equal(65m, parameters.MinStrength);
    }

    [Fact]
    public void NoLookahead_FutureCandle_DoesNotAffectEvaluation()
    {
        var strategy = new MomoVolatilityRangeReversionStrategy();
        var candles = BuildMinimalCandles(200);

        var context1 = new StrategyContext
        {
            SymbolId = 1,
            Symbol = "ETHUSDT",
            Timeframe = Timeframe.M5,
            HigherTimeframe = Timeframe.H1,
            MarketRegime = MarketRegime.Ranging,
            Candles = candles,
            IndicatorSnapshot = null,
            EvaluatedAtUtc = DateTime.UtcNow
        };

        var result1 = strategy.Evaluate(context1);

        var futureCandle = new Candle
        {
            SymbolId = 1,
            ExchangeId = 1,
            Timeframe = Timeframe.M5,
            OpenTimeUtc = DateTime.UtcNow.AddMinutes(5),
            CloseTimeUtc = DateTime.UtcNow.AddMinutes(10),
            Open = 3000m,
            High = 3050m,
            Low = 2950m,
            Close = 3025m,
            Volume = 100m,
            IsClosed = true
        };

        var candlesWithFuture = candles.Concat(new[] { futureCandle }).ToList();

        var context2 = new StrategyContext
        {
            SymbolId = 1,
            Symbol = "ETHUSDT",
            Timeframe = Timeframe.M5,
            HigherTimeframe = Timeframe.H1,
            MarketRegime = MarketRegime.Ranging,
            Candles = candlesWithFuture,
            IndicatorSnapshot = null,
            EvaluatedAtUtc = DateTime.UtcNow
        };

        var result2 = strategy.Evaluate(context2);

        Assert.Equal(result1.Direction, result2.Direction);
        Assert.Equal(result1.Reason, result2.Reason);
    }

    [Fact]
    public void RejectionCodes_AreWellDefined()
    {
        Assert.NotNull(MomoVolatilityRangeRejectionCodes.InsufficientData);
        Assert.NotNull(MomoVolatilityRangeRejectionCodes.RangeTooNarrow);
        Assert.NotNull(MomoVolatilityRangeRejectionCodes.RangeTooWide);
        Assert.NotNull(MomoVolatilityRangeRejectionCodes.TrendFilterFailed);
        Assert.NotNull(MomoVolatilityRangeRejectionCodes.VolatilityTooLow);
        Assert.NotNull(MomoVolatilityRangeRejectionCodes.VolatilityTooHigh);
        Assert.NotNull(MomoVolatilityRangeRejectionCodes.NoBoundaryProbe);
        Assert.NotNull(MomoVolatilityRangeRejectionCodes.CloseDidNotReclaim);
        Assert.NotNull(MomoVolatilityRangeRejectionCodes.RsiNotExtreme);
        Assert.NotNull(MomoVolatilityRangeRejectionCodes.WickConfirmationMissing);
        Assert.NotNull(MomoVolatilityRangeRejectionCodes.RewardRiskInsufficient);
        Assert.NotNull(MomoVolatilityRangeRejectionCodes.InvalidStop);
        Assert.NotNull(MomoVolatilityRangeRejectionCodes.DuplicateSetup);
        Assert.NotNull(MomoVolatilityRangeRejectionCodes.EntryConfirmed);
    }

    [Fact]
    public void Version_IsCorrect()
    {
        Assert.Equal("1.0.0", MomoVolatilityRangeReversionStrategy.Version);
    }

    [Fact]
    public void Strategy_OutputsCorrectEnvelope()
    {
        var strategy = new MomoVolatilityRangeReversionStrategy();
        var context = new StrategyContext
        {
            SymbolId = 1,
            Symbol = "ETHUSDT",
            Timeframe = Timeframe.M5,
            HigherTimeframe = Timeframe.H1,
            MarketRegime = MarketRegime.Trending,
            Candles = BuildMinimalCandles(200),
            IndicatorSnapshot = null,
            EvaluatedAtUtc = DateTime.UtcNow
        };

        var result = strategy.Evaluate(context);

        Assert.NotNull(result.RawDataJson);
        Assert.Contains("MOMO_VOLATILITY_RANGE_REVERSION", result.RawDataJson ?? string.Empty);
    }

    private static List<Candle> BuildMinimalCandles(int count, decimal basePrice = 3000m)
    {
        var candles = new List<Candle>();
        var start = DateTime.UtcNow.AddDays(-count);

        for (int i = 0; i < count; i++)
        {
            var noise = (decimal)(i * 0.01m);
            candles.Add(new Candle
            {
                SymbolId = 1,
                ExchangeId = 1,
                Timeframe = Timeframe.M5,
                OpenTimeUtc = start.AddMinutes(i * 5),
                CloseTimeUtc = start.AddMinutes(i * 5 + 5),
                Open = basePrice + noise,
                High = basePrice + noise + 30m,
                Low = basePrice + noise - 30m,
                Close = basePrice + noise + 5m,
                Volume = 50m + i,
                IsClosed = true,
                CreatedAtUtc = DateTime.UtcNow
            });
        }

        return candles;
    }
}
