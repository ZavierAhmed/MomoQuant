using System.Text.Json;
using MomoQuant.Application.Common;
using MomoQuant.Application.Risk.Models;
using MomoQuant.Application.Strategies.Models;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Risk;

namespace MomoQuant.Application.Trading;

public static class TradingPipelineConfidence
{
    public static bool ShouldEvaluateRisk(StrategyEvaluationResult evaluation) =>
        evaluation.IsValid &&
        evaluation.SignalType == SignalType.Entry &&
        evaluation.Direction is not TradeDirection.None;

    public static ConfidenceResolution ResolveConfidence(
        StrategyEvaluationResult evaluation,
        AiScoringInput? aiInput)
    {
        var strategyConfidence = ConfidenceScoreNormalizer.Normalize(evaluation.Strength);

        if (aiInput is null || !aiInput.Enabled)
        {
            return new ConfidenceResolution
            {
                StrategyConfidence = strategyConfidence,
                CombinedConfidence = strategyConfidence,
                RawCombinedScore = evaluation.Strength,
                AiEnabled = false,
                Source = "Strategy"
            };
        }

        if (!aiInput.Succeeded || !aiInput.Valid || !aiInput.Score.HasValue)
        {
            return new ConfidenceResolution
            {
                StrategyConfidence = strategyConfidence,
                CombinedConfidence = strategyConfidence,
                RawCombinedScore = evaluation.Strength,
                AiEnabled = true,
                AiSucceeded = false,
                AiFallbackUsed = true,
                AiWarning = aiInput.Warning ?? AiScoringHelper.UnavailableWarning,
                Source = "Strategy"
            };
        }

        var aiConfidence = ConfidenceScoreNormalizer.Normalize(aiInput.Score);
        var combined = Math.Clamp(strategyConfidence * 0.70m + aiConfidence * 0.30m, 0m, 100m);

        return new ConfidenceResolution
        {
            StrategyConfidence = strategyConfidence,
            AiConfidence = aiConfidence,
            CombinedConfidence = combined,
            RawCombinedScore = combined,
            AiEnabled = true,
            AiSucceeded = true,
            AiFallbackUsed = false,
            Source = "Combined"
        };
    }

    public static decimal ResolveEffectiveMinimum(decimal sessionMinConfidenceScore, IReadOnlyList<RiskRule> rules) =>
        EffectiveMinConfidenceResolver.Resolve(
            sessionMinConfidenceScore,
            RiskRuleSet.FromRules(rules).MinConfidenceScore);

    public static string BuildConfidenceDiagnosticsJson(ConfidenceResolution resolution, decimal minimumConfidenceScore) =>
        JsonSerializer.Serialize(new
        {
            strategyConfidence = resolution.StrategyConfidence,
            aiConfidence = resolution.AiConfidence,
            combinedConfidence = resolution.CombinedConfidence,
            rawConfidenceScore = resolution.RawCombinedScore,
            normalizedConfidenceScore = resolution.CombinedConfidence,
            minimumConfidenceScore,
            source = resolution.Source,
            aiEnabled = resolution.AiEnabled,
            aiSucceeded = resolution.AiSucceeded,
            aiFallbackUsed = resolution.AiFallbackUsed,
            aiWarning = resolution.AiWarning
        });

    public static string BuildConfidenceDiagnosticsJson(
        decimal rawConfidenceScore,
        decimal normalizedConfidenceScore,
        decimal minimumConfidenceScore,
        string source) =>
        JsonSerializer.Serialize(new
        {
            rawConfidenceScore,
            normalizedConfidenceScore,
            minimumConfidenceScore,
            source
        });
}
