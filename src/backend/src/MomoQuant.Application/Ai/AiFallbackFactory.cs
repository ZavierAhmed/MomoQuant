using MomoQuant.Application.Ai.Dtos;

namespace MomoQuant.Application.Ai;

public static class AiFallbackFactory
{
    public const string UnavailableWarning = "AI service unavailable";

    public static DetectRegimeResponseDto CreateRegimeFallback() => new()
    {
        Regime = "Unknown",
        Confidence = 0,
        Reasons = [UnavailableWarning],
        UsedFallback = true
    };

    public static ScoreConfidenceResponseDto CreateConfidenceFallback() => new()
    {
        AdvisoryRulesVersion = "AdvisoryRules/v1",
        EvaluationStatus = "NotEvaluated",
        IsStrategySupported = false,
        SupportedInputs = [],
        MissingInputs = [],
        AdvisoryScore = null,
        AdvisoryClassification = "VeryLow",
        ConfidenceScore = 0,
        Classification = "VeryLow",
        Reasons = [],
        Warnings = [UnavailableWarning],
        UsedFallback = true,
        AdvisoryEligible = false
    };

    public static DetectAnomalyResponseDto CreateAnomalyFallback() => new()
    {
        IsAnomalous = false,
        Severity = "None",
        Reasons = [UnavailableWarning],
        UsedFallback = true
    };

    public static ExplainTradeResponseDto CreateExplainFallback() => new()
    {
        Summary = "AI explanation is unavailable.",
        Details = [UnavailableWarning],
        Caution = "Do not trade based on fallback AI output.",
        UsedFallback = true
    };

    /// <summary>Advisory-only eligibility helper. Does not authorize trade execution.</summary>
    public static bool IsAdvisoryEligible(DetectRegimeResponseDto regime, ScoreConfidenceResponseDto confidence, DetectAnomalyResponseDto? anomaly)
    {
        if (regime.UsedFallback || confidence.UsedFallback)
        {
            return false;
        }

        if (anomaly?.UsedFallback == true)
        {
            return false;
        }

        if (anomaly?.IsAnomalous == true &&
            string.Equals(anomaly.Severity, "High", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (IsNonEvaluatedStatus(confidence.EvaluationStatus))
        {
            return false;
        }

        var score = confidence.AdvisoryScore ?? confidence.ConfidenceScore;
        return score >= 80;
    }

    /// <summary>Temporary compat alias for <see cref="IsAdvisoryEligible"/>.</summary>
    public static bool IsTradeAllowed(DetectRegimeResponseDto regime, ScoreConfidenceResponseDto confidence, DetectAnomalyResponseDto? anomaly) =>
        IsAdvisoryEligible(regime, confidence, anomaly);

    private static bool IsNonEvaluatedStatus(string? status) =>
        !string.IsNullOrWhiteSpace(status)
        && (string.Equals(status, "UnsupportedStrategy", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "InsufficientInputs", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "InvalidInputs", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "NotEvaluated", StringComparison.OrdinalIgnoreCase));
}
