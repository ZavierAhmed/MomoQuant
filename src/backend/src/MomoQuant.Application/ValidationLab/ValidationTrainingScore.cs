namespace MomoQuant.Application.ValidationLab;

/// <summary>
/// ValidationTrainingScore/v1 — training-only objective (0–100).
/// A Expectancy 0–30, B PF 0–20, C Drawdown 0–15, D Sample 0–15, E Cost 0–10, F Stability 0–10.
/// </summary>
public static class ValidationTrainingScoreVersions
{
    public const string Current = "ValidationTrainingScore/v1";

    /// <summary>
    /// Milestone 23.0D — null-honest scoring for ValidationMetrics/v1.3.2 trial snapshots.
    /// Missing metrics never coalesce to fabricated values; each unavailable component
    /// contributes 0 with an explanatory note.
    /// </summary>
    public const string V2 = "ValidationTrainingScore/v2";
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

    /// <summary>
    /// ValidationTrainingScore/v2 — same component budget as v1
    /// (A Expectancy 0–30, B PF 0–20, C Drawdown 0–15, D Sample 0–15, E Cost 0–10, F Stability 0–10)
    /// computed strictly from a ValidationMetrics/v1.3.2 metric snapshot without null-coalescing.
    /// </summary>
    public static ValidationTrainingScoreBreakdown CalculateV2(
        LayerSegmentMetrics metrics,
        int minimumClosedTrades = 30)
    {
        var notes = new List<string>();
        var closedTrades = metrics.ClosedOutcomePopulationCount ?? metrics.ClosedTradeCount;
        if (closedTrades < minimumClosedTrades)
        {
            notes.Add($"Insufficient sample for full scoring ({closedTrades} < {minimumClosedTrades}).");
        }

        // A. Expectancy quality (0–30) — only when NetExpectancyR was actually evaluated.
        decimal expectancyScore;
        if (metrics.NetExpectancyR is decimal expectancy)
        {
            expectancyScore = expectancy <= 0m ? 0m : Math.Min(30m, expectancy * 15m);
        }
        else
        {
            expectancyScore = 0m;
            notes.Add("NetExpectancyR not evaluated; expectancy component scored 0.");
        }

        // B. Profit factor quality (0–20). Infinity (no losing PnL) earns the full component.
        decimal pfScore;
        var pf = metrics.NetProfitFactor ?? metrics.ProfitFactor;
        var pfStatus = metrics.NetProfitFactorStatus ?? metrics.ProfitFactorStatus;
        if (pf is decimal pfValue)
        {
            pfScore = pfValue <= 0m ? 0m : pfValue >= 2m ? 20m : Math.Min(20m, (pfValue - 1m) * 20m);
            if (pfScore < 0m) pfScore = 0m;
        }
        else if (pfStatus == ProfitFactorStatus.Infinity)
        {
            pfScore = 20m;
            notes.Add("NetProfitFactor is infinite (no losing PnL); component scored 20.");
        }
        else
        {
            pfScore = 0m;
            notes.Add("NetProfitFactor not evaluated; profit factor component scored 0.");
        }

        // C. Drawdown quality (0–15) — not fabricated when the contract produces no drawdown.
        decimal ddScore;
        if (metrics.MaximumRealizedDrawdownPercent is decimal dd)
        {
            ddScore = dd <= 0m ? 15m : dd >= 25m ? 0m : Math.Round(15m * (1m - dd / 25m), 2);
        }
        else
        {
            ddScore = 0m;
            notes.Add("MaximumRealizedDrawdownPercent not evaluated; drawdown component scored 0.");
        }

        // D. Sample sufficiency (0–15) from the closed-outcome population.
        var sampleScore = closedTrades <= 0
            ? 0m
            : closedTrades >= minimumClosedTrades
                ? 15m
                : Math.Round(15m * closedTrades / minimumClosedTrades, 2);

        // E. Cost efficiency (0–10) from the frozen-fee cost population.
        decimal costScore;
        if (metrics.TransactionCosts is decimal costs && metrics.GrossProfit is decimal grossProfit && grossProfit > 0m)
        {
            var feePct = Math.Round(costs / grossProfit * 100m, 4);
            costScore = feePct <= 0m ? 10m : feePct >= 50m ? 0m : Math.Round(10m * (1m - feePct / 50m), 2);
        }
        else
        {
            costScore = 0m;
            notes.Add("Fee-to-gross-profit ratio not evaluable; cost component scored 0.");
        }

        // F. Opportunity stability (0–10) — deterministic from candle/candidate populations.
        var opportunityRate = metrics.OpportunityRatePer1000Candles;
        var stabScore = opportunityRate <= 0m
            ? 0m
            : opportunityRate >= 5m
                ? 10m
                : Math.Round(Math.Min(10m, opportunityRate * 2m), 2);

        var total = expectancyScore + pfScore + ddScore + sampleScore + costScore + stabScore;
        if (closedTrades < Math.Max(5, minimumClosedTrades / 3))
        {
            total = Math.Min(total, 40m);
            notes.Add("Score capped due to very small sample.");
        }

        return new ValidationTrainingScoreBreakdown
        {
            Version = ValidationTrainingScoreVersions.V2,
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