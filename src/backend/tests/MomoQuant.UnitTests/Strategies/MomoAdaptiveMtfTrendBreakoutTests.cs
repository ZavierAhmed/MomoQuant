using MomoQuant.Application.Strategies.Implementations;
using MomoQuant.Application.Strategies.MomoAdaptive;
using MomoQuant.Domain.Constants;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.MarketData;
using MomoQuant.Domain.Strategies;

namespace MomoQuant.UnitTests.Strategies;

/// <summary>
/// Milestone 23.1A — MOMO Adaptive MTF Trend Breakout Strategy Unit Tests.
/// </summary>
public sealed class MomoAdaptiveMtfTrendBreakoutTests
{
    [Fact]
    public void Strategy_HasCorrectCode()
    {
        var strategy = new MomoAdaptiveMultiTimeframeTrendBreakoutStrategy();
        Assert.Equal(StrategyCode.MomoAdaptiveMultiTimeframeTrendBreakout, strategy.Code);
    }

    [Fact]
    public void Strategy_SupportsTrendingAndBreakoutRegimes()
    {
        var strategy = new MomoAdaptiveMultiTimeframeTrendBreakoutStrategy();
        Assert.Contains(MarketRegime.Trending, strategy.SupportedRegimes);
        Assert.Contains(MarketRegime.Breakout, strategy.SupportedRegimes);
    }

    [Fact]
    public void Strategy_SupportsMultipleTimeframes()
    {
        var strategy = new MomoAdaptiveMultiTimeframeTrendBreakoutStrategy();
        Assert.Contains(Timeframe.M5, strategy.SupportedTimeframes);
        Assert.Contains(Timeframe.M15, strategy.SupportedTimeframes);
        Assert.Contains(Timeframe.H1, strategy.SupportedTimeframes);
        Assert.Contains(Timeframe.H4, strategy.SupportedTimeframes);
    }

    [Fact]
    public void Evaluate_InsufficientCandles_RejectsMtfDataUnavailable()
    {
        var strategy = new MomoAdaptiveMultiTimeframeTrendBreakoutStrategy();
        var context = new StrategyContext
        {
            SymbolId = 1,
            Symbol = "BTCUSDT",
            Timeframe = Timeframe.M5,
            HigherTimeframe = Timeframe.H1,
            MarketRegime = MarketRegime.Trending,
            Candles = Array.Empty<Candle>(),
            HigherTimeframeCandles = Array.Empty<Candle>(),
            IndicatorSnapshot = null,
            EvaluatedAtUtc = DateTime.UtcNow
        };

        var result = strategy.Evaluate(context);

        Assert.Equal(TradeDirection.None, result.Direction);
        Assert.Contains(MomoAdaptiveMtfRejectionCodes.MtfDataUnavailable, result.Reason ?? string.Empty);
    }

    [Fact]
    public void Evaluate_UnsupportedTimeframe_RejectsMtfDataUnavailable()
    {
        var strategy = new MomoAdaptiveMultiTimeframeTrendBreakoutStrategy();
        var context = new StrategyContext
        {
            SymbolId = 1,
            Symbol = "BTCUSDT",
            Timeframe = Timeframe.M1,
            HigherTimeframe = Timeframe.M5,
            MarketRegime = MarketRegime.Trending,
            Candles = BuildMinimalCandles(250),
            HigherTimeframeCandles = BuildMinimalCandles(250),
            IndicatorSnapshot = null,
            EvaluatedAtUtc = DateTime.UtcNow
        };

        var result = strategy.Evaluate(context);

        Assert.Equal(TradeDirection.None, result.Direction);
        Assert.Contains(MomoAdaptiveMtfRejectionCodes.MtfDataUnavailable, result.Reason ?? string.Empty);
    }

    [Fact]
    public void Evaluate_NoDuplicateSetup_WhenSeenFingerprintsEmpty()
    {
        var strategy = new MomoAdaptiveMultiTimeframeTrendBreakoutStrategy();
        var candles = BuildMinimalCandles(250);

        var context = new StrategyContext
        {
            SymbolId = 1,
            Symbol = "BTCUSDT",
            Timeframe = Timeframe.M5,
            HigherTimeframe = Timeframe.H1,
            MarketRegime = MarketRegime.Trending,
            Candles = candles,
            HigherTimeframeCandles = BuildMinimalCandles(250),
            IndicatorSnapshot = null,
            StrategyParameters = new Dictionary<string, string>(),
            EvaluatedAtUtc = DateTime.UtcNow
        };

        var result = strategy.Evaluate(context);
        
        Assert.NotEqual(MomoAdaptiveMtfRejectionCodes.DuplicateSetup, result.Reason);
    }

    [Fact]
    public void HigherTimeframeMapping_M5_MapsToH1()
    {
        var htf = MomoAdaptiveMtfTrendBreakoutEvaluator.ResolveHigherTimeframe(Timeframe.M5);
        Assert.Equal(Timeframe.H1, htf);
    }

    [Fact]
    public void HigherTimeframeMapping_M15_MapsToH4()
    {
        var htf = MomoAdaptiveMtfTrendBreakoutEvaluator.ResolveHigherTimeframe(Timeframe.M15);
        Assert.Equal(Timeframe.H4, htf);
    }

