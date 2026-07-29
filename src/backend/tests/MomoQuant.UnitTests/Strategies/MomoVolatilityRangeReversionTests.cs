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

    [Fact]
    public void EvaluateAtCurrentCandle_ValidLong_WithCompleteDefaults()
    {
        var candles = MomoVolatilityRangeReversionFormulaTests.BuildValidLong();
        var (candidate, reason) = MomoVolatilityRangeReversionEvaluator.EvaluateAtCurrentCandle(
            candles,
            new Dictionary<string, string>(MomoVolatilityRangeReversionParameters.GetDefaultParameterContract()),
            new HashSet<string>(),
            StrategyCode.MomoVolatilityRangeReversion.ToCode(),
            1,
            "5m");

        Assert.NotNull(candidate);
        Assert.Equal(MomoVolatilityRangeRejectionCodes.EntryConfirmed, reason);
        Assert.Equal(TradeDirection.Long, candidate!.Direction);
        Assert.True(candidate.Strength >= 65m);
    }

    [Fact]
    public void EvaluateAtCurrentCandle_ValidShort_WithCompleteDefaults()
    {
        var candles = MomoVolatilityRangeReversionFormulaTests.BuildValidShort();
        var (candidate, reason) = MomoVolatilityRangeReversionEvaluator.EvaluateAtCurrentCandle(
            candles,
            new Dictionary<string, string>(MomoVolatilityRangeReversionParameters.GetDefaultParameterContract()),
            new HashSet<string>(),
            StrategyCode.MomoVolatilityRangeReversion.ToCode(),
            1,
            "5m");

        Assert.NotNull(candidate);
        Assert.Equal(MomoVolatilityRangeRejectionCodes.EntryConfirmed, reason);
        Assert.Equal(TradeDirection.Short, candidate!.Direction);
        Assert.True(candidate.Strength >= 65m);
    }

    [Fact]
    public void EvaluateAtCurrentCandle_DuplicateSetup_Rejected()
    {
        var candles = MomoVolatilityRangeReversionFormulaTests.BuildValidLong();
        var defaults = new Dictionary<string, string>(MomoVolatilityRangeReversionParameters.GetDefaultParameterContract());
        var (first, _) = MomoVolatilityRangeReversionEvaluator.EvaluateAtCurrentCandle(
            candles, defaults, new HashSet<string>(), StrategyCode.MomoVolatilityRangeReversion.ToCode(), 1, "5m");
        Assert.NotNull(first);

        var (second, reason) = MomoVolatilityRangeReversionEvaluator.EvaluateAtCurrentCandle(
            candles, defaults, new HashSet<string> { first!.SetupFingerprint }, StrategyCode.MomoVolatilityRangeReversion.ToCode(), 1, "5m");
        Assert.Null(second);
        Assert.Equal(MomoVolatilityRangeRejectionCodes.DuplicateSetup, reason);
    }

    [Fact]
    public void EvaluateAtCurrentCandle_StrictInsideRangeReclaim_LongRequiresCloseAboveRangeLow()
    {
        var candles = MomoVolatilityRangeReversionFormulaTests.BuildValidLong();
        var last = candles[^1];
        candles[^1] = new Candle
        {
            SymbolId = last.SymbolId,
            ExchangeId = last.ExchangeId,
            Timeframe = last.Timeframe,
            OpenTimeUtc = last.OpenTimeUtc,
            CloseTimeUtc = last.CloseTimeUtc,
            Open = last.Open,
            High = last.High,
            Low = last.Low,
            Close = 2849m,
            Volume = last.Volume,
            IsClosed = last.IsClosed,
            CreatedAtUtc = last.CreatedAtUtc
        };

        var (candidate, reason) = MomoVolatilityRangeReversionEvaluator.EvaluateAtCurrentCandle(
            candles,
            new Dictionary<string, string>(MomoVolatilityRangeReversionParameters.GetDefaultParameterContract()),
            new HashSet<string>(),
            StrategyCode.MomoVolatilityRangeReversion.ToCode(),
            1,
            "5m");

        Assert.Null(candidate);
        Assert.Equal(MomoVolatilityRangeRejectionCodes.CloseDidNotReclaim, reason);
    }

    [Fact]
    public void EvaluateAtCurrentCandle_LargeOutsideClose_Rejected()
    {
        var candles = MomoVolatilityRangeReversionFormulaTests.BuildValidShort();
        var last = candles[^1];
        candles[^1] = new Candle
        {
            SymbolId = last.SymbolId,
            ExchangeId = last.ExchangeId,
            Timeframe = last.Timeframe,
            OpenTimeUtc = last.OpenTimeUtc,
            CloseTimeUtc = last.CloseTimeUtc,
            Open = last.Open,
            High = Math.Max(last.High, 3150.5m),
            Low = last.Low,
            Close = 3150.5m,
            Volume = last.Volume,
            IsClosed = last.IsClosed,
            CreatedAtUtc = last.CreatedAtUtc
        };

        var (candidate, reason) = MomoVolatilityRangeReversionEvaluator.EvaluateAtCurrentCandle(
            candles,
            new Dictionary<string, string>(MomoVolatilityRangeReversionParameters.GetDefaultParameterContract()),
            new HashSet<string>(),
            StrategyCode.MomoVolatilityRangeReversion.ToCode(),
            1,
            "5m");

        Assert.Null(candidate);
        Assert.Equal(MomoVolatilityRangeRejectionCodes.CloseDidNotReclaim, reason);
    }

    [Fact]
    public void EvaluateAtCurrentCandle_TrendingRejected_ExpansionBreakout()
    {
        var candles = BuildExpansionBreakoutScenario();
        var parameters = new Dictionary<string, string>
        {
            ["minRangeWidthAtr"] = "0.01",
            ["maxRangeWidthAtr"] = "1000",
            ["minVolatilityRatio"] = "0.01",
            ["maxVolatilityRatio"] = "100",
            ["minStrength"] = "0"
        };

        var (candidate, reason) = MomoVolatilityRangeReversionEvaluator.EvaluateAtCurrentCandle(
            candles,
            parameters,
            new HashSet<string>(),
            StrategyCode.MomoVolatilityRangeReversion.ToCode(),
            1,
            "5m");

        Assert.Null(candidate);
        Assert.Equal(MomoVolatilityRangeRejectionCodes.TrendFilterFailed, reason);
    }

    [Fact]
    public void EvaluateAtCurrentCandle_RangeTooNarrow_Rejected()
    {
        var candles = MomoVolatilityRangeReversionFormulaTests.BuildValidLong();
        var parameters = new Dictionary<string, string>(MomoVolatilityRangeReversionParameters.GetDefaultParameterContract())
        {
            ["minRangeWidthAtr"] = "50",
            ["maxRangeWidthAtr"] = "100"
        };

        var (candidate, reason) = MomoVolatilityRangeReversionEvaluator.EvaluateAtCurrentCandle(
            candles,
            parameters,
            new HashSet<string>(),
            StrategyCode.MomoVolatilityRangeReversion.ToCode(),
            1,
            "5m");

        Assert.Null(candidate);
        Assert.Equal(MomoVolatilityRangeRejectionCodes.RangeTooNarrow, reason);
    }

    [Fact]
    public void EvaluateAtCurrentCandle_RangeTooWide_Rejected()
    {
        var candles = MomoVolatilityRangeReversionFormulaTests.BuildValidLong();
        var parameters = new Dictionary<string, string>(MomoVolatilityRangeReversionParameters.GetDefaultParameterContract())
        {
            ["minRangeWidthAtr"] = "0.5",
            ["maxRangeWidthAtr"] = "1.0"
        };

        var (candidate, reason) = MomoVolatilityRangeReversionEvaluator.EvaluateAtCurrentCandle(
            candles,
            parameters,
            new HashSet<string>(),
            StrategyCode.MomoVolatilityRangeReversion.ToCode(),
            1,
            "5m");

        Assert.Null(candidate);
        Assert.Equal(MomoVolatilityRangeRejectionCodes.RangeTooWide, reason);
    }

    [Fact]
    public void EvaluateAtCurrentCandle_VolatilityTooLow_Rejected()
    {
        var candles = BuildLowVolatilityRange();
        var parameters = new Dictionary<string, string>
        {
            ["minRangeWidthAtr"] = "0.01",
            ["maxRangeWidthAtr"] = "1000",
            ["minVolatilityRatio"] = "5.0",  // Force VolatilityTooLow with high min
            ["maxVolatilityRatio"] = "100",
            ["minStrength"] = "0"
        };

        var (candidate, reason) = MomoVolatilityRangeReversionEvaluator.EvaluateAtCurrentCandle(
            candles,
            parameters,
            new HashSet<string>(),
            StrategyCode.MomoVolatilityRangeReversion.ToCode(),
            1,
            "5m");

        Assert.Null(candidate);
        Assert.Equal(MomoVolatilityRangeRejectionCodes.VolatilityTooLow, reason);
    }

    [Fact]
    public void EvaluateAtCurrentCandle_VolatilityTooHigh_Rejected()
    {
        var candles = BuildHighVolatilityRange();
        var parameters = new Dictionary<string, string>
        {
            ["minRangeWidthAtr"] = "0.01",
            ["maxRangeWidthAtr"] = "1000",
            ["minVolatilityRatio"] = "0.01",
            ["maxVolatilityRatio"] = "0.5",  // Force VolatilityTooHigh with low max
            ["minStrength"] = "0"
        };

        var (candidate, reason) = MomoVolatilityRangeReversionEvaluator.EvaluateAtCurrentCandle(
            candles,
            parameters,
            new HashSet<string>(),
            StrategyCode.MomoVolatilityRangeReversion.ToCode(),
            1,
            "5m");

        Assert.Null(candidate);
        Assert.Equal(MomoVolatilityRangeRejectionCodes.VolatilityTooHigh, reason);
    }

    [Fact]
    public void EvaluateAtCurrentCandle_InvalidTargetMode_Rejected()
    {
        var candles = BuildWideRangeForInvalidMode();
        var parameters = new Dictionary<string, string>
        {
            ["targetMode"] = "InvalidMode",
            ["minRangeWidthAtr"] = "0.01",
            ["maxRangeWidthAtr"] = "1000",
            ["minVolatilityRatio"] = "0.01",
            ["maxVolatilityRatio"] = "100",
            ["maxEmaSeparationAtr"] = "100",
            ["maxSlowEmaSlopeAtr"] = "100",
            ["rsiOversold"] = "100",
            ["rsiOverbought"] = "0",
            ["minimumWickPercent"] = "0",
            ["minimumRewardRisk"] = "0.01",
            ["minStrength"] = "0"
        };

        var (candidate, reason) = MomoVolatilityRangeReversionEvaluator.EvaluateAtCurrentCandle(
            candles,
            parameters,
            new HashSet<string>(),
            StrategyCode.MomoVolatilityRangeReversion.ToCode(),
            1,
            "5m");

        Assert.Null(candidate);
        Assert.Equal(MomoVolatilityRangeRejectionCodes.InvalidParameters, reason);
    }

    [Fact]
    public void EvaluateAtCurrentCandle_StrengthBelowThreshold_Rejected()
    {
        var candles = MomoVolatilityRangeReversionFormulaTests.BuildValidLong();
        var parameters = new Dictionary<string, string>(MomoVolatilityRangeReversionParameters.GetDefaultParameterContract())
        {
            ["minStrength"] = "99.9"
        };

        var (candidate, reason) = MomoVolatilityRangeReversionEvaluator.EvaluateAtCurrentCandle(
            candles,
            parameters,
            new HashSet<string>(),
            StrategyCode.MomoVolatilityRangeReversion.ToCode(),
            1,
            "5m");

        Assert.Null(candidate);
        Assert.Equal(MomoVolatilityRangeRejectionCodes.StrengthBelowMinimum, reason);
    }

    [Fact]
    public void MinimumRequiredCandles_WithDefaults_AtLeast158()
    {
        var parameters = MomoVolatilityRangeReversionParameters.Read(new Dictionary<string, string>());
        var minCandles = MomoVolatilityRangeReversionEvaluator.MinimumRequiredCandles(parameters);

        Assert.True(minCandles >= 158, $"Expected at least 158 candles with defaults, got {minCandles}");
    }

    [Fact]
    public void Parameters_GetDefaultParameterContract_MatchesDefaults()
    {
        var contract = MomoVolatilityRangeReversionParameters.GetDefaultParameterContract();
        var parameters = MomoVolatilityRangeReversionParameters.Read(contract);

        Assert.Equal(48, parameters.RangeLookback);
        Assert.Equal(3.0m, parameters.MinRangeWidthAtr);
        Assert.Equal(12.0m, parameters.MaxRangeWidthAtr);
        Assert.Equal("RangeMidpoint", parameters.TargetMode);
        Assert.Equal(65m, parameters.MinStrength);
    }

    private static List<Candle> BuildRangeWithLowerBoundarySweep()
    {
        var candles = new List<Candle>();
        var start = DateTime.UtcNow.AddDays(-200);
        var rangeLow = 2900m;
        var rangeHigh = 3100m;
        var atr = 30m;
        var rangeMid = (rangeLow + rangeHigh) / 2m;

        // Provide 200+ candles for proper indicator calculation and range establishment
        // First 120 candles: establish stable range with consistent ATR ~30
        for (int i = 0; i < 120; i++)
        {
            var cyclePos = i % 10;
            var price = cyclePos < 5
                ? rangeLow + (cyclePos * 40m)
                : rangeHigh - ((cyclePos - 5) * 40m);

            candles.Add(new Candle
            {
                SymbolId = 1,
                ExchangeId = 1,
                Timeframe = Timeframe.M5,
                OpenTimeUtc = start.AddMinutes(i * 5),
                CloseTimeUtc = start.AddMinutes(i * 5 + 5),
                Open = price,
                High = price + atr * 0.6m,
                Low = price - atr * 0.6m,
                Close = price + atr * 0.1m,
                Volume = 50m + i,
                IsClosed = true,
                CreatedAtUtc = DateTime.UtcNow
            });
        }

        // Next 65 candles: drift down toward range low to create oversold RSI
        for (int i = 120; i < 185; i++)
        {
            var driftProgress = (i - 120) / 65m;
            var price = rangeMid - (driftProgress * (rangeMid - rangeLow - 50m));

            candles.Add(new Candle
            {
                SymbolId = 1,
                ExchangeId = 1,
                Timeframe = Timeframe.M5,
                OpenTimeUtc = start.AddMinutes(i * 5),
                CloseTimeUtc = start.AddMinutes(i * 5 + 5),
                Open = price + 5m,
                High = price + atr * 0.3m,
                Low = price - atr * 0.3m,
                Close = price - 5m,
                Volume = 50m + i,
                IsClosed = true,
                CreatedAtUtc = DateTime.UtcNow
            });
        }

        // Next 15 candles: consolidate near range low to stabilize oversold
        for (int i = 185; i < 200; i++)
        {
            candles.Add(new Candle
            {
                SymbolId = 1,
                ExchangeId = 1,
                Timeframe = Timeframe.M5,
                OpenTimeUtc = start.AddMinutes(i * 5),
                CloseTimeUtc = start.AddMinutes(i * 5 + 5),
                Open = rangeLow + 15m,
                High = rangeLow + 25m,
                Low = rangeLow + 5m,
                Close = rangeLow + 12m,
                Volume = 50m + i,
                IsClosed = true,
                CreatedAtUtc = DateTime.UtcNow
            });
        }

        // Final candle: probe below rangeLow with significant lower wick, then reclaim
        var reclaimPrice = rangeLow + 50m; // Reclaim well inside range
        var probeDepth = 40m;
        candles.Add(new Candle
        {
            SymbolId = 1,
            ExchangeId = 1,
            Timeframe = Timeframe.M5,
            OpenTimeUtc = start.AddMinutes(200 * 5),
            CloseTimeUtc = start.AddMinutes(200 * 5 + 5),
            Open = rangeLow + 10m,
            High = reclaimPrice + 10m,
            Low = rangeLow - probeDepth,
            Close = reclaimPrice,
            Volume = 200m,
            IsClosed = true,
            CreatedAtUtc = DateTime.UtcNow
        });

        return candles;
    }

    private static List<Candle> BuildRangeWithUpperBoundarySweep()
    {
        var candles = new List<Candle>();
        var start = DateTime.UtcNow.AddDays(-200);
        var rangeLow = 2900m;
        var rangeHigh = 3100m;
        var atr = 30m;
        var rangeMid = (rangeLow + rangeHigh) / 2m;

        // First 120 candles: establish stable range
        for (int i = 0; i < 120; i++)
        {
            var cyclePos = i % 10;
            var price = cyclePos < 5
                ? rangeLow + (cyclePos * 40m)
                : rangeHigh - ((cyclePos - 5) * 40m);

            candles.Add(new Candle
            {
                SymbolId = 1,
                ExchangeId = 1,
                Timeframe = Timeframe.M5,
                OpenTimeUtc = start.AddMinutes(i * 5),
                CloseTimeUtc = start.AddMinutes(i * 5 + 5),
                Open = price,
                High = price + atr * 0.6m,
                Low = price - atr * 0.6m,
                Close = price + atr * 0.1m,
                Volume = 50m + i,
                IsClosed = true,
                CreatedAtUtc = DateTime.UtcNow
            });
        }

        // Next 65 candles: drift up toward range high to create overbought RSI
        for (int i = 120; i < 185; i++)
        {
            var driftProgress = (i - 120) / 65m;
            var price = rangeMid + (driftProgress * (rangeHigh - rangeMid - 50m));

            candles.Add(new Candle
            {
                SymbolId = 1,
                ExchangeId = 1,
                Timeframe = Timeframe.M5,
                OpenTimeUtc = start.AddMinutes(i * 5),
                CloseTimeUtc = start.AddMinutes(i * 5 + 5),
                Open = price - 5m,
                High = price + atr * 0.3m,
                Low = price - atr * 0.3m,
                Close = price + 5m,
                Volume = 50m + i,
                IsClosed = true,
                CreatedAtUtc = DateTime.UtcNow
            });
        }

        // Next 15 candles: consolidate near range high to stabilize overbought
        for (int i = 185; i < 200; i++)
        {
            candles.Add(new Candle
            {
                SymbolId = 1,
                ExchangeId = 1,
                Timeframe = Timeframe.M5,
                OpenTimeUtc = start.AddMinutes(i * 5),
                CloseTimeUtc = start.AddMinutes(i * 5 + 5),
                Open = rangeHigh - 15m,
                High = rangeHigh - 5m,
                Low = rangeHigh - 25m,
                Close = rangeHigh - 12m,
                Volume = 50m + i,
                IsClosed = true,
                CreatedAtUtc = DateTime.UtcNow
            });
        }

        // Final candle: probe above rangeHigh with significant upper wick, then reclaim
        var reclaimPrice = rangeHigh - 50m; // Reclaim well inside range
        var probeHeight = 40m;
        candles.Add(new Candle
        {
            SymbolId = 1,
            ExchangeId = 1,
            Timeframe = Timeframe.M5,
            OpenTimeUtc = start.AddMinutes(200 * 5),
            CloseTimeUtc = start.AddMinutes(200 * 5 + 5),
            Open = rangeHigh - 10m,
            High = rangeHigh + probeHeight,
            Low = reclaimPrice - 10m,
            Close = reclaimPrice,
            Volume = 200m,
            IsClosed = true,
            CreatedAtUtc = DateTime.UtcNow
        });

        return candles;
    }

    private static List<Candle> BuildRangeWithSweepButNoReclaim()
    {
        var candles = BuildRangeWithLowerBoundarySweep();
        var last = candles[^1];
        candles[^1] = new Candle
        {
            SymbolId = last.SymbolId,
            ExchangeId = last.ExchangeId,
            Timeframe = last.Timeframe,
            OpenTimeUtc = last.OpenTimeUtc,
            CloseTimeUtc = last.CloseTimeUtc,
            Open = last.Open,
            High = last.High,
            Low = last.Low,
            Close = last.Low + 1m,
            Volume = last.Volume,
            IsClosed = last.IsClosed,
            CreatedAtUtc = last.CreatedAtUtc
        };
        return candles;
    }

    private static List<Candle> BuildRangeWithLargeOutsideClose()
    {
        var candles = BuildRangeWithLowerBoundarySweep();
        var last = candles[^1];
        candles[^1] = new Candle
        {
            SymbolId = last.SymbolId,
            ExchangeId = last.ExchangeId,
            Timeframe = last.Timeframe,
            OpenTimeUtc = last.OpenTimeUtc,
            CloseTimeUtc = last.CloseTimeUtc,
            Open = last.Open,
            High = last.High,
            Low = last.Low,
            Close = 2800m,
            Volume = last.Volume,
            IsClosed = last.IsClosed,
            CreatedAtUtc = last.CreatedAtUtc
        };
        return candles;
    }

    private static List<Candle> BuildExpansionBreakoutScenario()
    {
        var candles = new List<Candle>();
        var start = DateTime.UtcNow.AddDays(-200);
        var basePrice = 3000m;

        for (int i = 0; i < 110; i++)
        {
            candles.Add(new Candle
            {
                SymbolId = 1,
                ExchangeId = 1,
                Timeframe = Timeframe.M5,
                OpenTimeUtc = start.AddMinutes(i * 5),
                CloseTimeUtc = start.AddMinutes(i * 5 + 5),
                Open = basePrice,
                High = basePrice + 50m,
                Low = basePrice - 50m,
                Close = basePrice + (i % 2 == 0 ? 10m : -10m),
                Volume = 50m + i,
                IsClosed = true,
                CreatedAtUtc = DateTime.UtcNow
            });
        }

        for (int i = 110; i < 160; i++)
        {
            candles.Add(new Candle
            {
                SymbolId = 1,
                ExchangeId = 1,
                Timeframe = Timeframe.M5,
                OpenTimeUtc = start.AddMinutes(i * 5),
                CloseTimeUtc = start.AddMinutes(i * 5 + 5),
                Open = basePrice + (i - 110) * 10m,
                High = basePrice + (i - 110) * 10m + 50m,
                Low = basePrice + (i - 110) * 10m - 20m,
                Close = basePrice + (i - 110) * 10m + 30m,
                Volume = 50m + i,
                IsClosed = true,
                CreatedAtUtc = DateTime.UtcNow
            });
        }

        return candles;
    }

    private static List<Candle> BuildNarrowRange()
    {
        var candles = new List<Candle>();
        var start = DateTime.UtcNow.AddDays(-200);
        var basePrice = 3000m;

        for (int i = 0; i < 160; i++)
        {
            candles.Add(new Candle
            {
                SymbolId = 1,
                ExchangeId = 1,
                Timeframe = Timeframe.M5,
                OpenTimeUtc = start.AddMinutes(i * 5),
                CloseTimeUtc = start.AddMinutes(i * 5 + 5),
                Open = basePrice,
                High = basePrice + 2m,
                Low = basePrice - 2m,
                Close = basePrice + (i % 2 == 0 ? 1m : -1m),
                Volume = 50m + i,
                IsClosed = true,
                CreatedAtUtc = DateTime.UtcNow
            });
        }

        return candles;
    }

    private static List<Candle> BuildWideRange()
    {
        var candles = new List<Candle>();
        var start = DateTime.UtcNow.AddDays(-200);
        var basePrice = 3000m;
        var rangeLow = 2600m;  // Very wide range
        var rangeHigh = 3400m; // Width = 800
        var atr = 30m;         // Width/ATR = 800/30 = ~26.7 > maxRangeWidthAtr(12)

        for (int i = 0; i < 160; i++)
        {
            var cyclePos = i % 8;
            var price = cyclePos < 4
                ? rangeLow + (cyclePos * 200m)
                : rangeHigh - ((cyclePos - 4) * 200m);

            candles.Add(new Candle
            {
                SymbolId = 1,
                ExchangeId = 1,
                Timeframe = Timeframe.M5,
                OpenTimeUtc = start.AddMinutes(i * 5),
                CloseTimeUtc = start.AddMinutes(i * 5 + 5),
                Open = price,
                High = price + atr * 0.5m,
                Low = price - atr * 0.5m,
                Close = price + atr * 0.2m,
                Volume = 50m + i,
                IsClosed = true,
                CreatedAtUtc = DateTime.UtcNow
            });
        }

        return candles;
    }

    private static List<Candle> BuildLowVolatilityRange()
    {
        var candles = new List<Candle>();
        var start = DateTime.UtcNow.AddDays(-200);
        var basePrice = 3000m;
        var rangeLow = 2900m;
        var rangeHigh = 3100m;
        var atr = 30m;

        // Build valid range first (140 candles with ATR ~30)
        for (int i = 0; i < 140; i++)
        {
            var cyclePos = i % 8;
            var price = cyclePos < 4
                ? rangeLow + (cyclePos * 50m)
                : rangeHigh - ((cyclePos - 4) * 50m);

            candles.Add(new Candle
            {
                SymbolId = 1,
                ExchangeId = 1,
                Timeframe = Timeframe.M5,
                OpenTimeUtc = start.AddMinutes(i * 5),
                CloseTimeUtc = start.AddMinutes(i * 5 + 5),
                Open = price,
                High = price + atr * 0.5m,
                Low = price - atr * 0.5m,
                Close = price + atr * 0.2m,
                Volume = 50m + i,
                IsClosed = true,
                CreatedAtUtc = DateTime.UtcNow
            });
        }

        // Last 60 candles: very low volatility (ATR drops)
        for (int i = 140; i < 200; i++)
        {
            candles.Add(new Candle
            {
                SymbolId = 1,
                ExchangeId = 1,
                Timeframe = Timeframe.M5,
                OpenTimeUtc = start.AddMinutes(i * 5),
                CloseTimeUtc = start.AddMinutes(i * 5 + 5),
                Open = basePrice,
                High = basePrice + 2m,
                Low = basePrice - 2m,
                Close = basePrice + (i % 2 == 0 ? 1m : -1m),
                Volume = 50m + i,
                IsClosed = true,
                CreatedAtUtc = DateTime.UtcNow
            });
        }

        return candles;
    }

    private static List<Candle> BuildHighVolatilityRange()
    {
        var candles = new List<Candle>();
        var start = DateTime.UtcNow.AddDays(-200);
        var basePrice = 3000m;
        var rangeLow = 2900m;
        var rangeHigh = 3100m;
        var atr = 30m;

        // Build valid range first (140 candles with ATR ~30)
        for (int i = 0; i < 140; i++)
        {
            var cyclePos = i % 8;
            var price = cyclePos < 4
                ? rangeLow + (cyclePos * 50m)
                : rangeHigh - ((cyclePos - 4) * 50m);

            candles.Add(new Candle
            {
                SymbolId = 1,
                ExchangeId = 1,
                Timeframe = Timeframe.M5,
                OpenTimeUtc = start.AddMinutes(i * 5),
                CloseTimeUtc = start.AddMinutes(i * 5 + 5),
                Open = price,
                High = price + atr * 0.5m,
                Low = price - atr * 0.5m,
                Close = price + atr * 0.2m,
                Volume = 50m + i,
                IsClosed = true,
                CreatedAtUtc = DateTime.UtcNow
            });
        }

        // Last 60 candles: very high volatility (ATR spikes)
        for (int i = 140; i < 200; i++)
        {
            var noise = (i % 2 == 0 ? 1 : -1) * 100m;
            candles.Add(new Candle
            {
                SymbolId = 1,
                ExchangeId = 1,
                Timeframe = Timeframe.M5,
                OpenTimeUtc = start.AddMinutes(i * 5),
                CloseTimeUtc = start.AddMinutes(i * 5 + 5),
                Open = basePrice + noise,
                High = basePrice + noise + 150m,
                Low = basePrice + noise - 150m,
                Close = basePrice + noise + 30m,
                Volume = 50m + i,
                IsClosed = true,
                CreatedAtUtc = DateTime.UtcNow
            });
        }

        return candles;
    }

    private static List<Candle> BuildWideRangeWithBoundaryProbe()
    {
        var candles = new List<Candle>();
        var start = DateTime.UtcNow.AddDays(-10);
        var rangeLow = 2000m;  // VERY wide range
        var rangeHigh = 4000m; // Width = 2000
        var atr = 30m;  // Keep ATR small so width/ATR ratio is very high

        // Need at least 158 candles for default warmup
        for (int i = 0; i < 160; i++)
        {
            var cyclePos = i % 8;
            var price = cyclePos < 4
                ? rangeLow + (cyclePos * 500m)
                : rangeHigh - ((cyclePos - 4) * 500m);

            candles.Add(new Candle
            {
                SymbolId = 1,
                ExchangeId = 1,
                Timeframe = Timeframe.M5,
                OpenTimeUtc = start.AddMinutes(i * 5),
                CloseTimeUtc = start.AddMinutes(i * 5 + 5),
                Open = price,
                High = price + 15m,  // Small candle range to keep ATR low
                Low = price - 15m,
                Close = price + 5m,
                Volume = 50m + i,
                IsClosed = true,
                CreatedAtUtc = DateTime.UtcNow
            });
        }

        // Add boundary probe so we get past NoBoundaryProbe
        candles.Add(new Candle
        {
            SymbolId = 1,
            ExchangeId = 1,
            Timeframe = Timeframe.M5,
            OpenTimeUtc = start.AddMinutes(160 * 5),
            CloseTimeUtc = start.AddMinutes(160 * 5 + 5),
            Open = rangeLow + 10m,
            High = rangeLow + 60m,
            Low = rangeLow - 40m,
            Close = rangeLow + 50m,
            Volume = 200m,
            IsClosed = true,
            CreatedAtUtc = DateTime.UtcNow
        });

        return candles;
    }

    private static List<Candle> BuildWideRangeForInvalidMode()
    {
        var candles = new List<Candle>();
        var start = DateTime.UtcNow.AddDays(-10);
        var rangeLow = 2900m;
        var rangeHigh = 3100m;
        var atr = 30m;

        for (int i = 0; i < 200; i++)
        {
            var cyclePos = i % 8;
            var price = cyclePos < 4
                ? rangeLow + (cyclePos * 50m)
                : rangeHigh - ((cyclePos - 4) * 50m);

            candles.Add(new Candle
            {
                SymbolId = 1,
                ExchangeId = 1,
                Timeframe = Timeframe.M5,
                OpenTimeUtc = start.AddMinutes(i * 5),
                CloseTimeUtc = start.AddMinutes(i * 5 + 5),
                Open = price,
                High = price + atr * 0.6m,
                Low = price - atr * 0.6m,
                Close = price + atr * 0.1m,
                Volume = 50m + i,
                IsClosed = true,
                CreatedAtUtc = DateTime.UtcNow
            });
        }

        // Probe below and reclaim
        candles.Add(new Candle
        {
            SymbolId = 1,
            ExchangeId = 1,
            Timeframe = Timeframe.M5,
            OpenTimeUtc = start.AddMinutes(200 * 5),
            CloseTimeUtc = start.AddMinutes(200 * 5 + 5),
            Open = rangeLow + 10m,
            High = rangeLow + 60m,
            Low = rangeLow - 40m,
            Close = rangeLow + 50m,
            Volume = 200m,
            IsClosed = true,
            CreatedAtUtc = DateTime.UtcNow
        });

        return candles;
    }

    private static List<Candle> BuildWideRangeForStrengthTest()
    {
        var candles = new List<Candle>();
        var start = DateTime.UtcNow.AddDays(-10);
        var rangeLow = 2900m;
        var rangeHigh = 3100m;
        var atr = 30m;

        for (int i = 0; i < 200; i++)
        {
            var cyclePos = i % 8;
            var price = cyclePos < 4
                ? rangeLow + (cyclePos * 50m)
                : rangeHigh - ((cyclePos - 4) * 50m);

            candles.Add(new Candle
            {
                SymbolId = 1,
                ExchangeId = 1,
                Timeframe = Timeframe.M5,
                OpenTimeUtc = start.AddMinutes(i * 5),
                CloseTimeUtc = start.AddMinutes(i * 5 + 5),
                Open = price,
                High = price + atr * 0.6m,
                Low = price - atr * 0.6m,
                Close = price + atr * 0.1m,
                Volume = 50m + i,
                IsClosed = true,
                CreatedAtUtc = DateTime.UtcNow
            });
        }

        // Probe below and reclaim (weak setup)
        candles.Add(new Candle
        {
            SymbolId = 1,
            ExchangeId = 1,
            Timeframe = Timeframe.M5,
            OpenTimeUtc = start.AddMinutes(200 * 5),
            CloseTimeUtc = start.AddMinutes(200 * 5 + 5),
            Open = rangeLow + 10m,
            High = rangeLow + 60m,
            Low = rangeLow - 40m,
            Close = rangeLow + 50m,
            Volume = 200m,
            IsClosed = true,
            CreatedAtUtc = DateTime.UtcNow
        });

        return candles;
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
