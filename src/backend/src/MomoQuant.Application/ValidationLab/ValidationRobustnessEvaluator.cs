using MomoQuant.Domain.Enums;

namespace MomoQuant.Application.ValidationLab;

/// <summary>
/// Backward-compatible facade; Milestone 22.1 uses ValidationVerdictService for structured rules.
/// </summary>
public static class ValidationRobustnessEvaluator
{
    private static readonly ValidationVerdictService Verdict = new();

    public static RobustnessVerdictResult Evaluate(
        LayerSegmentMetrics training,
        LayerSegmentMetrics validation,
        ValidationQualificationProfile profile,
        bool parameterStabilityOk = true,
        bool dataQualityOk = true,
        bool configurationMatch = true,
        ValidationExperimentType experimentType = ValidationExperimentType.ValidateExistingFrozenConfiguration,
        ParameterStabilityApplicability stabilityApplicability = ParameterStabilityApplicability.NotApplicable,
        CandidateReconciliationStatus? reconciliationStatus = null,
        ValidationLeakageAuditStatus? leakageStatus = null,
        bool metricConsistencyOk = true)
    {
        // Preserve prior behavior for ValidateExisting: never fail on stability when not applicable.
        if (experimentType == ValidationExperimentType.ValidateExistingFrozenConfiguration)
        {
            stabilityApplicability = ParameterStabilityApplicability.NotApplicable;
            parameterStabilityOk = true;
        }

        return Verdict.Evaluate(
            training,
            validation,
            profile,
            experimentType,
            stabilityApplicability,
            parameterStabilityOk,
            dataQualityOk,
            configurationMatch,
            reconciliationStatus,
            leakageStatus,
            metricConsistencyOk);
    }

    public static ValidationOverlayStatus CompareOverlay(
        LayerSegmentMetrics rawValidation,
        LayerSegmentMetrics overlayValidation,
        int minSample)
    {
        if (overlayValidation.ClosedTradeCount < minSample)
        {
            return ValidationOverlayStatus.InsufficientSample;
        }

        var rawExp = rawValidation.NetExpectancyR ?? 0m;
        var ovExp = overlayValidation.NetExpectancyR ?? 0m;
        var rawPf = rawValidation.NetProfitFactor ?? rawValidation.ProfitFactor ?? 0m;
        var ovPf = overlayValidation.NetProfitFactor ?? overlayValidation.ProfitFactor ?? 0m;
        var improved = ovExp > rawExp && ovPf >= rawPf;
        var degraded = ovExp < rawExp && ovPf <= rawPf;
        if (improved) return ValidationOverlayStatus.Improved;
        if (degraded) return ValidationOverlayStatus.Degraded;
        return ValidationOverlayStatus.Mixed;
    }
}
