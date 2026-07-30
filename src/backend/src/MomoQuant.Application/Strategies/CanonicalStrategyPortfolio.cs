using MomoQuant.Domain.Constants;
using MomoQuant.Domain.Enums;

namespace MomoQuant.Application.Strategies;

/// <summary>
/// Authoritative operational portfolio for Milestone 23.1A.
/// Only these three codes may be registered, enabled, or used for new runs.
/// </summary>
public static class CanonicalStrategyPortfolio
{
    public const string ArchivedCannotEnableMessage =
        "This strategy is archived and cannot be enabled for new runs.";

    public static string ArchivedCannotUseMessage(string code) =>
        $"Strategy '{code}' is archived and cannot be used for new runs.";

    public static IReadOnlyList<string> ActiveCodes { get; } =
    [
        StrategyCodes.MomoAdaptiveMultiTimeframeTrendBreakout,
        StrategyCodes.PriceStructureBreakoutRetest,
        StrategyCodes.MomoVolatilityRangeReversion
    ];

    public static IReadOnlyList<StrategyCode> ActiveStrategyCodes { get; } =
    [
        StrategyCode.MomoAdaptiveMultiTimeframeTrendBreakout,
        StrategyCode.PriceStructureBreakoutRetest,
        StrategyCode.MomoVolatilityRangeReversion
    ];

    /// <summary>Canonical strategies eligible for new Strategy Laboratory research runs.</summary>
    public static IReadOnlyList<string> StrategyLabNewRunCodes { get; } =
    [
        StrategyCodes.MomoAdaptiveMultiTimeframeTrendBreakout,
        StrategyCodes.PriceStructureBreakoutRetest,
        StrategyCodes.MomoVolatilityRangeReversion
    ];

    public static bool IsStrategyLabNewRunCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        foreach (var allowed in StrategyLabNewRunCodes)
        {
            if (string.Equals(allowed, code, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsCanonicalActive(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        foreach (var active in ActiveCodes)
        {
            if (string.Equals(active, code, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsCanonicalActive(StrategyCode code) =>
        ActiveStrategyCodes.Contains(code);

    public static bool CanCreateNewRun(string? code) => IsCanonicalActive(code);

    public static bool CanCreateNewRun(StrategyCode code) => IsCanonicalActive(code);

    public static bool CanExecute(string? code) => IsCanonicalActive(code);

    public static bool CanExecute(StrategyCode code) => IsCanonicalActive(code);

    public static bool CanEnable(string? code) => IsCanonicalActive(code);

    public static bool CanEnable(StrategyCode code) => IsCanonicalActive(code);

    public static bool TryParseCanonical(string? code, out StrategyCode strategyCode)
    {
        strategyCode = default;
        if (string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        try
        {
            var parsed = StrategyCodeExtensions.FromCode(code);
            if (IsCanonicalActive(parsed))
            {
                strategyCode = parsed;
                return true;
            }
            
            strategyCode = default;
            return false;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }
}

public enum StrategyPortfolioStatus
{
    Active = 0,
    Archived = 1
}
