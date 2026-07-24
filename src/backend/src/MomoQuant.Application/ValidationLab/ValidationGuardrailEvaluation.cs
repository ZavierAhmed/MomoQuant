using System.Globalization;
using MomoQuant.Domain.Enums;

namespace MomoQuant.Application.ValidationLab;

/// <summary>
/// Explicit guardrail failure codes for ValidationMetrics/v1.3.2 trials (Milestone 23.0D WP18).
/// A missing metric is never coalesced to 0 — it becomes NotEvaluated, and a mandatory
/// NotEvaluated guardrail makes the trial ineligible.
/// </summary>
public static class ValidationGuardrailFailureCodes
{
    public const string ClosedTradesBelowMinimum = "GUARDRAIL_CLOSED_TRADES_BELOW_MINIMUM";
    public const string ProfitFactorBelowMinimum = "GUARDRAIL_PROFIT_FACTOR_BELOW_MINIMUM";
    public const string ProfitFactorNotEvaluated = "GUARDRAIL_PROFIT_FACTOR_NOT_EVALUATED";
    public const string NetExpectancyBelowMinimum = "GUARDRAIL_NET_EXPECTANCY_BELOW_MINIMUM";
    public const string NetExpectancyNotEvaluated = "GUARDRAIL_NET_EXPECTANCY_NOT_EVALUATED";
    public const string MaxDrawdownExceeded = "GUARDRAIL_MAX_DRAWDOWN_EXCEEDED";
    public const string MaxDrawdownNotEvaluated = "GUARDRAIL_MAX_DRAWDOWN_NOT_EVALUATED";
    public const string MissingTrialMetricSnapshot = "MISSING_TRIAL_METRIC_SNAPSHOT";
}

public enum ValidationGuardrailOutcome
{
    Passed = 1,
    Failed = 2,
    NotEvaluated = 3,
    NotApplicable = 4
}

public sealed class ValidationGuardrailResult
{
    public string GuardrailKey { get; init; } = string.Empty;
    public bool IsMandatory { get; init; }
    public ValidationGuardrailOutcome Outcome { get; init; }
    public string? ActualValue { get; init; }
    public string? LimitValue { get; init; }

    /// <summary>Explicit failure code when Outcome is Failed, or a mandatory NotEvaluated.</summary>
    public string? FailureCode { get; init; }

    public string? Reason { get; init; }
}

public sealed class ValidationGuardrailEvaluation
{
    public const string Version = "ValidationGuardrailEvaluation/v1";

    public string EvaluationVersion { get; init; } = Version;
    public IReadOnlyList<ValidationGuardrailResult> Results { get; init; } = [];

    /// <summary>"Passed" or "Failed" — compatible with ValidationParameterTrial.GuardrailDecision.</summary>
    public string Decision { get; init; } = "Failed";

    public bool Passed { get; init; }

    /// <summary>Eligible for ranking only when every mandatory guardrail evaluated and passed.</summary>
    public bool IsRankEligible { get; init; }

    public IReadOnlyList<string> FailureCodes { get; init; } = [];
}

