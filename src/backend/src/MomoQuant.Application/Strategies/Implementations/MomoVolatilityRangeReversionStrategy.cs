using System.Text.Json;
using MomoQuant.Application.Strategies.MomoRange;
using MomoQuant.Domain.Constants;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Strategies;

namespace MomoQuant.Application.Strategies.Implementations;

public sealed class MomoVolatilityRangeReversionStrategy : StrategyBase
{
    public const string Version = MomoVolatilityRangeReversionEvaluator.StrategyVersion;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public override StrategyCode Code => StrategyCode.MomoVolatilityRangeReversion;

    public override string Name => "MOMO Volatility Range Reversion";

    public override string Description =>
        "Mean-reversion strategy that probes volatility-defined range boundaries, reclaims inside the range, and targets the midpoint.";

    public override IReadOnlyCollection<MarketRegime> SupportedRegimes { get; } =
        [MarketRegime.Ranging, MarketRegime.Reversal];

    public override IReadOnlyCollection<Timeframe> SupportedTimeframes { get; } =
        [Timeframe.M5, Timeframe.M15, Timeframe.M30, Timeframe.H1];

    public override StrategySignalResult Evaluate(StrategyContext context)
    {
        if (!IsSupportedTimeframe(context.Timeframe, SupportedTimeframes))
        {
            return NoTrade(
                MomoVolatilityRangeRejectionCodes.InsufficientData,
                "Timeframe is not supported.",
                BuildEnvelope(context, MomoVolatilityRangeRejectionCodes.InsufficientData));
        }

        if (!IsSupportedRegime(context.MarketRegime, SupportedRegimes))
        {
            return NoTrade(
                MomoVolatilityRangeRejectionCodes.TrendFilterFailed,
                $"Market regime '{context.MarketRegime}' is not supported.",
                BuildEnvelope(context, MomoVolatilityRangeRejectionCodes.TrendFilterFailed));
        }

        var candles = context.Candles;
        if (candles.Count == 0)
        {
            return NoTrade(
                MomoVolatilityRangeRejectionCodes.InsufficientData,
                "Insufficient candle data.",
                BuildEnvelope(context, MomoVolatilityRangeRejectionCodes.InsufficientData));
        }

        var seen = context.StrategyParameters.TryGetValue("__seenFingerprints", out var seenJson)
            ? JsonSerializer.Deserialize<HashSet<string>>(seenJson) ?? new HashSet<string>()
            : new HashSet<string>();

        var (candidate, reason) = MomoVolatilityRangeReversionEvaluator.EvaluateAtCurrentCandle(
            candles,
            context.StrategyParameters,
            seen,
            StrategyCodes.MomoVolatilityRangeReversion,
            context.SymbolId,
            MarketData.TimeframeParser.ToApiString(context.Timeframe));

        if (candidate is null)
        {
            return NoTrade(reason, reason, BuildEnvelope(context, reason));
        }

        return Entry(
            candidate.Direction,
            candidate.Strength,
            candidate.Strength,
            candidate.EntryPrice,
            candidate.StopLoss,
            candidate.TakeProfit,
            MomoVolatilityRangeRejectionCodes.EntryConfirmed,
            JsonSerializer.Serialize(new
            {
                setupFingerprint = candidate.SetupFingerprint,
                version = Version,
                diagnostics = JsonSerializer.Deserialize<object>(candidate.RawDataJson)
            }, JsonOptions));
    }

    private static string BuildEnvelope(StrategyContext context, string reason) =>
        JsonSerializer.Serialize(new
        {
            strategyCode = StrategyCodes.MomoVolatilityRangeReversion,
            version = Version,
            reason,
            symbolId = context.SymbolId,
            symbol = context.Symbol,
            timeframe = MarketData.TimeframeParser.ToApiString(context.Timeframe),
            marketRegime = context.MarketRegime.ToString(),
            evaluatedAtUtc = context.EvaluatedAtUtc
        }, JsonOptions);
}
