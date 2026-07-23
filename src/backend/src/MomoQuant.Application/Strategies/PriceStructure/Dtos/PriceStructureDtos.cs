using System.Text.Json.Serialization;
using MomoQuant.Domain.Enums;

namespace MomoQuant.Application.Strategies.PriceStructure.Dtos;

public sealed class PriceStructureCandidateDto
{
    public required TradeDirection Direction { get; init; }
    public required decimal EntryPrice { get; init; }
    public required decimal StopLoss { get; init; }
    public required decimal Target1 { get; init; }
    public decimal? Target2 { get; init; }
    public required decimal RewardRisk { get; init; }
    public required string Reason { get; init; }
    public required string SetupFingerprint { get; init; }
    public required PriceStructureSetupDto Structure { get; init; }
}

public sealed class PriceStructureSetupDto
{
    public string SetupType { get; init; } = string.Empty;
    public string Direction { get; init; } = string.Empty;
    public decimal BrokenOrSweptLevel { get; init; }
    public DateTime? SwingTimeUtc { get; init; }
    public DateTime? BreakoutOrSweepTimeUtc { get; init; }
    public DateTime? RetestOrReclaimTimeUtc { get; init; }
    public DateTime? ConfirmationTimeUtc { get; init; }
    public int? SwingIndex { get; init; }
    public int? BreakoutIndex { get; init; }
    public int? RetestIndex { get; init; }
    public int? ConfirmationIndex { get; init; }
}

public sealed class StrategyFeatureDefinitionDto
{
    public required string Key { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public bool DefaultEnabled { get; init; }
    public bool Experimental { get; init; }
    public string VersionIntroduced { get; init; } = "1.0.0";
}

public sealed class BreakoutRetestParameters
{
    public int SwingLeftBars { get; init; } = 2;
    public int SwingRightBars { get; init; } = 2;
    public int MinSwingDistanceBars { get; init; } = 3;
    public bool UseWicksForSwing { get; init; } = true;
    public decimal MinBreakoutClosePercent { get; init; }
    public bool BreakoutMustCloseBeyondLevel { get; init; } = true;
    public int MaxRetestBars { get; init; } = 20;
    public decimal RetestTolerancePercent { get; init; } = 0.15m;
    public string RetestToleranceMode { get; init; } = "Percent";
    public bool AllowWickThroughLevel { get; init; } = true;
    public decimal MaxRetestPenetrationPercent { get; init; } = 0.30m;
    public string ConfirmationMode { get; init; } = "BullishReactionClose";
    public decimal FixedRewardRisk { get; init; } = 2.0m;
    public decimal StopBufferPercent { get; init; } = 0.05m;
}

public sealed class LiquiditySweepParameters
{
    public int SwingLeftBars { get; init; } = 2;
    public int SwingRightBars { get; init; } = 2;
    public int MaxLiquidityLevelAgeBars { get; init; } = 200;
    public bool IncludeSingleSwingLevels { get; init; } = true;
    public bool IncludeEqualHighLowLevels { get; init; } = true;
    public decimal EqualLevelTolerancePercent { get; init; } = 0.10m;
    public int MaxReclaimBars { get; init; } = 1;
    public bool RequireSameCandleReclaim { get; init; } = true;
    public decimal MinimumSweepDistancePercent { get; init; }
    public decimal? MaximumSweepDistancePercent { get; init; }
    public string ConfirmationMode { get; init; } = "ReclaimCloseOnly";
    public decimal FixedRewardRisk { get; init; } = 2.0m;
    public decimal StopBufferPercent { get; init; } = 0.05m;
}