    [Fact]
    public void HigherTimeframeMapping_H1_MapsToH4()
    {
        var htf = MomoAdaptiveMtfTrendBreakoutEvaluator.ResolveHigherTimeframe(Timeframe.H1);
        Assert.Equal(Timeframe.H4, htf);
    }

    [Fact]
    public void HigherTimeframeMapping_H4_MapsToD1()
    {
        var htf = MomoAdaptiveMtfTrendBreakoutEvaluator.ResolveHigherTimeframe(Timeframe.H4);
        Assert.Equal(Timeframe.D1, htf);
    }

    [Fact]
    public void Parameters_HaveReasonableDefaults()
    {
        var parameters = MomoAdaptiveMtfTrendBreakoutEvaluator.ReadParameters(new Dictionary<string, string>());

        Assert.Equal(50, parameters.HtfFastEmaPeriod);
        Assert.Equal(200, parameters.HtfSlowEmaPeriod);
        Assert.Equal(20, parameters.BreakoutLookback);
        Assert.Equal(14, parameters.FastAtrPeriod);
        Assert.Equal(2.50m, parameters.FixedRewardRisk);
        Assert.Equal(70m, parameters.MinStrength);
    }

    [Fact]
    public void NoLookahead_FutureHtfCandle_DoesNotAffectEvaluation()
    {
        var strategy = new MomoAdaptiveMultiTimeframeTrendBreakoutStrategy();
        var candles = BuildMinimalCandles(250);
        var htfCandles = BuildMinimalCandles(50);

        var context1 = new StrategyContext
        {
            SymbolId = 1,
            Symbol = "BTCUSDT",
            Timeframe = Timeframe.M5,
            HigherTimeframe = Timeframe.H1,
            MarketRegime = MarketRegime.Trending,
            Candles = candles,
            HigherTimeframeCandles = htfCandles,
            IndicatorSnapshot = null,
            EvaluatedAtUtc = DateTime.UtcNow
        };

        var result1 = strategy.Evaluate(context1);

        var futureHtfCandle = new Candle
        {
            SymbolId = 1,
            ExchangeId = 1,
            Timeframe = Timeframe.H1,
            OpenTimeUtc = DateTime.UtcNow.AddHours(1),
            CloseTimeUtc = DateTime.UtcNow.AddHours(2),
            Open = 50000m,
            High = 51000m,
            Low = 49000m,
            Close = 50500m,
            Volume = 100m,
            IsClosed = true
        };

        var htfCandlesWithFuture = htfCandles.Concat(new[] { futureHtfCandle }).ToList();

        var context2 = new StrategyContext
        {
            SymbolId = 1,
            Symbol = "BTCUSDT",
            Timeframe = Timeframe.M5,
            HigherTimeframe = Timeframe.H1,
            MarketRegime = MarketRegime.Trending,
            Candles = candles,
            HigherTimeframeCandles = htfCandlesWithFuture,
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
        Assert.NotNull(MomoAdaptiveMtfRejectionCodes.MtfDataUnavailable);
        Assert.NotNull(MomoAdaptiveMtfRejectionCodes.HtfTrendNotAligned);
        Assert.NotNull(MomoAdaptiveMtfRejectionCodes.HtfSlopeNotAligned);
        Assert.NotNull(MomoAdaptiveMtfRejectionCodes.ExecutionTrendNotAligned);
        Assert.NotNull(MomoAdaptiveMtfRejectionCodes.VolatilityTooLow);
        Assert.NotNull(MomoAdaptiveMtfRejectionCodes.VolatilityTooHigh);
        Assert.NotNull(MomoAdaptiveMtfRejectionCodes.NoBreakout);
        Assert.NotNull(MomoAdaptiveMtfRejectionCodes.BreakoutBufferNotMet);
        Assert.NotNull(MomoAdaptiveMtfRejectionCodes.MomentumNotConfirmed);
        Assert.NotNull(MomoAdaptiveMtfRejectionCodes.WaitingForRetest);
        Assert.NotNull(MomoAdaptiveMtfRejectionCodes.RetestExpired);
        Assert.NotNull(MomoAdaptiveMtfRejectionCodes.RetestInvalidated);
        Assert.NotNull(MomoAdaptiveMtfRejectionCodes.BreakoutOverextended);
        Assert.NotNull(MomoAdaptiveMtfRejectionCodes.InvalidStop);
        Assert.NotNull(MomoAdaptiveMtfRejectionCodes.DuplicateSetup);
        Assert.NotNull(MomoAdaptiveMtfRejectionCodes.EntryConfirmed);
    }

    [Fact]
    public void Version_IsCorrect()
    {
        Assert.Equal("1.0.0", MomoAdaptiveMultiTimeframeTrendBreakoutStrategy.Version);
    }

    private static List<Candle> BuildMinimalCandles(int count, decimal basePrice = 50000m)
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
                High = basePrice + noise + 50m,
                Low = basePrice + noise - 50m,
                Close = basePrice + noise + 10m,
                Volume = 100m + i,
                IsClosed = true,
                CreatedAtUtc = DateTime.UtcNow
            });
        }

        return candles;
    }
}
