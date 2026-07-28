using System.Text.Json;
using MomoQuant.Application.Common;
using MomoQuant.Application.Strategies.PriceStructure;
using MomoQuant.Domain.Constants;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Strategies;

namespace MomoQuant.Application.Strategies.Implementations;

public sealed class PriceStructureBreakoutRetestStrategy : StrategyBase
{
    public const string Version = PriceStructureBreakoutRetestEvaluator.StrategyVersion;
    public const string VersionV10 = PriceStructureBreakoutRetestEvaluator.StrategyVersionV10;

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

        var settings = PriceStructureBreakoutRetestEvaluator.ReadParameters(context.StrategyParameters);
        var strengthBreakdown = PriceStructureBreakoutRetestEvaluator.ComputeStrengthBreakdown(candles, candidate, settings);
        var strength = ConfidenceScoreNormalizer.Normalize(strengthBreakdown.Total);

        return Entry(
            candidate.Direction,
            strength,
            strength,
            candidate.EntryPrice,
            candidate.StopLoss,
            candidate.Target1,
            candidate.Reason,
            JsonSerializer.Serialize(new
            {
                setupFingerprint = candidate.SetupFingerprint,
                structure = candidate.Structure,
                version = Version,
                strengthBreakdown = new
                {
                    total = strengthBreakdown.Total,
                    breakoutDistance = strengthBreakdown.BreakoutDistanceScore,
                    retestQuality = strengthBreakdown.RetestQualityScore,
                    confirmationQuality = strengthBreakdown.ConfirmationQualityScore,
                    rewardRiskValidity = strengthBreakdown.RewardRiskValidityScore
                }
            }, JsonOptions));
    }

    private static string TimeframeParserToApi(Timeframe timeframe) =>
        MarketData.TimeframeParser.ToApiString(timeframe);
}
