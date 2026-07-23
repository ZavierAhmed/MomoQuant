using System.Text.Json;
using MomoQuant.Application.Strategies.VolatilityGatedSuperTrend;
using MomoQuant.Application.Strategies.VolatilityGatedSuperTrend.Dtos;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.MarketData;
using MomoQuant.Domain.Strategies;

namespace MomoQuant.Application.Strategies.Implementations;

public sealed class VolatilityGatedSuperTrendMomentumStrategy : StrategyBase
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IVolatilityGatedSuperTrendContextService _contextService;
    private readonly VolatilityGatedSuperTrendEvaluator _evaluator;
    private readonly IVolatilityGatedSuperTrendRetestTracker _retestTracker;
    private readonly IVolatilityGatedSuperTrendFunnelTracker? _funnelTracker;

    public VolatilityGatedSuperTrendMomentumStrategy()
        : this(null, null, null, null)
    {
    }

    public VolatilityGatedSuperTrendMomentumStrategy(
        IVolatilityGatedSuperTrendContextService? contextService = null,
        VolatilityGatedSuperTrendEvaluator? evaluator = null,
        IVolatilityGatedSuperTrendRetestTracker? retestTracker = null,
        IVolatilityGatedSuperTrendFunnelTracker? funnelTracker = null)
    {
        _contextService = contextService ?? new VolatilityGatedSuperTrendContextService();
        _evaluator = evaluator ?? new VolatilityGatedSuperTrendEvaluator();
        _retestTracker = retestTracker ?? new VolatilityGatedSuperTrendRetestTracker();
        _funnelTracker = funnelTracker;
    }

    public override StrategyCode Code => StrategyCode.VolatilityGatedSupertrendMomentum;
    public override string Name => "Volatility-Gated SuperTrend Momentum";
    public override string Description =>
        "SuperTrend continuation strategy filtered by ATR volatility regime and momentum confirmation to reduce sideways-market whipsaws.";

    public override IReadOnlyCollection<MarketRegime> SupportedRegimes { get; } =
        [MarketRegime.Trending, MarketRegime.Breakout, MarketRegime.HighVolatility];

    public override IReadOnlyCollection<Timeframe> SupportedTimeframes { get; } =
        [Timeframe.M3, Timeframe.M5, Timeframe.M15, Timeframe.M30, Timeframe.H1, Timeframe.H4];

    public void PrecomputeData(
        long symbolId,
        IReadOnlyList<Candle> executionCandles,
        IReadOnlyDictionary<string, string> rawParameters,
        long? tradingSessionId = null)
    {
        if (tradingSessionId is long sessionId)
        {
            _retestTracker.ResetRun(sessionId);
        }

        var parameters = VolatilityGatedSuperTrendParameters.From(rawParameters);
        _contextService.Precompute(symbolId, executionCandles, parameters);
        _funnelTracker?.Reset();
    }

    public override StrategySignalResult Evaluate(StrategyContext context)
    {
        if (!IsSupportedTimeframe(context.Timeframe, SupportedTimeframes))
        {
            _funnelTracker?.RecordEvaluation(new VolatilityGatedSuperTrendDiagnosticsDto
            {
                CandleTimeUtc = context.EvaluatedAtUtc,
                RejectionReason = VolatilityGatedSuperTrendRejectionCodes.TimeframeNotSupported,
                FinalDecision = "NoTrade"
            });
            return NoTrade(VolatilityGatedSuperTrendRejectionCodes.ToDisplayReason(VolatilityGatedSuperTrendRejectionCodes.TimeframeNotSupported));
        }

        var current = context.CurrentCandle;
        if (current is null)
        {
            _funnelTracker?.RecordEvaluation(new VolatilityGatedSuperTrendDiagnosticsDto
            {
                CandleTimeUtc = context.EvaluatedAtUtc,
                RejectionReason = VolatilityGatedSuperTrendRejectionCodes.MissingData,
                FinalDecision = "NoTrade"
            });
            return NoTrade(VolatilityGatedSuperTrendRejectionCodes.ToDisplayReason(VolatilityGatedSuperTrendRejectionCodes.MissingData));
        }

        var parameters = VolatilityGatedSuperTrendParameters.From(context.StrategyParameters);
        var index = context.CurrentCandleIndex ?? context.Candles.Count - 1;
        if (index < 0 || index >= context.Candles.Count)
        {
            return NoTrade(VolatilityGatedSuperTrendRejectionCodes.ToDisplayReason(VolatilityGatedSuperTrendRejectionCodes.MissingData));
        }

        var evaluation = _evaluator.Evaluate(
            context.Candles,
            index,
            parameters,
            _contextService,
            _retestTracker,
            context.SymbolId,
            context.TradingSessionId ?? 0);

        if (evaluation.Diagnostics is not null)
        {
            _funnelTracker?.RecordEvaluation(evaluation.Diagnostics);
        }

        if (!evaluation.IsEntry || evaluation.EntryPrice is null || evaluation.StopLoss is null)
        {
            var reason = evaluation.RejectionCode is not null
                ? VolatilityGatedSuperTrendRejectionCodes.ToDisplayReason(evaluation.RejectionCode)
                : "No valid entry setup met strategy conditions.";
            return evaluation.RejectionCode is not null
                ? NoTrade(evaluation.RejectionCode, reason)
                : NoTrade(reason);
        }

        var direction = evaluation.Direction == "Long" ? TradeDirection.Long : TradeDirection.Short;
        return Entry(
            direction,
            evaluation.Strength ?? parameters.MinStrength,
            evaluation.Strength ?? parameters.MinStrength,
            evaluation.EntryPrice.Value,
            evaluation.StopLoss,
            evaluation.TakeProfit,
            evaluation.Reason ?? "Volatility-gated SuperTrend momentum entry.",
            JsonSerializer.Serialize(evaluation.Diagnostics, JsonOptions));
    }
}
