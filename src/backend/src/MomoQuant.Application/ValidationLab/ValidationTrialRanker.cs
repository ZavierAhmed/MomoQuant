using MomoQuant.Domain.ValidationLab;

namespace MomoQuant.Application.ValidationLab;

/// <summary>
/// Ranks trials using training-only fields. Validation metrics must never affect ranking.
/// </summary>
public static class ValidationTrialRanker
{
    public static IReadOnlyList<ValidationParameterTrial> OrderForRanking(
        IEnumerable<ValidationParameterTrial> trials)
    {
        return trials
            .Where(t => string.Equals(t.GuardrailDecision, "Passed", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(t => t.TrainingScore ?? decimal.MinValue)
            .ThenByDescending(t => t.NetExpectancyR ?? decimal.MinValue)
            .ThenByDescending(t => t.ProfitFactor ?? decimal.MinValue)
            .ThenBy(t => t.MaximumDrawdownPercent ?? decimal.MaxValue)
            .ThenByDescending(t => t.ClosedTradeCount)
            .ThenBy(t => t.ParameterFingerprint, StringComparer.Ordinal)
            .ToList();
    }

    public static void AssignRanks(IList<ValidationParameterTrial> trials)
    {
        foreach (var trial in trials)
        {
            trial.Rank = null;
        }

        var ordered = OrderForRanking(trials);
        for (var i = 0; i < ordered.Count; i++)
        {
            ordered[i].Rank = i + 1;
        }
    }

    public static ValidationParameterTrial? SelectWinner(IEnumerable<ValidationParameterTrial> trials) =>
        OrderForRanking(trials).FirstOrDefault();
}
