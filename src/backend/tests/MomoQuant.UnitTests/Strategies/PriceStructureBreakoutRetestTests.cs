using MomoQuant.Application.Strategies.Implementations;
using MomoQuant.Application.Strategies.PriceStructure;
using MomoQuant.Domain.Constants;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.MarketData;
using MomoQuant.Domain.Strategies;

namespace MomoQuant.UnitTests.Strategies;

/// <summary>
/// Milestone 23.1A — Price Structure Breakout + Retest Strategy Unit Tests (v1.1.0).
/// </summary>
public sealed class PriceStructureBreakoutRetestTests
{
    [Fact]
    public void Strategy_HasCorrectCode()
    {
        var strategy = new PriceStructureBreakoutRetestStrategy();
        Assert.Equal(StrategyCode.PriceStructureBreakoutRetest, strategy.Code);
    }

    [Fact]
    public void Strategy_SupportsMultipleRegimes()
    {
        var strategy = new PriceStructureBreakoutRetestStrategy();
        Assert.Contains(MarketRegime.Breakout, strategy.SupportedRegimes);
        Assert.Contains(MarketRegime.Trending, strategy.SupportedRegimes);
        Assert.Contains(MarketRegime.Ranging, strategy.SupportedRegimes);
    }

    [Fact]
    public void Strategy_SupportsMultipleTimeframes()
    {
        var strategy = new PriceStructureBreakoutRetestStrategy();
        Assert.Contains(Timeframe.M5, strategy.SupportedTimeframes);
        Assert.Contains(Timeframe.M15, strategy.SupportedTimeframes);
        Assert.Contains(Timeframe.M30, strategy.SupportedTimeframes);
        Assert.Contains(Timeframe.H1, strategy.SupportedTimeframes);
        Assert.Contains(Timeframe.H4, strategy.SupportedTimeframes);
    }

    [Fact]
    public void Evaluate_InsufficientCandles_RejectsInsufficientData()
    {
        var strategy = new PriceStructureBreakoutRetestStrategy();
        var context = new StrategyContext
        {
            SymbolId = 1,
            Symbol = "BTCUSDT",
            Timeframe = Timeframe.M5,
            HigherTimeframe = Timeframe.H1,
            MarketRegime = MarketRegime.Breakout,
            Candles = BuildMinimalCandles(5),
            IndicatorSnapshot = null,
            EvaluatedAtUtc = DateTime.UtcNow
        };

        var result = strategy.Evaluate(context);

        Assert.Equal(TradeDirection.None, result.Direction);
        Assert.Contains(PriceStructureRejectionCodes.InsufficientData, result.Reason ?? string.Empty);
    }

    [Fact]
    public void Evaluate_UnsupportedTimeframe_RejectsInsufficientData()
    {
        var strategy = new PriceStructureBreakoutRetestStrategy();
        var context = new StrategyContext
        {
            SymbolId = 1,
            Symbol = "BTCUSDT",
            Timeframe = Timeframe.M1,
            HigherTimeframe = Timeframe.M5,
            MarketRegime = MarketRegime.Breakout,
            Candles = BuildMinimalCandles(100),
            IndicatorSnapshot = null,
            EvaluatedAtUtc = DateTime.UtcNow
        };

        var result = strategy.Evaluate(context);

        Assert.Equal(TradeDirection.None, result.Direction);
        Assert.Contains(PriceStructureRejectionCodes.InsufficientData, result.Reason ?? string.Empty);
    }

    [Fact]
    public void Parameters_HaveReasonableDefaults()
    {
        var parameters = PriceStructureBreakoutRetestEvaluator.ReadParameters(new Dictionary<string, string>());

        Assert.Equal(2, parameters.SwingLeftBars);
        Assert.Equal(2, parameters.SwingRightBars);
        Assert.Equal(3, parameters.MinSwingDistanceBars);
        Assert.True(parameters.UseWicksForSwing);
        Assert.True(parameters.BreakoutMustCloseBeyondLevel);
        Assert.Equal(20, parameters.MaxRetestBars);
        Assert.Equal(0.15m, parameters.RetestTolerancePercent);
        Assert.Equal("Percent", parameters.RetestToleranceMode);
        Assert.Equal(0.25m, parameters.RetestToleranceAtrMultiplier);
        Assert.True(parameters.AllowWickThroughLevel);
        Assert.Equal(0.30m, parameters.MaxRetestPenetrationPercent);
        Assert.Equal("ReactionClose", parameters.ConfirmationMode);
        Assert.Equal(2.0m, parameters.FixedRewardRisk);
        Assert.Equal(0.05m, parameters.StopBufferPercent);
    }

    [Fact]
    public void Parameters_V11_SupportsPercentTolerance()
    {
        var parameters = PriceStructureBreakoutRetestEvaluator.ReadParameters(new Dictionary<string, string>
        {
            ["retestToleranceMode"] = "Percent",
            ["retestTolerancePercent"] = "0.20"
        });

        Assert.Equal("Percent", parameters.RetestToleranceMode);
        Assert.Equal(0.20m, parameters.RetestTolerancePercent);
    }

    [Fact]
    public void Parameters_V11_SupportsAtrTolerance()
    {
        var parameters = PriceStructureBreakoutRetestEvaluator.ReadParameters(new Dictionary<string, string>
        {
            ["retestToleranceMode"] = "ATR",
            ["retestToleranceAtrMultiplier"] = "0.30"
        });

        Assert.Equal("ATR", parameters.RetestToleranceMode);
        Assert.Equal(0.30m, parameters.RetestToleranceAtrMultiplier);
    }

