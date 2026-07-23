using System.Text.Json;
using MomoQuant.Application.Strategies.BbLiquiditySweep;
using MomoQuant.Application.Strategies.BbLiquiditySweep.Dtos;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Strategies;

namespace MomoQuant.Application.Strategies.Implementations;

public abstract class BbLiquiditySweepCisdStrategyBase : StrategyBase
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    protected readonly IBbLiquiditySweepContextService ContextService;
    protected readonly BbLiquiditySweepEvaluator Evaluator;
    protected readonly IBbLiquiditySweepSessionTracker SessionTracker;
    protected readonly IBbLiquiditySweepFunnelTracker? FunnelTracker;

    protected BbLiquiditySweepCisdStrategyBase(
        bool useRsiPrimedFilter,
        IBbLiquiditySweepContextService? contextService = null,
        BbLiquiditySweepEvaluator? evaluator = null,
        IBbLiquiditySweepSessionTracker? sessionTracker = null,
        IBbLiquiditySweepFunnelTracker? funnelTracker = null)
    {
        UseRsiPrimedFilter = useRsiPrimedFilter;
        ContextService = contextService ?? new BbLiquiditySweepContextService();
        Evaluator = evaluator ?? new BbLiquiditySweepEvaluator();
        SessionTracker = sessionTracker ?? new BbLiquiditySweepSessionTracker();
        FunnelTracker = funnelTracker;
    }

    protected bool UseRsiPrimedFilter { get; }

    public override IReadOnlyCollection<MarketRegime> SupportedRegimes { get; } =
        [MarketRegime.Reversal, MarketRegime.Ranging, MarketRegime.HighVolatility, MarketRegime.Breakout, MarketRegime.Unknown];

    public override IReadOnlyCollection<Timeframe> SupportedTimeframes { get; } =
        [Timeframe.M3];

    public void PrecomputeData(
        long symbolId,
        IReadOnlyList<Domain.MarketData.Candle> executionCandles,
        IReadOnlyList<Domain.MarketData.Candle>? oneMinuteCandles,
        IReadOnlyList<Domain.MarketData.Candle>? fiveMinuteCandles,
        IReadOnlyDictionary<string, string> rawParameters,
        long? tradingSessionId = null)
    {
        if (tradingSessionId is long sessionId)
        {
            SessionTracker.ResetRun(sessionId);
        }

        var parameters = BbLiquiditySweepParameters.From(rawParameters, UseRsiPrimedFilter);
        ContextService.Precompute(symbolId, executionCandles, oneMinuteCandles, fiveMinuteCandles, parameters);
        if (UseRsiPrimedFilter)
        {
            ContextService.BuildRsiSeries(executionCandles, parameters);
        }

        if (FunnelTracker is not null)
        {
            var engineInfo = Evaluator.GetImplementationInfo();
            FunnelTracker.Reset(parameters.StrictnessProfile, engineInfo.ImplementationMode, engineInfo.SourceCodeAvailable);
            if (FunnelTracker is BbLiquiditySweepFunnelTracker concreteTracker)
            {
                concreteTracker.SetMaxSamples(parameters.MaxSampleEvaluations);
            }
        }
    }

    public override StrategySignalResult Evaluate(StrategyContext context)
    {
        if (!IsSupportedTimeframe(context.Timeframe, SupportedTimeframes))
        {
            return NoTrade(BbLiquiditySweepRejectionCodes.TimeframeNotSupported, BbLiquiditySweepRejectionCodes.ToDisplayReason(BbLiquiditySweepRejectionCodes.TimeframeNotSupported));
        }

        var parameters = BbLiquiditySweepParameters.From(context.StrategyParameters, UseRsiPrimedFilter);
        var current = context.CurrentCandle;
        if (current is null)
        {
            return NoTrade(BbLiquiditySweepRejectionCodes.NoCurrentCandle, BbLiquiditySweepRejectionCodes.ToDisplayReason(BbLiquiditySweepRejectionCodes.NoCurrentCandle));
        }

        var index = context.Candles.Count - 1;
        var evaluation = Evaluator.Evaluate(
            context.Candles,
            index,
            parameters,
            ContextService,
            SessionTracker,
            context.TradingSessionId);

        if (evaluation.CandleMetrics is not null)
        {
            FunnelTracker?.RecordCandleMetrics(evaluation.CandleMetrics);
        }

        if (evaluation.Direction is null || evaluation.EntryPrice is null || evaluation.StopLoss is null || evaluation.TakeProfit is null)
        {
            var rejectionCode = evaluation.StagedRejectionCode
                ?? evaluation.Diagnostics.RejectionReason
                ?? BbLiquiditySweepRejectionCodes.NoLiquiditySweep;
            var displayReason = BbLiquiditySweepRejectionCodes.ToDisplayReason(rejectionCode);
            RecordSampleRejection(evaluation, rejectionCode, displayReason);
            return NoTrade(rejectionCode, displayReason, BuildRawData(evaluation, parameters));
        }

        var strength = StrategyStrengthHelper.ResolveStrength(parameters.MinStrength + 10m, parameters.MinStrength);
        return Entry(
            evaluation.Direction.Value,
            strength,
            strength,
            evaluation.EntryPrice.Value,
            evaluation.StopLoss,
            evaluation.TakeProfit,
            evaluation.Reason ?? "BB liquidity sweep with CISD confirmation.",
            BuildRawData(evaluation, parameters));
    }

    private void RecordSampleRejection(BbLiquiditySweepEvaluationResult evaluation, string rejectionCode, string displayReason)
    {
        if (FunnelTracker is null)
        {
            return;
        }

        FunnelTracker.RecordSampleRejection(new BbLiquiditySweepSampleEvaluation
        {
            CandleTimeUtc = evaluation.Diagnostics.CandleTimeUtc,
            StagedRejectionCode = rejectionCode,
            DisplayReason = displayReason,
            Diagnostics = evaluation.Diagnostics
        });
    }

    private string BuildRawData(BbLiquiditySweepEvaluationResult evaluation, BbLiquiditySweepParameters parameters) =>
        JsonSerializer.Serialize(new
        {
            diagnostics = evaluation.Diagnostics,
            candleMetrics = evaluation.CandleMetrics,
            stagedRejectionCode = evaluation.StagedRejectionCode,
            strictnessProfile = parameters.StrictnessProfile.ToString(),
            strategySettings = parameters,
            targetSource = evaluation.TargetSource?.ToString(),
            breakevenTriggerPrice = evaluation.BreakevenTriggerPrice,
            indicatorImplementation = BuildIndicatorImplementationStatus()
        }, JsonOptions);

    protected abstract object BuildIndicatorImplementationStatus();
}

