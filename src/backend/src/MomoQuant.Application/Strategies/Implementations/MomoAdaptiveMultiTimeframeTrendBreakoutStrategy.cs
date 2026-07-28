using System.Text.Json;
using MomoQuant.Application.Strategies.MomoAdaptive;
using MomoQuant.Domain.Constants;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Strategies;

namespace MomoQuant.Application.Strategies.Implementations;

public sealed class MomoAdaptiveMultiTimeframeTrendBreakoutStrategy : StrategyBase
{
    public const string Version = MomoAdaptiveMtfTrendBreakoutEvaluator.StrategyVersion;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public override StrategyCode Code => StrategyCode.MomoAdaptiveMultiTimeframeTrendBreakout;
    public override string Name => "MOMO Adaptive Multi-Timeframe Trend Breakout";
    public override string Description =>
        "Multi-timeframe trend-aligned breakout with adaptive ATR buffer, retest confirmation, and MACD momentum filtering.";

    public override IReadOnlyCollection<MarketRegime> SupportedRegimes { get; } =
        [MarketRegime.Trending, MarketRegime.Breakout];

    public override IReadOnlyCollection<Timeframe> SupportedTimeframes { get; } =
        [Timeframe.M5, Timeframe.M15, Timeframe.H1, Timeframe.H4];

    public override StrategySignalResult Evaluate(StrategyContext context)
    {
        if (!IsSupportedTimeframe(context.Timeframe, SupportedTimeframes))
        {
            return NoTrade(MomoAdaptiveMtfRejectionCodes.MtfDataUnavailable, "Timeframe is not supported.");
        }

        var candles = context.Candles;
        if (candles.Count == 0)
        {
            return NoTrade(MomoAdaptiveMtfRejectionCodes.MtfDataUnavailable, "Insufficient candle data.");
        }

        var seen = context.StrategyParameters.TryGetValue("__seenFingerprints", out var seenJson)
            ? JsonSerializer.Deserialize<HashSet<string>>(seenJson) ?? new HashSet<string>()
            : new HashSet<string>();

        var (candidate, reason) = MomoAdaptiveMtfTrendBreakoutEvaluator.EvaluateAtCurrentCandle(
            candles,
            context.HigherTimeframeCandles,
            context.StrategyParameters,
            context.MarketRegime,
            seen,
            StrategyCodes.MomoAdaptiveMultiTimeframeTrendBreakout,
            context.SymbolId,
            TimeframeParserToApi(context.Timeframe));

        if (candidate is null)
        {
            return NoTrade(reason, reason);
        }

        return Entry(
            candidate.Direction,
            candidate.Strength,
            candidate.Strength,
            candidate.EntryPrice,
            candidate.StopLoss,
            candidate.TakeProfit,
            candidate.Reason,
            JsonSerializer.Serialize(new
            {
                setupFingerprint = candidate.SetupFingerprint,
                strengthBreakdown = candidate.StrengthBreakdown,
                setup = candidate.Setup,
                version = Version,
                reasonCode = MomoAdaptiveMtfRejectionCodes.EntryConfirmed
            }, JsonOptions));
    }

    private static string TimeframeParserToApi(Timeframe timeframe) =>
        MarketData.TimeframeParser.ToApiString(timeframe);
}
