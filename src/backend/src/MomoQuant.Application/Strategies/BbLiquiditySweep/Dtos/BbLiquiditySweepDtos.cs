using MomoQuant.Domain.Enums;

namespace MomoQuant.Application.Strategies.BbLiquiditySweep.Dtos;

public enum LiquidityDirection
{
    BuySideLiquidity,
    SellSideLiquidity
}

public enum TargetSource
{
    LtfLiquidity,
    FiveMinuteLiquidity,
    Fixed3R
}

public enum RsiPrimedImplementationMode
{
    FullPort,
    DominantCycleFallback
}

public enum RsiPrimedSignalValueMode
{
    HaClose,
    HaLowHigh,
    Ohlc4
}

public sealed class BollingerBandsValueDto
{
    public required DateTime TimeUtc { get; init; }
    public decimal Middle { get; init; }
    public decimal Upper { get; init; }
    public decimal Lower { get; init; }
    public decimal Bandwidth { get; init; }
    public decimal PercentB { get; init; }
}

public sealed class LiquidityLevelDto
{
    public required string Id { get; init; }
    public required string Timeframe { get; init; }
    public required LiquidityDirection Direction { get; init; }
    public required decimal Price { get; init; }
    public required DateTime CreatedAtUtc { get; init; }
    public DateTime? LastTouchedAtUtc { get; init; }
    public DateTime? SweptAtUtc { get; init; }
    public bool IsSwept { get; init; }
    public DateTime? SourceSwingCandleTimeUtc { get; init; }
    public decimal StrengthScore { get; init; }
    public int TouchCount { get; init; }
    public required string ImplementationMode { get; init; }
    public required string SourceIndicatorName { get; init; }
}

public sealed class LiquiditySweepSignalDto
{
    public required TradeDirection Direction { get; init; }
    public required string SweptLiquidityLevelId { get; init; }
    public required decimal SweptLiquidityPrice { get; init; }
    public required DateTime CandleTimeUtc { get; init; }
    public decimal CandleHigh { get; init; }
    public decimal CandleLow { get; init; }
    public decimal CandleClose { get; init; }
    public decimal BbUpper { get; init; }
    public decimal BbLower { get; init; }
    public bool SweptOutsideBb { get; init; }
    public bool ClosedBackInsideBb { get; init; }
    public bool ClosedBackAcrossLiquidityLine { get; init; }
    public bool IsValidSweep { get; init; }
    public string? RejectionReason { get; init; }
}

public enum CisdConfirmationType
{
    StructureBreak,
    BearishCandleHighBreak,
    BullishCandleLowBreak,
    DisplacementClose
}

public sealed class CisdSignalDto
{
    public required TradeDirection Direction { get; init; }
    public decimal CisdLevel { get; init; }
    public DateTime? ConfirmedAtUtc { get; init; }
    public long? ConfirmedCandleId { get; init; }
    public CisdConfirmationType? ConfirmationType { get; init; }
    public decimal ConfidenceScore { get; init; }
    public bool IsConfirmed { get; init; }
    public string? RejectionReason { get; init; }
}

public sealed class RsiPrimedResultDto
{
    public required DateTime TimeUtc { get; init; }
    public decimal? RsiOpen { get; init; }
    public decimal? RsiHigh { get; init; }
    public decimal? RsiLow { get; init; }
    public decimal? RsiClose { get; init; }
    public decimal? HaOpen { get; init; }
    public decimal? HaHigh { get; init; }
    public decimal? HaLow { get; init; }
    public decimal? HaClose { get; init; }
    public decimal? Ohlc4 { get; init; }
    public decimal? AdaptiveMa { get; init; }
    public int DominantCycleLength { get; init; }
    public bool IsOversold { get; init; }
    public bool IsOverbought { get; init; }
    public string? BullishPattern { get; init; }
    public string? BearishPattern { get; init; }
    public decimal? SignalValue { get; init; }
    public RsiPrimedImplementationMode ImplementationMode { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public sealed class BbLiquiditySweepDiagnosticsDto
{
    public required DateTime CandleTimeUtc { get; init; }
    public bool InAllowedSession { get; init; }
    public string? SessionName { get; init; }
    public decimal? UpperBb { get; init; }
    public decimal? MiddleBb { get; init; }
    public decimal? LowerBb { get; init; }
    public decimal? NearestBuySideLiquidity { get; init; }
    public decimal? NearestSellSideLiquidity { get; init; }
    public LiquidityLevelDto? SweptLiquidityLevel { get; init; }
    public TradeDirection? SweepDirection { get; init; }
    public bool SweptOutsideBb { get; init; }
    public bool ClosedBackInsideBb { get; init; }
    public bool ClosedBackAcrossLiquidityLine { get; init; }
    public bool CisdDetected { get; init; }
    public decimal? CisdLevel { get; init; }
    public decimal? RsiPrimedSignalValue { get; init; }
    public decimal? RsiPrimedHaOpen { get; init; }
    public decimal? RsiPrimedHaHigh { get; init; }
    public decimal? RsiPrimedHaLow { get; init; }
    public decimal? RsiPrimedHaClose { get; init; }
    public decimal? RsiPrimedOhlc4 { get; init; }
    public bool RsiPrimedOversold { get; init; }
    public bool RsiPrimedOverbought { get; init; }
    public string? RsiPrimedImplementationMode { get; init; }
    public bool? RsiFilterPassed { get; init; }
    public bool EntryCandidate { get; init; }
    public string? RejectionReason { get; init; }
    public decimal? RiskReward { get; init; }
    public TargetSource? TargetSource { get; init; }
    public string? FinalDecision { get; init; }
    public string? LiquidityLineEngineMode { get; init; }
    public bool ItsImpossibleSourceAvailable { get; init; }
}

public sealed class ExternalLiquidityEngineInfoDto
{
    public required string ExternalIndicatorName { get; init; }
    public bool SourceCodeAvailable { get; init; }
    public required string ImplementationMode { get; init; }
    public string? ApproximationReason { get; init; }
    public IReadOnlyList<string> CompatibleSettings { get; init; } = [];
}

public sealed class BbLiquiditySweepEvaluationResult
{
    public required BbLiquiditySweepDiagnosticsDto Diagnostics { get; init; }
    public BbLiquiditySweepCandleMetrics? CandleMetrics { get; init; }
    public string? StagedRejectionCode { get; init; }
    public TradeDirection? Direction { get; init; }
    public decimal? EntryPrice { get; init; }
    public decimal? StopLoss { get; init; }
    public decimal? TakeProfit { get; init; }
    public decimal? BreakevenTriggerPrice { get; init; }
    public TargetSource? TargetSource { get; init; }
    public string? Reason { get; init; }
}
