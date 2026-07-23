using MomoQuant.Domain.Enums;

namespace MomoQuant.Application.Settings;

public class TradingSettingsDto
{
    public decimal MaxLeverage { get; init; } = 5m;
    public decimal DefaultLeverage { get; init; } = 2m;
    public decimal MaxRiskPerTradePercent { get; init; } = 2m;
    public decimal DefaultRiskPerTradePercent { get; init; } = 1m;
    public decimal MaxDailyLossPercent { get; init; } = 10m;
    public decimal MaxTotalDrawdownPercent { get; init; } = 25m;
    public int MaxOpenPositions { get; init; } = 3;
    public int MaxTradesPerDay { get; init; } = 50;
    public int MaxTradesPerSymbolPerDay { get; init; } = 12;
    public decimal MaxPositionSizeUsd { get; init; } = 5000m;
    public decimal MinRewardRiskRatio { get; init; } = 1.2m;
    public decimal DefaultRewardRiskRatio { get; init; } = 1.5m;
    public decimal MakerFeeRate { get; init; } = 0.0002m;
    public decimal TakerFeeRate { get; init; } = 0.0005m;
    public string SlippageModel { get; init; } = "FixedPercent";
    public decimal SlippagePercent { get; init; } = 0.02m;
    public int OrderExpiryCandles { get; init; } = 3;
    public bool AllowLongTrades { get; init; } = true;
    public bool AllowShortTrades { get; init; } = true;
    public bool AllowMultiplePositionsPerSymbol { get; init; }
    public bool AllowPositionScaling { get; init; }
    public bool AllowReversePosition { get; init; }
    public string DefaultBenchmarkEvaluationMode { get; init; } = nameof(BenchmarkEvaluationMode.RawStrategyResearch);
    public decimal DefaultBenchmarkInitialBalance { get; init; } = 10000m;
    public long? DefaultBenchmarkRiskProfileId { get; init; }
    public long? DefaultLivePaperRiskProfileId { get; init; }
    public decimal DefaultConfidenceThreshold { get; init; } = 50m;
    public bool ConfidenceHardGateDefault { get; init; }
    public bool UseAiScoringDefault { get; init; }
    public bool StrictAiRequiredDefault { get; init; }
    public bool EnableShadowTradeAnalysis { get; init; } = true;
    public string SameCandleExitPolicy { get; init; } = "ConservativeStopFirst";
}

public sealed class UpdateTradingSettingsRequest : TradingSettingsDto;
