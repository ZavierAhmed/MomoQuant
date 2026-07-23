namespace MomoQuant.Application;

public static class AuthorizationPolicies
{
    public const string AdminOnly = "AdminOnly";
    public const string AdminOrTrader = "AdminOrTrader";
    /// <summary>Read-only research access: Admin, Trader, Viewer.</summary>
    public const string ResearchRead = "ResearchRead";
    /// <summary>Mutating research execution: Admin, Trader only.</summary>
    public const string ResearchExecute = "ResearchExecute";
}
