using System.Text.Json;
using MomoQuant.Application.Strategies.PriceStructure;
using MomoQuant.Domain.Constants;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Strategies;

namespace MomoQuant.Application.Strategies.Implementations;

public sealed class PriceStructureLiquiditySweepReclaimStrategy : StrategyBase
{
    public const string Version = PriceStructureLiquiditySweepEvaluator.StrategyVersion;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public override StrategyCode Code => StrategyCode.PriceStructureLiquiditySweepReclaim;
    public override string Name => "Price Structure Liquidity Sweep + Reclaim";
    public override string Description =>
        "Detects swing liquidity levels, sweeps through them, and reclaims the level using OHLC candles only.";

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

        var (candidate, reason) = PriceStructureLiquiditySweepEvaluator.EvaluateAtCurrentCandle(
            candles,
            context.StrategyParameters,
            seen,
            StrategyCodes.PriceStructureLiquiditySweepReclaim,
            context.SymbolId,
            MarketData.TimeframeParser.ToApiString(context.Timeframe));

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
}
