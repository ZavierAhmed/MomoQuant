using MomoQuant.Domain.Enums;

namespace MomoQuant.Application.TradingSystems;

/// <summary>
/// Timeframes supported for Trading System analysis. These are analysis-only timeframes
/// and are intentionally separate from strategy execution timeframes.
/// </summary>
public static class SkSystemConstants
{
    public const string SystemCode = "SK_SYSTEM";
    public const string SystemName = "SK System Analyzer";

    public const string AnalysisOnlyDisclaimer =
        "This is chart analysis only. It does not create trades, orders, benchmark runs, or bot signals.";

    /// <summary>All timeframes supported for SK market-data import and analysis.</summary>
    public static readonly IReadOnlyList<string> SupportedAnalysisTimeframes =
        ["1m", "3m", "5m", "15m", "30m", "1h", "4h", "1d", "1w"];

    public static readonly IReadOnlyList<string> SupportedPrimaryTimeframes =
        ["1m", "3m", "5m", "15m", "30m", "1h", "4h"];

    public static readonly IReadOnlyList<string> SupportedHigherTimeframes =
        ["15m", "30m", "1h", "4h", "1d", "1w"];

    public static readonly IReadOnlyList<string> SupportedSensitivities =
        ["Conservative", "Balanced", "Aggressive"];

    public static readonly IReadOnlyList<string> SupportedDirectionModes =
        ["Auto", "BullishOnly", "BearishOnly"];

    public static readonly IReadOnlyList<string> SupportedExplanationModes =
        ["Beginner", "Intermediate", "Expert"];

    public static readonly IReadOnlyList<string> SupportedQuickViewModes =
        ["Clean", "Beginner", "Advanced"];

    public const string DefaultExplanationMode = "Beginner";
    public const string DefaultQuickViewMode = "Beginner";
    public const string DefaultPrimaryTimeframe = "15m";
    public const string DefaultHigherTimeframe = "4h";

    public const int MinLookbackCandles = 50;
    public const int MaxLookbackCandles = 1000;
    public const int DefaultLookbackCandles = 500;

    public static bool IsSupportedAnalysisTimeframe(string? value) =>
        value is not null && SupportedAnalysisTimeframes.Contains(value, StringComparer.OrdinalIgnoreCase);

    public static bool IsSupportedPrimaryTimeframe(string? value) =>
        value is not null && SupportedPrimaryTimeframes.Contains(value, StringComparer.OrdinalIgnoreCase);

    public static bool IsSupportedHigherTimeframe(string? value) =>
        value is not null && SupportedHigherTimeframes.Contains(value, StringComparer.OrdinalIgnoreCase);

    public static string NormalizeSensitivity(string? value) =>
        SupportedSensitivities.FirstOrDefault(
            s => string.Equals(s, value, StringComparison.OrdinalIgnoreCase)) ?? "Balanced";

    public static string NormalizeDirectionMode(string? value) =>
        SupportedDirectionModes.FirstOrDefault(
            m => string.Equals(m, value, StringComparison.OrdinalIgnoreCase)) ?? "Auto";

    public static string NormalizeExplanationMode(string? value) =>
        SupportedExplanationModes.FirstOrDefault(
            m => string.Equals(m, value, StringComparison.OrdinalIgnoreCase)) ?? DefaultExplanationMode;

    public static string NormalizeQuickViewMode(string? value) =>
        SupportedQuickViewModes.FirstOrDefault(
            m => string.Equals(m, value, StringComparison.OrdinalIgnoreCase)) ?? DefaultQuickViewMode;

    public static int ResolveSwingBars(string sensitivity, int minSwingCandles) =>
        sensitivity switch
        {
            "Conservative" => Math.Max(minSwingCandles + 2, 3),
            "Aggressive" => Math.Max(minSwingCandles - 1, 1),
            _ => Math.Max(minSwingCandles, 2)
        };

    public static decimal ResolveMinDistanceMultiplier(string sensitivity) =>
        sensitivity switch
        {
            "Conservative" => 1.5m,
            "Aggressive" => 0.5m,
            _ => 1.0m
        };
}
