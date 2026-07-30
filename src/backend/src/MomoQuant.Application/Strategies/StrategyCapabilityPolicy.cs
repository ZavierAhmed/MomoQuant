using MomoQuant.Domain.Enums;

namespace MomoQuant.Application.Strategies;

/// <summary>
/// Authoritative capability gates for canonical strategies.
/// Archived codes are never optimizable/validatable for new work.
/// </summary>
public static class StrategyCapabilityPolicy
{
    public const string OptimizationNotSupportedMessage =
        "Strategy does not support parameter optimization.";

    public const string ValidationNotSupportedMessage =
        "Strategy does not support Validation Laboratory experiments until audited research datasets exist.";

    public static bool SupportsOptimization(StrategyCode code) =>
        CanonicalStrategyPortfolio.IsCanonicalActive(code)
        && code is StrategyCode.PriceStructureBreakoutRetest;

    public static bool SupportsOptimization(string? code)
    {
        if (!TryParse(code, out var parsed))
        {
            return false;
        }

        return SupportsOptimization(parsed);
    }

    public static bool SupportsValidation(StrategyCode code) =>
        CanonicalStrategyPortfolio.IsCanonicalActive(code)
        && code is StrategyCode.PriceStructureBreakoutRetest;

    public static bool SupportsValidation(string? code)
    {
        if (!TryParse(code, out var parsed))
        {
            return false;
        }

        return SupportsValidation(parsed);
    }

    public static bool SupportsStrategyLab(StrategyCode code) =>
        CanonicalStrategyPortfolio.IsCanonicalActive(code)
        && code is StrategyCode.MomoAdaptiveMultiTimeframeTrendBreakout
            or StrategyCode.PriceStructureBreakoutRetest
            or StrategyCode.MomoVolatilityRangeReversion;

    public static bool SupportsStrategyLab(string? code)
    {
        if (!TryParse(code, out var parsed))
        {
            return false;
        }

        return SupportsStrategyLab(parsed);
    }

    public static string? RejectStrategyLabReason(StrategyCode code)
    {
        if (!CanonicalStrategyPortfolio.CanCreateNewRun(code))
        {
            return CanonicalStrategyPortfolio.ArchivedCannotUseMessage(code.ToCode());
        }

        if (!SupportsStrategyLab(code))
        {
            return "Strategy is not enabled for Strategy Laboratory.";
        }

        return null;
    }

    public static string? RejectStrategyLabReason(string? code)
    {
        if (!TryParse(code, out var parsed))
        {
            return "Strategy code is invalid.";
        }

        return RejectStrategyLabReason(parsed);
    }

    public static string? RejectOptimizationReason(StrategyCode code)
    {
        if (!CanonicalStrategyPortfolio.CanCreateNewRun(code))
        {
            return CanonicalStrategyPortfolio.ArchivedCannotUseMessage(code.ToCode());
        }

        if (!SupportsOptimization(code))
        {
            return OptimizationNotSupportedMessage;
        }

        return null;
    }

    public static string? RejectOptimizationReason(string? code)
    {
        if (!TryParse(code, out var parsed))
        {
            return "Strategy code is invalid.";
        }

        return RejectOptimizationReason(parsed);
    }

    public static string? RejectValidationReason(StrategyCode code)
    {
        if (!CanonicalStrategyPortfolio.CanCreateNewRun(code))
        {
            return CanonicalStrategyPortfolio.ArchivedCannotUseMessage(code.ToCode());
        }

        if (!SupportsValidation(code))
        {
            return ValidationNotSupportedMessage;
        }

        return null;
    }

    public static string? RejectValidationReason(string? code)
    {
        if (!TryParse(code, out var parsed))
        {
            return "Strategy code is invalid.";
        }

        return RejectValidationReason(parsed);
    }

    private static bool TryParse(string? code, out StrategyCode parsed)
    {
        parsed = default;
        if (string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        try
        {
            parsed = StrategyCodeExtensions.FromCode(code);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }
}
