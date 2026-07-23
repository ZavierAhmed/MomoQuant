namespace MomoQuant.Domain.Risk;

public static class RiskRuleKeys
{
    public const string MaxRiskPerTradePercent = "MaxRiskPerTradePercent";
    public const string MaxDailyLossPercent = "MaxDailyLossPercent";
    public const string MaxWeeklyLossPercent = "MaxWeeklyLossPercent";
    public const string MaxOpenPositions = "MaxOpenPositions";
    public const string MaxExposurePerSymbolPercent = "MaxExposurePerSymbolPercent";
    public const string MaxTotalExposurePercent = "MaxTotalExposurePercent";
    public const string MaxCorrelatedExposurePercent = "MaxCorrelatedExposurePercent";
    public const string MaxConsecutiveLosses = "MaxConsecutiveLosses";
    public const string MinConfidenceScore = "MinConfidenceScore";
    public const string MaxSpreadPercent = "MaxSpreadPercent";
    public const string MaxAtrPercent = "MaxAtrPercent";
    public const string EmergencyStopEnabled = "EmergencyStopEnabled";
    public const string RequireStopLoss = "RequireStopLoss";
    public const string MinRewardRiskRatio = "MinRewardRiskRatio";
}
