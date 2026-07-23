using MomoQuant.Domain.Enums;

namespace MomoQuant.Application.ValidationLab;

public interface IValidationMetricConsistencyService
{
    MetricConsistencyReport Validate(
        LayerSegmentMetrics metrics,
        IReadOnlyList<TradeLevelMetrics>? trades = null,
        ExpectancyMetricType? qualificationExpectancyMetric = null,
        decimal? displayedExpectancy = null);
}

public sealed class ValidationMetricConsistencyService : IValidationMetricConsistencyService
{
    private const decimal PnlTolerance = 0.02m;
    private const decimal RatioTolerance = 0.0001m;

    public MetricConsistencyReport Validate(
        LayerSegmentMetrics metrics,
        IReadOnlyList<TradeLevelMetrics>? trades = null,
        ExpectancyMetricType? qualificationExpectancyMetric = null,
        decimal? displayedExpectancy = null)
    {
        var diagnostics = new List<MetricConsistencyDiagnostic>();
        var material = false;

        if (metrics.GrossPnl.HasValue && metrics.NetPnl.HasValue)
        {
            var costs = metrics.TransactionCosts ?? 0m;
            var expectedNet = metrics.GrossPnl.Value - costs;
            if (Math.Abs(expectedNet - metrics.NetPnl.Value) > PnlTolerance)
            {
                material = true;
                diagnostics.Add(Diag("NetPnlCostMismatch",
                    $"GrossPnl ({metrics.GrossPnl}) - TransactionCosts ({costs}) != NetPnl ({metrics.NetPnl})."));
            }
        }

        var outcomeSum = metrics.WinnerCount + metrics.LoserCount;
        // Breakeven/expired may be in closed count; only flag when winners+losers exceed closed.
        if (outcomeSum > metrics.ClosedTradeCount)
        {
            material = true;
            diagnostics.Add(Diag("ClosedTradeCountMismatch",
                $"Winner+Loser ({outcomeSum}) exceeds ClosedTradeCount ({metrics.ClosedTradeCount})."));
        }

        if (trades is { Count: > 0 })
        {
            var grossRs = trades.Where(t => t.GrossR.HasValue).Select(t => t.GrossR!.Value).ToList();
            if (grossRs.Count > 0 && metrics.GrossExpectancyR.HasValue)
            {
                var avg = grossRs.Average();
                if (Math.Abs(avg - metrics.GrossExpectancyR.Value) > RatioTolerance)
                {
                    material = true;
                    diagnostics.Add(Diag("ExpectancyMismatch",
                        $"GrossExpectancyR {metrics.GrossExpectancyR} != avg GrossR {avg}."));
                }
            }

            var netRs = trades.Where(t => t.NetR.HasValue).Select(t => t.NetR!.Value).ToList();
            if (netRs.Count > 0 && metrics.NetExpectancyR.HasValue)
            {
                var avg = netRs.Average();
                if (Math.Abs(avg - metrics.NetExpectancyR.Value) > RatioTolerance)
                {
                    material = true;
                    diagnostics.Add(Diag("ExpectancyMismatch",
                        $"NetExpectancyR {metrics.NetExpectancyR} != avg NetR {avg}."));
                }
            }

            var gGain = trades.Where(t => t.GrossPnl > 0).Sum(t => t.GrossPnl);
            var gLoss = trades.Where(t => t.GrossPnl < 0).Sum(t => Math.Abs(t.GrossPnl));
            var expectedGpf = ValidationMetricsContract.ComputeProfitFactor(gGain, gLoss);
            if (expectedGpf.Status == ProfitFactorStatus.Finite
                && metrics.GrossProfitFactor.HasValue
                && Math.Abs(expectedGpf.NumericValue!.Value - metrics.GrossProfitFactor.Value) > RatioTolerance)
            {
                material = true;
                diagnostics.Add(Diag("ProfitFactorMismatch",
                    $"GrossProfitFactor {metrics.GrossProfitFactor} != recomputed {expectedGpf.NumericValue}."));
            }

            var nGain = trades.Where(t => t.NetPnl > 0).Sum(t => t.NetPnl);
            var nLoss = trades.Where(t => t.NetPnl < 0).Sum(t => Math.Abs(t.NetPnl));
            var expectedNpf = ValidationMetricsContract.ComputeProfitFactor(nGain, nLoss);
            if (expectedNpf.Status == ProfitFactorStatus.Finite
                && metrics.NetProfitFactor.HasValue
                && Math.Abs(expectedNpf.NumericValue!.Value - metrics.NetProfitFactor.Value) > RatioTolerance)
            {
                material = true;
                diagnostics.Add(Diag("ProfitFactorMismatch",
                    $"NetProfitFactor {metrics.NetProfitFactor} != recomputed {expectedNpf.NumericValue}."));
            }

            if (trades.Count != metrics.ClosedTradeCount)
            {
                diagnostics.Add(Diag("ClosedTradeCountMismatch",
                    $"Trade-level count {trades.Count} != ClosedTradeCount {metrics.ClosedTradeCount}."));
                material = true;
            }
        }

        if (qualificationExpectancyMetric.HasValue && displayedExpectancy.HasValue)
        {
            var qualValue = qualificationExpectancyMetric == ExpectancyMetricType.GrossExpectancyR
                ? metrics.GrossExpectancyR
                : metrics.NetExpectancyR;
            if (qualValue.HasValue && Math.Abs(qualValue.Value - displayedExpectancy.Value) > RatioTolerance)
            {
                material = true;
                diagnostics.Add(Diag("QualificationMetricDisplayMismatch",
                    $"Displayed expectancy {displayedExpectancy} != qualification {qualificationExpectancyMetric} value {qualValue}."));
            }
        }

        // Ambiguous legacy: NetExpectancyR equals AverageR while GrossExpectancyR missing → flag.
        if (metrics.NetExpectancyR.HasValue
            && metrics.AverageR.HasValue
            && metrics.GrossExpectancyR is null
            && metrics.NetExpectancyR == metrics.AverageR
            && metrics.TransactionCosts is > 0m)
        {
            material = true;
            diagnostics.Add(Diag("ValidationMetricMismatch",
                "NetExpectancyR appears aliased to AverageR (gross) under non-zero costs — ValidationMetrics/v1 legacy."));
        }

        return new MetricConsistencyReport
        {
            IsConsistent = !material,
            BlocksPassedVerdict = material,
            Diagnostics = diagnostics
        };
    }

    private static MetricConsistencyDiagnostic Diag(string key, string message) =>
        new() { Key = key, Message = message };
}

public sealed class MetricConsistencyReport
{
    public bool IsConsistent { get; init; }
    public bool BlocksPassedVerdict { get; init; }
    public IReadOnlyList<MetricConsistencyDiagnostic> Diagnostics { get; init; } = [];
}

public sealed class MetricConsistencyDiagnostic
{
    public string Key { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}
