namespace MomoQuant.Application.StrategyLab;

/// <summary>
/// Bounded collection-query contracts for Strategy Lab run endpoints.
/// </summary>
public static class StrategyLabQueryLimits
{
    public const int RecentRunsDefault = 50;
    public const int RunsByStrategyDefault = 20;
    public const int RunsMaximum = 200;

    public static int NormalizeRecentRuns(int requested) => Normalize(requested, RecentRunsDefault);

    public static int NormalizeRunsByStrategy(int requested) => Normalize(requested, RunsByStrategyDefault);

    private static int Normalize(int requested, int fallback) =>
        requested <= 0 ? fallback : Math.Min(requested, RunsMaximum);
}
