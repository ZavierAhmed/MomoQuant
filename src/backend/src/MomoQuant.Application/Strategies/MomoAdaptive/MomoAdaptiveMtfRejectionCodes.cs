namespace MomoQuant.Application.Strategies.MomoAdaptive;

public static class MomoAdaptiveMtfRejectionCodes
{
    public const string MtfDataUnavailable = "MtfDataUnavailable";
    public const string InvalidParameters = "InvalidParameters";
    public const string UnsupportedRegime = "UnsupportedRegime";
    public const string HtfTrendNotAligned = "HtfTrendNotAligned";
    public const string HtfSlopeNotAligned = "HtfSlopeNotAligned";
    public const string ExecutionTrendNotAligned = "ExecutionTrendNotAligned";
    public const string VolatilityTooLow = "VolatilityTooLow";
    public const string VolatilityTooHigh = "VolatilityTooHigh";
    public const string NoBreakout = "NoBreakout";
    public const string BreakoutBufferNotMet = "BreakoutBufferNotMet";
    public const string MomentumNotConfirmed = "MomentumNotConfirmed";
    public const string WaitingForRetest = "WaitingForRetest";
    public const string RetestExpired = "RetestExpired";
    public const string RetestInvalidated = "RetestInvalidated";
    public const string BreakoutOverextended = "BreakoutOverextended";
    public const string StrengthBelowMinimum = "StrengthBelowMinimum";
    public const string InvalidStop = "InvalidStop";
    public const string InvalidTarget = "InvalidTarget";
    public const string DuplicateSetup = "DuplicateSetup";
    public const string EntryConfirmed = "EntryConfirmed";
}
