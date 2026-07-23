using MomoQuant.Domain.Constants;

namespace MomoQuant.Application.Strategies.VolatilityGatedSuperTrend;

public static class VgResearchProfilePresets
{
    public const string Conservative = "Conservative";
    public const string Balanced = "Balanced";
    public const string Exploratory = "Exploratory";

    public static IReadOnlyList<string> SupportedProfiles { get; } =
        [Conservative, Balanced, Exploratory];

    public static bool IsExploratory(string? profile) =>
        string.Equals(profile, Exploratory, StringComparison.OrdinalIgnoreCase);

    public static IReadOnlyDictionary<string, string> Apply(
        string? profile,
        IReadOnlyDictionary<string, string>? baseParameters = null)
    {
        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (baseParameters is not null)
        {
            foreach (var (key, value) in baseParameters)
            {
                merged[key] = value;
            }
        }

        var normalized = string.IsNullOrWhiteSpace(profile) ? Balanced : profile.Trim();
        switch (normalized)
        {
            case Conservative:
                merged["requireRetest"] = "true";
                merged["minVolatilityRatio"] = "1.10";
                merged["fixedRewardRisk"] = "2.0";
                merged["retestAtrTolerance"] = "0.25";
                merged["allowTrendContinuationEntry"] = "false";
                break;
            case Exploratory:
                merged["requireRetest"] = "false";
                merged["minVolatilityRatio"] = "0.90";
                merged["fixedRewardRisk"] = "1.5";
                merged["retestAtrTolerance"] = "0.70";
                merged["allowTrendContinuationEntry"] = "true";
                break;
            default:
                merged["requireRetest"] = "true";
                merged["minVolatilityRatio"] = "1.00";
                merged["fixedRewardRisk"] = "1.8";
                merged["retestAtrTolerance"] = "0.45";
                merged["allowTrendContinuationEntry"] = "false";
                break;
        }

        return merged;
    }

    public static string ProfileLabel(string? profile) =>
        string.IsNullOrWhiteSpace(profile) ? Balanced : profile.Trim();

    public static bool AppliesToStrategy(string strategyCode) =>
        string.Equals(strategyCode, StrategyCodes.VolatilityGatedSupertrendMomentum, StringComparison.OrdinalIgnoreCase);
}
