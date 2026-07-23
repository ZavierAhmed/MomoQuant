using MomoQuant.Application.Common;

namespace MomoQuant.Application.Strategies;

public static class StrategyStrengthHelper
{
    public static decimal ResolveStrength(decimal rawStrength, decimal minStrength) =>
        ConfidenceScoreNormalizer.Normalize(Math.Max(minStrength, rawStrength));

    public static bool IsSkipReason(string reason) =>
        reason.Contains("not supported", StringComparison.OrdinalIgnoreCase) ||
        reason.Contains("Insufficient", StringComparison.OrdinalIgnoreCase) ||
        reason.Contains("missing", StringComparison.OrdinalIgnoreCase) ||
        reason.Contains("is required", StringComparison.OrdinalIgnoreCase);
}
