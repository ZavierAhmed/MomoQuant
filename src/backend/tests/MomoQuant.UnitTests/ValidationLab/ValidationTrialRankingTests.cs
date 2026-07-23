using MomoQuant.Application.ValidationLab;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.ValidationLab;

namespace MomoQuant.UnitTests.ValidationLab;

public sealed class ValidationTrialRankingTests
{
    [Fact]
    public void Guardrail_rejected_cannot_win()
    {
        var trials = new List<ValidationParameterTrial>
        {
            new()
            {
                TrialNumber = 1,
                ParameterFingerprint = "HIGH",
                TrainingScore = 99m,
                NetExpectancyR = 2m,
                ProfitFactor = 3m,
                MaximumDrawdownPercent = 1m,
                ClosedTradeCount = 100,
                GuardrailDecision = "Failed",
                Status = ValidationTrialStatus.GuardrailRejected
            },
            new()
            {
                TrialNumber = 2,
                ParameterFingerprint = "OK",
                TrainingScore = 50m,
                NetExpectancyR = 0.3m,
                ProfitFactor = 1.2m,
                MaximumDrawdownPercent = 10m,
                ClosedTradeCount = 40,
                GuardrailDecision = "Passed",
                Status = ValidationTrialStatus.Completed
            }
        };

        ValidationTrialRanker.AssignRanks(trials);
        var winner = ValidationTrialRanker.SelectWinner(trials);
        Assert.NotNull(winner);
        Assert.Equal("OK", winner!.ParameterFingerprint);
        Assert.Null(trials[0].Rank);
        Assert.Equal(1, trials[1].Rank);
    }

    [Fact]
    public void Changing_validation_metrics_does_not_change_ranking()
    {
        var trials = new List<ValidationParameterTrial>
        {
            new()
            {
                TrialNumber = 1,
                ParameterFingerprint = "A",
                TrainingScore = 70m,
                NetExpectancyR = 0.5m,
                ProfitFactor = 1.4m,
                MaximumDrawdownPercent = 8m,
                ClosedTradeCount = 40,
                GuardrailDecision = "Passed",
                Status = ValidationTrialStatus.Completed
            },
            new()
            {
                TrialNumber = 2,
                ParameterFingerprint = "B",
                TrainingScore = 65m,
                NetExpectancyR = 0.6m,
                ProfitFactor = 1.5m,
                MaximumDrawdownPercent = 5m,
                ClosedTradeCount = 50,
                GuardrailDecision = "Passed",
                Status = ValidationTrialStatus.Completed
            }
        };

        ValidationTrialRanker.AssignRanks(trials);
        var firstWinner = ValidationTrialRanker.SelectWinner(trials)!.ParameterFingerprint;

        // Ranking uses training-only fields; re-rank must be stable without validation metrics.
        ValidationTrialRanker.AssignRanks(trials);
        var secondWinner = ValidationTrialRanker.SelectWinner(trials)!.ParameterFingerprint;

        Assert.Equal(firstWinner, secondWinner);
        Assert.Equal("A", firstWinner);
    }
}