public sealed class BbLiquiditySweepCisdStrategy : BbLiquiditySweepCisdStrategyBase
{
    public BbLiquiditySweepCisdStrategy()
        : base(useRsiPrimedFilter: false)
    {
    }

    public BbLiquiditySweepCisdStrategy(
        IBbLiquiditySweepContextService contextService,
        BbLiquiditySweepEvaluator evaluator,
        IBbLiquiditySweepSessionTracker sessionTracker,
        IBbLiquiditySweepFunnelTracker? funnelTracker = null)
        : base(false, contextService, evaluator, sessionTracker, funnelTracker)
    {
    }

    public override StrategyCode Code => StrategyCode.BbLiquiditySweepCisd;

    public override string Name => "BB Liquidity Sweep CISD";

    public override string Description =>
        "3-minute Bollinger Band liquidity sweep with CISD confirmation. Uses MOMO-native liquidity-line approximation inspired by #itsimpossible.";

    protected override object BuildIndicatorImplementationStatus() => new
    {
        bb = new { mode = "Native MOMO Bollinger Bands" },
        itsImpossible = new
        {
            mode = "MOMO liquidity-line approximation",
            sourceProvided = false,
            sourceName = "#itsimpossible",
            reason = "Exact Pine source was not provided."
        },
        cisd = new { mode = "MOMO CISD approximation" }
    };
}

public sealed class BbLiquiditySweepCisdRsiPrimedStrategy : BbLiquiditySweepCisdStrategyBase
{
    public BbLiquiditySweepCisdRsiPrimedStrategy()
        : base(useRsiPrimedFilter: true)
    {
    }

    public BbLiquiditySweepCisdRsiPrimedStrategy(
        IBbLiquiditySweepContextService contextService,
        BbLiquiditySweepEvaluator evaluator,
        IBbLiquiditySweepSessionTracker sessionTracker,
        IBbLiquiditySweepFunnelTracker? funnelTracker = null)
        : base(true, contextService, evaluator, sessionTracker, funnelTracker)
    {
    }

    public override StrategyCode Code => StrategyCode.BbLiquiditySweepCisdRsiPrimed;

    public override string Name => "BB Liquidity Sweep CISD + RSI Primed";

    public override string Description =>
        "Adds MOMO port of RSI Primed [ChartPrime] filter: longs below 30, shorts above 70.";

    protected override object BuildIndicatorImplementationStatus() => new
    {
        bb = new { mode = "Native MOMO Bollinger Bands" },
        rsiPrimed = new
        {
            mode = "ChartPrime Pine Port",
            sourceProvided = true,
            sourceName = "RSI Primed [ChartPrime]",
            license = "MPL 2.0",
            dominantCycleMode = "DominantCycleFallback"
        },
        itsImpossible = new
        {
            mode = "MOMO liquidity-line approximation",
            sourceProvided = false,
            sourceName = "#itsimpossible",
            reason = "Exact Pine source was not provided."
        },
        cisd = new { mode = "MOMO CISD approximation" }
    };
}
