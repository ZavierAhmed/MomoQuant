namespace MomoQuant.Application.ValidationLab;

/// <summary>
/// ValidationTrainingScore/v1 — training-only objective (0–100).
/// A Expectancy 0–30, B PF 0–20, C Drawdown 0–15, D Sample 0–15, E Cost 0–10, F Stability 0–10.
/// </summary>
public static class ValidationTrainingScoreVersions
{
    public const string Current = "ValidationTrainingScore/v1";
}

public sealed class ValidationTrainingScoreBreakdown
{
    public string Version { get; init; } = ValidationTrainingScoreVersions.Current;
    public decimal ExpectancyQuality { get; init; }
    public decimal ProfitFactorQuality { get; init; }
    public decimal DrawdownQuality { get; init; }
    public decimal SampleSufficiency { get; init; }
    public decimal CostEfficiency { get; init; }
    public decimal OpportunityStability { get; init; }
    public decimal Total { get; init; }
    public IReadOnlyList<string> Notes { get; init; } = [];
}

public static class ValidationTrainingScoreCalculator
{
    public static ValidationTrainingScoreBreakdown Calculate(
        int closedTrades,
        decimal? netExpectancyR,
        decimal? profitFactor,
        decimal? maxDrawdownPercent,
        decimal? feeToGrossProfitPercent,
        decimal opportunityRatePer1000,
        int minimumClosedTrades = 30)
    {
        var notes = new List<string>();
        if (closedTrades < minimumClosedTrades)
        {
            notes.Add($"Insufficient sample for full scoring ({closedTrades} < {minimumClosedTrades}).");
        }

        var expectancy = netExpectancyR ?? 0m;
        var expectancyScore = expectancy <= 0m
            ? 0m
            : Math.Min(30m, expectancy * 15m);

        var pf = profitFactor ?? 0m;
        var pfScore = pf <= 0m
            ? 0m
            : pf >= 2m ? 20m : Math.Min(20m, (pf - 1m) * 20m);

        var dd = maxDrawdownPercent ?? 100m;
        var ddScore = dd <= 0m ? 15m : dd >= 25m ? 0m : Math.Round(15m * (1m - dd / 25m), 2);

        var sampleScore = closedTrades <= 0
            ? 0m
            : closedTrades >= minimumClosedTrades
                ? 15m
                : Math.Round(15m * closedTrades / minimumClosedTrades, 2);

        var feePct = feeToGrossProfitPercent ?? 0m;
        var costScore = feePct <= 0m ? 10m : feePct >= 50m ? 0m : Math.Round(10m * (1m - feePct / 50m), 2);

        var stabScore = opportunityRatePer1000 <= 0m
            ? 0m
            : opportunityRatePer1000 >= 5m
                ? 10m
                : Math.Round(Math.Min(10m, opportunityRatePer1000 * 2m), 2);

        // Cap total when sample insufficient — do not fabricate high scores.
        var total = expectancyScore + pfScore + ddScore + sampleScore + costScore + stabScore;
        if (closedTrades < Math.Max(5, minimumClosedTrades / 3))
        {
            total = Math.Min(total, 40m);
            notes.Add("Score capped due to very small sample.");
        }

        return new ValidationTrainingScoreBreakdown
        {
            ExpectancyQuality = Math.Round(expectancyScore, 2),
            ProfitFactorQuality = Math.Round(pfScore, 2),
            DrawdownQuality = Math.Round(ddScore, 2),
            SampleSufficiency = Math.Round(sampleScore, 2),
            CostEfficiency = Math.Round(costScore, 2),
            OpportunityStability = Math.Round(stabScore, 2),
            Total = Math.Round(total, 2),
            Notes = notes
        };
    }
}