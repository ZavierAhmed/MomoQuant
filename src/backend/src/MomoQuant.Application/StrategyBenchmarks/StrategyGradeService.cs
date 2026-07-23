using MomoQuant.Application.StrategyBenchmarks.Dtos;

namespace MomoQuant.Application.StrategyBenchmarks;

public interface IStrategyGradeService
{
    StrategyBenchmarkGradeDto Grade(StrategyBenchmarkMetrics metrics, IReadOnlyList<StrategyBenchmarkMetrics>? siblings = null);
}

public sealed class StrategyBenchmarkMetrics
{
    public decimal NetPnlPercent { get; init; }
    public decimal MaxDrawdownPercent { get; init; }
    public decimal ProfitFactor { get; init; }
    public decimal WinRatePercent { get; init; }
    public int TotalTrades { get; init; }
    public int TotalSignals { get; init; }
    public int ApprovedSignals { get; init; }
    public int RejectedSignals { get; init; }
    public int MissedOrders { get; init; }
}

public sealed class StrategyGradeService : IStrategyGradeService
{
    public StrategyBenchmarkGradeDto Grade(
        StrategyBenchmarkMetrics metrics,
        IReadOnlyList<StrategyBenchmarkMetrics>? siblings = null)
    {
        var warnings = new List<string>();
        var strengths = new List<string>();
        var weaknesses = new List<string>();

        if (metrics.TotalTrades == 0)
        {
            return new StrategyBenchmarkGradeDto
            {
                Grade = "N/A",
                Score = 0m,
                Label = "No valid trades",
                Strengths = [],
                Weaknesses = ["No valid trades were produced."],
                Warnings = ["No usable sample."]
            };
        }

        if (metrics.TotalTrades < 5)
        {
            warnings.Add("Sample size too small.");
            if (metrics.NetPnlPercent > 0)
            {
                warnings.Add("Profitable but statistically weak sample.");
            }
        }

        if (metrics.NetPnlPercent > 0m)
        {
            strengths.Add("Positive net PnL.");
        }
        else
        {
            weaknesses.Add("Net PnL is not positive.");
        }

        if (metrics.ProfitFactor >= 1.5m)
        {
            strengths.Add("Profit factor is strong.");
        }
        else if (metrics.ProfitFactor < 1.0m)
        {
            weaknesses.Add("Profit factor is below break-even.");
        }

        if (metrics.MaxDrawdownPercent <= 5m)
        {
            strengths.Add("Drawdown stayed controlled.");
        }
        else if (metrics.MaxDrawdownPercent > 10m)
        {
            weaknesses.Add("Drawdown is high.");
        }

        var rejectedAndMissed = metrics.RejectedSignals + metrics.MissedOrders;
        if (metrics.TotalSignals > 0 && rejectedAndMissed > metrics.TotalSignals * 0.5m)
        {
            warnings.Add("Most signals were rejected by risk or missed execution.");
            weaknesses.Add("Risk or execution friction is high.");
        }

        var consistencyScore = ScoreConsistency(metrics, siblings);
        if (consistencyScore >= 80m)
        {
            strengths.Add("Performance is reasonably consistent across tested markets.");
        }
        else if (siblings is { Count: > 1 })
        {
            weaknesses.Add("Performance is inconsistent across symbols/timeframes.");
        }

        var score =
            ScoreNetPnl(metrics.NetPnlPercent) * 0.25m +
            ScoreDrawdown(metrics.MaxDrawdownPercent) * 0.20m +
            ScoreProfitFactor(metrics.ProfitFactor) * 0.20m +
            ScoreWinRate(metrics.WinRatePercent) * 0.10m +
            ScoreTradeCount(metrics.TotalTrades, metrics.NetPnlPercent) * 0.10m +
            consistencyScore * 0.10m +
            ScoreRiskPenalty(metrics) * 0.05m;

        score = Math.Round(Math.Clamp(score, 0m, 100m), 2);

        return new StrategyBenchmarkGradeDto
        {
            Grade = MapGrade(score),
            Score = score,
            Label = null,
            Strengths = strengths.Distinct().ToList(),
            Weaknesses = weaknesses.Distinct().ToList(),
            Warnings = warnings.Distinct().ToList()
        };
    }

    private static decimal ScoreNetPnl(decimal netPnlPercent)
    {
        if (netPnlPercent <= 0m) return 0m;
        if (netPnlPercent <= 5m) return netPnlPercent / 5m * 70m;
        if (netPnlPercent <= 15m) return 70m + (netPnlPercent - 5m) / 10m * 30m;
        return 100m;
    }

    private static decimal ScoreDrawdown(decimal maxDrawdownPercent)
    {
        var value = Math.Abs(maxDrawdownPercent);
        if (value <= 2m) return 100m;
        if (value <= 5m) return 80m;
        if (value <= 10m) return 60m;
        if (value <= 20m) return 30m;
        return 0m;
    }

    private static decimal ScoreProfitFactor(decimal profitFactor)
    {
        if (profitFactor < 1.0m) return 0m;
        if (profitFactor < 1.2m) return 40m;
        if (profitFactor < 1.5m) return 70m;
        if (profitFactor <= 2.0m) return 90m;
        return 100m;
    }

    private static decimal ScoreWinRate(decimal winRatePercent)
    {
        if (winRatePercent < 35m) return 20m;
        if (winRatePercent < 45m) return 50m;
        if (winRatePercent < 55m) return 70m;
        if (winRatePercent < 65m) return 90m;
        return 100m;
    }

    private static decimal ScoreTradeCount(int totalTrades, decimal netPnlPercent)
    {
        if (totalTrades == 0) return 0m;
        if (totalTrades <= 5) return 20m;
        if (totalTrades <= 15) return 60m;
        if (totalTrades <= 100) return 100m;
        return netPnlPercent > 10m ? 100m : 80m;
    }

    private static decimal ScoreConsistency(StrategyBenchmarkMetrics metrics, IReadOnlyList<StrategyBenchmarkMetrics>? siblings)
    {
        if (siblings is null || siblings.Count <= 1)
        {
            return metrics.NetPnlPercent > 0m ? 75m : 40m;
        }

        var active = siblings.Where(item => item.TotalTrades > 0).ToList();
        if (active.Count == 0)
        {
            return 0m;
        }

        var positiveRatio = active.Count(item => item.NetPnlPercent > 0m) * 100m / active.Count;
        var deepLossRatio = active.Count(item => item.NetPnlPercent < -5m) * 100m / active.Count;
        return Math.Clamp(positiveRatio - deepLossRatio * 0.5m, 0m, 100m);
    }

    private static decimal ScoreRiskPenalty(StrategyBenchmarkMetrics metrics)
    {
        if (metrics.TotalSignals <= 0)
        {
            return metrics.TotalTrades > 0 ? 80m : 0m;
        }

        var penaltyRatio = (metrics.RejectedSignals + metrics.MissedOrders) / (decimal)Math.Max(metrics.TotalSignals, 1);
        return Math.Clamp(100m - penaltyRatio * 100m, 0m, 100m);
    }

    private static string MapGrade(decimal score) =>
        score switch
        {
            >= 90m => "A+",
            >= 85m => "A",
            >= 80m => "B+",
            >= 70m => "B",
            >= 60m => "C",
            >= 50m => "D",
            _ => "F"
        };
}
