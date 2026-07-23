using MomoQuant.Application.StrategyBenchmarks.Dtos;

namespace MomoQuant.Application.StrategyBenchmarks;

public interface IRiskConfidenceCalibrationAdvisor
{
    RiskConfidenceCalibrationDto Build(IReadOnlyList<StrategyBenchmarkRejectionQualityDto> qualityRows);
}

public sealed class RiskConfidenceCalibrationAdvisor : IRiskConfidenceCalibrationAdvisor
{
    public RiskConfidenceCalibrationDto Build(IReadOnlyList<StrategyBenchmarkRejectionQualityDto> qualityRows)
    {
        var confidenceRejected = qualityRows.Sum(item => item.RejectedByConfidenceCount + item.RejectedByBothCount);
        var riskRejected = qualityRows.Sum(item => item.RejectedByRiskCount + item.RejectedByBothCount);
        var confidenceFalse = qualityRows.Sum(item => item.ConfidenceFalseRejectCount);
        var confidenceCorrect = qualityRows.Sum(item => item.ConfidenceCorrectRejectCount);
        var riskFalse = qualityRows.Sum(item => item.RiskFalseRejectCount);
        var riskCorrect = qualityRows.Sum(item => item.RiskCorrectRejectCount);
        var confidenceFalseRate = confidenceRejected <= 0 ? 0m : Math.Round(confidenceFalse * 100m / confidenceRejected, 2);
        var riskFalseRate = riskRejected <= 0 ? 0m : Math.Round(riskFalse * 100m / riskRejected, 2);
        var confidenceCorrectRate = confidenceRejected <= 0 ? 0m : Math.Round(confidenceCorrect * 100m / confidenceRejected, 2);
        var riskCorrectRate = riskRejected <= 0 ? 0m : Math.Round(riskCorrect * 100m / riskRejected, 2);

        var evidence = new List<string>
        {
            $"Confidence false rejection rate: {confidenceFalseRate:0.##}%",
            $"Risk false rejection rate: {riskFalseRate:0.##}%",
            $"Confidence correct rejection rate: {confidenceCorrectRate:0.##}%",
            $"Risk correct rejection rate: {riskCorrectRate:0.##}%"
        };
        var warnings = new List<string>();
        var strategySuggestions = new List<string>();
        var riskSuggestions = new List<string>();
        decimal? recommendedThreshold = null;

        if (confidenceFalseRate > 45m)
        {
            warnings.Add("Confidence threshold appears strict; many rejected candidates had positive shadow outcomes.");
            strategySuggestions.Add("Consider lowering confidence threshold by 5-10 points for research mode.");
            riskSuggestions.Add("Keep risk rules enabled while reducing confidence threshold.");
            recommendedThreshold = 50m;
        }
        else if (riskFalseRate > 45m)
        {
            warnings.Add("Risk rules may be over-restrictive for current strategy sample.");
            strategySuggestions.Add("Review max risk per trade and minimum reward/risk constraints.");
            riskSuggestions.Add("Use RiskOnlyResearch mode to inspect rejected setups.");
        }
        else
        {
            strategySuggestions.Add("Current rejection behavior appears stable; continue FullValidation sampling.");
            riskSuggestions.Add("No immediate risk-rule loosening is recommended.");
        }

        return new RiskConfidenceCalibrationDto
        {
            ConfidenceFalseRejectionRatePercent = confidenceFalseRate,
            RiskFalseRejectionRatePercent = riskFalseRate,
            ConfidenceCorrectRejectionRatePercent = confidenceCorrectRate,
            RiskCorrectRejectionRatePercent = riskCorrectRate,
            ConfidenceThresholdRecommendation = recommendedThreshold,
            RiskRuleRecommendations = riskSuggestions,
            StrategySpecificRecommendations = strategySuggestions,
            Warnings = warnings,
            EvidenceSummary = evidence
        };
    }
}
