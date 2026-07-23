namespace MomoQuant.Application.Strategies.PriceStructure;

public static class PriceStructureRejectionCodes
{
    public const string NoConfirmedSwing = "NoConfirmedSwing";
    public const string NoBreakout = "NoBreakout";
    public const string WaitingForRetest = "WaitingForRetest";
    public const string RetestExpired = "RetestExpired";
    public const string RetestInvalidated = "RetestInvalidated";
    public const string NoConfirmation = "NoConfirmation";
    public const string InvalidStop = "InvalidStop";
    public const string DuplicateSetup = "DuplicateSetup";
    public const string NoLiquidityLevel = "NoLiquidityLevel";
    public const string NoSweep = "NoSweep";
    public const string SweepDidNotReclaim = "SweepDidNotReclaim";
    public const string ReclaimExpired = "ReclaimExpired";
    public const string InsufficientData = "InsufficientData";
}
