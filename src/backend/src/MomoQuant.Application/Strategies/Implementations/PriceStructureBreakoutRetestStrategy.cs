using System.Text.Json;
using MomoQuant.Application.Strategies.PriceStructure;
using MomoQuant.Domain.Constants;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Strategies;

namespace MomoQuant.Application.Strategies.Implementations;

public sealed class PriceStructureBreakoutRetestStrategy : StrategyBase
{
    public const string Version = PriceStructureBreakoutRetestEvaluator.StrategyVersion;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public override StrategyCode Code => StrategyCode.PriceStructureBreakoutRetest;
    public override string Name => "Price Structure Breakout + Retest";
    public override string Description =>
        "Detects confirmed swing structure levels, breakout closes, retests, and confirmation using OHLC candles only.";

    public override IReadOnlyCollection<MarketRegime> SupportedRegimes { get; } =
        [MarketRegime.Breakout, MarketRegime.Trending, MarketRegime.Ranging];

    public override IReadOnlyCollection<Timeframe> SupportedTimeframes { get; } =
        [Timeframe.M5, Timeframe.M15, Timeframe.M30, Timeframe.H1, Timeframe.H4];

    public override StrategySignalResult Evaluate(StrategyContext context)
    {
        if (!IsSupportedTimeframe(context.Timeframe, SupportedTimeframes))
        {
            return NoTrade(PriceStructureRejectionCodes.InsufficientData, "Timeframe is not supported.");
        }

        var candles = context.Candles;
        if (candles.Count < 10)
        {
            return NoTrade(PriceStructureRejectionCodes.InsufficientData, "Insufficient candle data.");
        }

        var seen = context.StrategyParameters.TryGetValue("__seenFingerprints", out var seenJson)
            ? JsonSerializer.Deserialize<HashSet<string>>(seenJson) ?? new HashSet<string>()
            : new HashSet<string>();

        var (candidate, reason) = PriceStructureBreakoutRetestEvaluator.EvaluateAtCurrentCandle(
            candles,
            context.StrategyParameters,
            seen,
            StrategyCodes.PriceStructureBreakoutRetest,
            context.SymbolId,
            TimeframeParserToApi(context.Timeframe));

        if (candidate is null)
        {
            return NoTrade(reason, reason);
        }

        return Entry(
            candidate.Direction,
            70m,
            70m,
            candidate.EntryPrice,
            candidate.StopLoss,
            candidate.Target1,
            candidate.Reason,
            JsonSerializer.Serialize(new
            {
                setupFingerprint = candidate.SetupFingerprint,
                structure = candidate.Structure,
                version = Version
            }, JsonOptions));
    }

    private static string TimeframeParserToApi(Timeframe timeframe) =>
        MarketData.TimeframeParser.ToApiString(timeframe);
}