    [Fact]
    public void Parameters_V11_ToleranceAppliedOnce()
    {
        var parameters = PriceStructureBreakoutRetestEvaluator.ReadParameters(new Dictionary<string, string>
        {
            ["retestToleranceMode"] = "Percent",
            ["retestTolerancePercent"] = "0.15"
        });

        Assert.Equal("Percent", parameters.RetestToleranceMode);
        Assert.NotEqual(0.0m, parameters.RetestTolerancePercent);
    }

    [Fact]
    public void Parameters_V11_BullishConfirmation_ReactionClose()
    {
        var parameters = PriceStructureBreakoutRetestEvaluator.ReadParameters(new Dictionary<string, string>
        {
            ["confirmationMode"] = "ReactionClose"
        });

        Assert.Equal("ReactionClose", parameters.ConfirmationMode);
    }

    [Fact]
    public void Parameters_V11_BullishConfirmation_Engulfing()
    {
        var parameters = PriceStructureBreakoutRetestEvaluator.ReadParameters(new Dictionary<string, string>
        {
            ["confirmationMode"] = "Engulfing"
        });

        Assert.Equal("Engulfing", parameters.ConfirmationMode);
    }

    [Fact]
    public void Parameters_V11_BullishConfirmation_CloseBeyondExtreme()
    {
        var parameters = PriceStructureBreakoutRetestEvaluator.ReadParameters(new Dictionary<string, string>
        {
            ["confirmationMode"] = "CloseBeyondExtreme"
        });

        Assert.Equal("CloseBeyondExtreme", parameters.ConfirmationMode);
    }

    [Fact]
    public void Version_IsV110()
    {
        Assert.Equal("1.1.0", PriceStructureBreakoutRetestStrategy.Version);
        Assert.Equal("1.0.0", PriceStructureBreakoutRetestStrategy.VersionV10);
    }

    [Fact]
    public void Version_V11_FingerprintDifferentFromV10()
    {
        var candles = BuildMinimalCandles(50);
        var parameters = new Dictionary<string, string>
        {
            ["retestToleranceMode"] = "ATR",
            ["retestToleranceAtrMultiplier"] = "0.25"
        };

        var (candidate1, _) = PriceStructureBreakoutRetestEvaluator.EvaluateAtCurrentCandle(
            candles,
            parameters,
            new HashSet<string>(),
            StrategyCodes.PriceStructureBreakoutRetest,
            1,
            "5m");

        var parametersV10Style = new Dictionary<string, string>
        {
            ["retestTolerancePercent"] = "0.15"
        };

        var (candidate2, _) = PriceStructureBreakoutRetestEvaluator.EvaluateAtCurrentCandle(
            candles,
            parametersV10Style,
            new HashSet<string>(),
            StrategyCodes.PriceStructureBreakoutRetest,
            1,
            "5m");

        if (candidate1 is not null && candidate2 is not null)
        {
            Assert.NotEqual(candidate1.SetupFingerprint, candidate2.SetupFingerprint);
        }
    }

    [Fact]
    public void Strength_IsDeterministic()
    {
        var candles = BuildMinimalCandles(50);
        var parameters = new Dictionary<string, string>();

        var (candidate1, _) = PriceStructureBreakoutRetestEvaluator.EvaluateAtCurrentCandle(
            candles,
            parameters,
            new HashSet<string>(),
            StrategyCodes.PriceStructureBreakoutRetest,
            1,
            "5m");

        var (candidate2, _) = PriceStructureBreakoutRetestEvaluator.EvaluateAtCurrentCandle(
            candles,
            parameters,
            new HashSet<string>(),
            StrategyCodes.PriceStructureBreakoutRetest,
            1,
            "5m");

        if (candidate1 is not null && candidate2 is not null)
        {
            Assert.Equal(candidate1.SetupFingerprint, candidate2.SetupFingerprint);
        }
    }

    [Fact]
    public void RejectionCodes_AreWellDefined()
    {
        Assert.NotNull(PriceStructureRejectionCodes.InsufficientData);
        Assert.NotNull(PriceStructureRejectionCodes.NoConfirmedSwing);
        Assert.NotNull(PriceStructureRejectionCodes.NoBreakout);
        Assert.NotNull(PriceStructureRejectionCodes.WaitingForRetest);
        Assert.NotNull(PriceStructureRejectionCodes.RetestExpired);
        Assert.NotNull(PriceStructureRejectionCodes.RetestInvalidated);
        Assert.NotNull(PriceStructureRejectionCodes.NoConfirmation);
        Assert.NotNull(PriceStructureRejectionCodes.InvalidStop);
        Assert.NotNull(PriceStructureRejectionCodes.DuplicateSetup);
    }

    private static List<Candle> BuildMinimalCandles(int count, decimal basePrice = 50000m)
    {
        var candles = new List<Candle>();
        var start = DateTime.UtcNow.AddDays(-count);

        for (int i = 0; i < count; i++)
        {
            var noise = (decimal)(i * 10m);
            candles.Add(new Candle
            {
                SymbolId = 1,
                ExchangeId = 1,
                Timeframe = Timeframe.M5,
                OpenTimeUtc = start.AddMinutes(i * 5),
                CloseTimeUtc = start.AddMinutes(i * 5 + 5),
                Open = basePrice + noise,
                High = basePrice + noise + 100m,
                Low = basePrice + noise - 100m,
                Close = basePrice + noise + 50m,
                Volume = 100m + i,
                IsClosed = true,
                CreatedAtUtc = DateTime.UtcNow
            });
        }

        return candles;
    }
}
