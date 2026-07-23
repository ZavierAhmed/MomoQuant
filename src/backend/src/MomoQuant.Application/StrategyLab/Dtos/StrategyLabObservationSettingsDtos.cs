using MomoQuant.Domain.Enums;

namespace MomoQuant.Application.StrategyLab.Dtos;

public sealed class StrategyLabObservationSettingsDto
{
    public string ConfidenceModel { get; set; } = "StrategySetupQuality/v1";
    public bool UseSystemDefaultConfidenceThreshold { get; set; } = true;
    public decimal? CustomConfidenceThreshold { get; set; }
    public decimal EffectiveConfidenceThreshold { get; set; } = 80m;

    public bool UseSystemDefaultRiskSettings { get; set; } = true;
    public long? RiskProfileId { get; set; }
    public decimal? RiskApprovalThreshold { get; set; }
    public decimal? RiskPerTradePercent { get; set; }
    public decimal? PreferredLeverage { get; set; }
    public decimal? MaximumLeverage { get; set; }

    /// <summary>Legacy ambiguous fields — preserved for older clients; not used as futures limits.</summary>
    public decimal? MaximumPositionExposurePercent { get; set; }
    public decimal? MaximumConcurrentExposurePercent { get; set; }

    public decimal? MaxNotionalExposurePerSymbolPercent { get; set; }
    public decimal? MaxTotalNotionalExposurePercent { get; set; }
    public decimal? MaxMarginUsagePerSymbolPercent { get; set; }
    public decimal? MaxTotalMarginUsagePercent { get; set; }
    public decimal? MaxConcurrentRiskPercent { get; set; }
    public int? MaxOpenPositions { get; set; }
    public decimal? MaxDailyLossPercent { get; set; }
    public decimal? MaxDrawdownPercent { get; set; }
    public decimal? MinimumRewardRisk { get; set; }
    public decimal? FeeEfficiencyHardLimitPercent { get; set; }

    public ExposureSemanticsVersion ExposureSemanticsVersion { get; set; } = ExposureSemanticsVersion.ExplicitFuturesExposureV2;
    public string? LegacyExposureResolution { get; set; }

    /// <summary>Slippage in basis points for StrategyLabCostModel/v1. Default 0.</summary>
    public decimal? SlippageBasisPoints { get; set; }
    public string? EntryOrderType { get; set; } = "Taker";
    public string? ExitOrderType { get; set; } = "Taker";
}

public sealed class ScoreDistributionDiagnosticsDto
{
    public int UniqueScoreCount { get; init; }
    public decimal? MinScore { get; init; }
    public decimal? MaxScore { get; init; }
    public decimal? AverageScore { get; init; }
    public decimal? StandardDeviation { get; init; }
    public decimal? MostCommonScore { get; init; }
    public decimal MostCommonScorePercent { get; init; }
    public string? DegenerateWarningCode { get; init; }
    public string? DegenerateWarningMessage { get; init; }
}
