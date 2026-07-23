namespace MomoQuant.Application.Risk.Dtos;

public sealed class RiskProfileDto
{
    public required long Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required bool IsDefault { get; init; }
}

public sealed class CreateRiskProfileRequest
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public bool IsDefault { get; init; }
}

public sealed class UpdateRiskProfileRequest
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public bool IsDefault { get; init; }
}

public sealed class RiskRuleDto
{
    public required long Id { get; init; }
    public required string RuleKey { get; init; }
    public required string RuleValue { get; init; }
    public required Domain.Enums.SettingValueType ValueType { get; init; }
    public required bool IsEnabled { get; init; }
}

public sealed class UpdateRiskRuleItem
{
    public required string RuleKey { get; init; }
    public required string RuleValue { get; init; }
    public required Domain.Enums.SettingValueType ValueType { get; init; }
    public bool IsEnabled { get; init; } = true;
}

public sealed class UpdateRiskRulesRequest
{
    public required List<UpdateRiskRuleItem> Rules { get; init; }
}

public sealed class RiskDecisionDto
{
    public required long Id { get; init; }
    public long? TradingSessionId { get; init; }
    public long? SignalId { get; init; }
    public long? AiDecisionId { get; init; }
    public required long SymbolId { get; init; }
    public required string Decision { get; init; }
    public required string Reason { get; init; }
    public decimal? ApprovedRiskPercent { get; init; }
    public decimal? PositionSize { get; init; }
    public decimal? StopLoss { get; init; }
    public decimal? TakeProfit { get; init; }
    public string? RejectedRuleKey { get; init; }
    public required DateTime CreatedAtUtc { get; init; }
}

public sealed class RiskEvaluationRequest
{
    public required long RiskProfileId { get; init; }
    public required long SymbolId { get; init; }
    public required string Direction { get; init; }
    public required decimal EntryPrice { get; init; }
    public decimal? SuggestedStopLoss { get; init; }
    public decimal? SuggestedTakeProfit { get; init; }
    public required decimal ConfidenceScore { get; init; }
    public string? StrategyCode { get; init; }
    public long? SignalId { get; init; }
    public long? AiDecisionId { get; init; }
    public long? TradingSessionId { get; init; }
    public required decimal AccountBalance { get; init; }
    public decimal DailyPnl { get; init; }
    public decimal WeeklyPnl { get; init; }
    public int OpenPositionCount { get; init; }
    public decimal OpenSymbolExposure { get; init; }
    public decimal TotalExposure { get; init; }
    public int ConsecutiveLosses { get; init; }
    public decimal? SpreadPercent { get; init; }
    public decimal? AtrPercent { get; init; }
    public string? MarketRegime { get; init; }
    public bool EmergencyStopEnabled { get; init; }
    public bool PersistDecision { get; init; }
}

public sealed class RiskEvaluationResponse
{
    public required bool Approved { get; init; }
    public required string Decision { get; init; }
    public required string Reason { get; init; }
    public string? RejectedRuleKey { get; init; }
    public decimal? ApprovedRiskPercent { get; init; }
    public decimal? PositionSize { get; init; }
    public decimal? StopLoss { get; init; }
    public decimal? TakeProfit { get; init; }
    public decimal? RiskAmount { get; init; }
    public long? RiskDecisionId { get; init; }
}