/// <summary>
/// ValidationMetrics/v1.3.2 guardrail evaluation. Never applies "(metric ?? 0m)" semantics:
/// null metrics produce NotEvaluated outcomes, and mandatory NotEvaluated → ineligible.
/// </summary>
public static class ValidationGuardrailEvaluator
{
    public static ValidationGuardrailEvaluation Evaluate(
        LayerSegmentMetrics metrics,
        ValidationQualificationProfile profile)
    {
        var results = new List<ValidationGuardrailResult>();

        // 1. Closed trades — always an evaluated integer population count.
        var closed = metrics.ClosedOutcomePopulationCount ?? metrics.ClosedTradeCount;
        results.Add(new ValidationGuardrailResult
        {
            GuardrailKey = "MinimumTrainingClosedTrades",
            IsMandatory = true,
            Outcome = closed >= profile.MinimumTrainingClosedTrades
                ? ValidationGuardrailOutcome.Passed
                : ValidationGuardrailOutcome.Failed,
            ActualValue = closed.ToString(CultureInfo.InvariantCulture),
            LimitValue = profile.MinimumTrainingClosedTrades.ToString(CultureInfo.InvariantCulture),
            FailureCode = closed >= profile.MinimumTrainingClosedTrades
                ? null
                : ValidationGuardrailFailureCodes.ClosedTradesBelowMinimum
        });

        // 2. Net profit factor. Infinity (all-winner population) passes any finite minimum;
        //    a null value without Infinity status is NotEvaluated (mandatory).
        var pf = metrics.NetProfitFactor ?? metrics.ProfitFactor;
        var pfStatus = metrics.NetProfitFactorStatus ?? metrics.ProfitFactorStatus;
        if (pf is decimal pfValue)
        {
            var pfPassed = pfValue >= profile.MinimumTrainingProfitFactor;
            results.Add(new ValidationGuardrailResult
            {
                GuardrailKey = "MinimumTrainingProfitFactor",
                IsMandatory = true,
                Outcome = pfPassed ? ValidationGuardrailOutcome.Passed : ValidationGuardrailOutcome.Failed,
                ActualValue = pfValue.ToString("G29", CultureInfo.InvariantCulture),
                LimitValue = profile.MinimumTrainingProfitFactor.ToString("G29", CultureInfo.InvariantCulture),
                FailureCode = pfPassed ? null : ValidationGuardrailFailureCodes.ProfitFactorBelowMinimum
            });
        }
        else if (pfStatus == ProfitFactorStatus.Infinity)
        {
            results.Add(new ValidationGuardrailResult
            {
                GuardrailKey = "MinimumTrainingProfitFactor",
                IsMandatory = true,
                Outcome = ValidationGuardrailOutcome.Passed,
                ActualValue = "Infinity",
                LimitValue = profile.MinimumTrainingProfitFactor.ToString("G29", CultureInfo.InvariantCulture),
                Reason = "Infinite profit factor (no losing PnL) satisfies any finite minimum."
            });
        }
        else
        {
            results.Add(new ValidationGuardrailResult
            {
                GuardrailKey = "MinimumTrainingProfitFactor",
                IsMandatory = true,
                Outcome = ValidationGuardrailOutcome.NotEvaluated,
                ActualValue = null,
                LimitValue = profile.MinimumTrainingProfitFactor.ToString("G29", CultureInfo.InvariantCulture),
                FailureCode = ValidationGuardrailFailureCodes.ProfitFactorNotEvaluated,
                Reason = $"NetProfitFactor unavailable (status: {pfStatus?.ToString() ?? "null"})."
            });
        }

        // 3. Net expectancy R — null means the metric could not be evaluated (mandatory).
        if (metrics.NetExpectancyR is decimal netExp)
        {
            var expPassed = netExp >= profile.MinimumTrainingNetExpectancyR;
            results.Add(new ValidationGuardrailResult
            {
                GuardrailKey = "MinimumTrainingNetExpectancyR",
                IsMandatory = true,
                Outcome = expPassed ? ValidationGuardrailOutcome.Passed : ValidationGuardrailOutcome.Failed,
                ActualValue = netExp.ToString("G29", CultureInfo.InvariantCulture),
                LimitValue = profile.MinimumTrainingNetExpectancyR.ToString("G29", CultureInfo.InvariantCulture),
                FailureCode = expPassed ? null : ValidationGuardrailFailureCodes.NetExpectancyBelowMinimum
            });
        }
        else
        {
            results.Add(new ValidationGuardrailResult
            {
                GuardrailKey = "MinimumTrainingNetExpectancyR",
                IsMandatory = true,
                Outcome = ValidationGuardrailOutcome.NotEvaluated,
                LimitValue = profile.MinimumTrainingNetExpectancyR.ToString("G29", CultureInfo.InvariantCulture),
                FailureCode = ValidationGuardrailFailureCodes.NetExpectancyNotEvaluated,
                Reason = $"NetExpectancyR unavailable (applicability: {metrics.NetExpectancyApplicability?.ToString() ?? "null"})."
            });
        }

        // 4. Maximum drawdown — enforced when the contract produces it. ValidationMetrics/v1.3.2
        //    normalized one-unit path aggregation defines no equity-relative drawdown, so a null
        //    value is NotApplicable (non-mandatory) rather than silently passing as 0.
        if (metrics.MaximumRealizedDrawdownPercent is decimal dd)
        {
            var ddPassed = dd <= profile.MaximumTrainingDrawdownPercent;
            results.Add(new ValidationGuardrailResult
            {
                GuardrailKey = "MaximumTrainingDrawdownPercent",
                IsMandatory = false,
                Outcome = ddPassed ? ValidationGuardrailOutcome.Passed : ValidationGuardrailOutcome.Failed,
                ActualValue = dd.ToString("G29", CultureInfo.InvariantCulture),
                LimitValue = profile.MaximumTrainingDrawdownPercent.ToString("G29", CultureInfo.InvariantCulture),
                FailureCode = ddPassed ? null : ValidationGuardrailFailureCodes.MaxDrawdownExceeded
            });
        }
        else
        {
            var isPopulationContract = ValidationMetricsContract.IsPopulationPathMetricsVersion(metrics.MetricsVersion);
            results.Add(new ValidationGuardrailResult
            {
                GuardrailKey = "MaximumTrainingDrawdownPercent",
                IsMandatory = false,
                Outcome = isPopulationContract
                    ? ValidationGuardrailOutcome.NotApplicable
                    : ValidationGuardrailOutcome.NotEvaluated,
                LimitValue = profile.MaximumTrainingDrawdownPercent.ToString("G29", CultureInfo.InvariantCulture),
                FailureCode = isPopulationContract ? null : ValidationGuardrailFailureCodes.MaxDrawdownNotEvaluated,
                Reason = isPopulationContract
                    ? "Normalized one-unit path aggregation defines no equity-relative drawdown."
                    : "MaximumRealizedDrawdownPercent unavailable."
            });
        }

        var failureCodes = results
            .Where(r => r.FailureCode is not null
                        && (r.Outcome == ValidationGuardrailOutcome.Failed
                            || (r.IsMandatory && r.Outcome == ValidationGuardrailOutcome.NotEvaluated)))
            .Select(r => r.FailureCode!)
            .ToList();
        var passed = failureCodes.Count == 0;

        return new ValidationGuardrailEvaluation
        {
            Results = results,
            Decision = passed ? "Passed" : "Failed",
            Passed = passed,
            IsRankEligible = passed,
            FailureCodes = failureCodes
        };
    }
}
