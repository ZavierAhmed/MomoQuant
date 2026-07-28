namespace MomoQuant.Application.Strategies.MomoRange;

public static class MomoVolatilityRangeRejectionCodes
{
    public const string InsufficientData = "InsufficientData";
    public const string RangeTooNarrow = "RangeTooNarrow";
    public const string RangeTooWide = "RangeTooWide";
    public const string TrendFilterFailed = "TrendFilterFailed";
    public const string VolatilityTooLow = "VolatilityTooLow";
    public const string VolatilityTooHigh = "VolatilityTooHigh";
    public const string NoBoundaryProbe = "NoBoundaryProbe";
    public const string CloseDidNotReclaim = "CloseDidNotReclaim";
    public const string RsiNotExtreme = "RsiNotExtreme";
    public const string WickConfirmationMissing = "WickConfirmationMissing";
    public const string RewardRiskInsufficient = "RewardRiskInsufficient";
    public const string InvalidStop = "InvalidStop";
    public const string DuplicateSetup = "DuplicateSetup";
    public const string EntryConfirmed = "EntryConfirmed";
}
