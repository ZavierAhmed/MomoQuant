namespace MomoQuant.Application.StrategyLab.Risk;

/// <summary>
/// Centralized decimal-safe risk limit comparisons for Strategy Lab.
/// Documented boundary policy (Milestone 21.5.2):
/// Inclusive maxima (equality allowed): leverage, margin, notional, concurrent risk,
/// open positions, drawdown, fee-efficiency hard cap.
/// Exclusive maxima (equality blocks): daily-loss entry gate.
/// Inclusive minima (equality allowed): reward/risk, risk score, confidence.
/// </summary>
public static class RiskLimitComparison
{
    /// <summary>Absolute tolerance for percentage/score comparisons (avoids FP noise).</summary>
    public const decimal DefaultTolerance = 0.0000001m;

    /// <summary>actual &lt;= limit (+tolerance). Equality passes.</summary>
    public static bool IsWithinInclusiveMaximum(decimal actual, decimal limit, decimal tolerance = DefaultTolerance) =>
        actual <= limit + tolerance;

    /// <summary>actual &lt; limit (−tolerance). Equality fails (blocks new entries).</summary>
    public static bool IsBelowExclusiveMaximum(decimal actual, decimal limit, decimal tolerance = DefaultTolerance) =>
        actual < limit - tolerance;

    /// <summary>actual &gt;= minimum (−tolerance). Equality passes.</summary>
    public static bool MeetsInclusiveMinimum(decimal actual, decimal minimum, decimal tolerance = DefaultTolerance) =>
        actual + tolerance >= minimum;

    public static bool ApproximatelyEqual(decimal a, decimal b, decimal tolerance = DefaultTolerance) =>
        Math.Abs(a - b) <= tolerance;
}
