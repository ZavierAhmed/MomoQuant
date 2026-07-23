using MomoQuant.Application.Strategies.VolatilityGatedSuperTrend.Dtos;

namespace MomoQuant.Application.Strategies.VolatilityGatedSuperTrend;

public interface IVolatilityGatedSuperTrendFunnelTracker
{
    void Reset();
    void RecordEvaluation(VolatilityGatedSuperTrendDiagnosticsDto diagnostics);
    void RecordTradeCreated();
    VolatilityGatedSuperTrendFunnelCounts GetSnapshot();
}

public sealed class VolatilityGatedSuperTrendFunnelTracker : IVolatilityGatedSuperTrendFunnelTracker
{
    private readonly object _sync = new();
    private readonly VolatilityGatedSuperTrendFunnelCounts _counts = new();

    public void Reset()
    {
        lock (_sync)
        {
            _counts.Evaluations = 0;
            _counts.SuperTrendBullishCount = 0;
            _counts.SuperTrendBearishCount = 0;
            _counts.VolatilityGatePassed = 0;
            _counts.VolatilityGateFailed = 0;
            _counts.MomentumPassed = 0;
            _counts.MomentumFailed = 0;
            _counts.RetestCount = 0;
            _counts.RetestMissing = 0;
            _counts.ConfirmationCount = 0;
            _counts.ConfirmationMissing = 0;
            _counts.CandidateSignals = 0;
            _counts.TradesCreated = 0;
            _counts.RejectionReasonBreakdown.Clear();
        }
    }

    public void RecordEvaluation(VolatilityGatedSuperTrendDiagnosticsDto diagnostics)
    {
        lock (_sync)
        {
            _counts.Evaluations++;
            if (string.Equals(diagnostics.TrendDirection, "Bullish", StringComparison.OrdinalIgnoreCase))
            {
                _counts.SuperTrendBullishCount++;
            }
            else if (string.Equals(diagnostics.TrendDirection, "Bearish", StringComparison.OrdinalIgnoreCase))
            {
                _counts.SuperTrendBearishCount++;
            }

            if (diagnostics.VolatilityGatePassed) _counts.VolatilityGatePassed++;
            else _counts.VolatilityGateFailed++;

            if (diagnostics.MomentumPassed) _counts.MomentumPassed++;
            else if (diagnostics.VolatilityGatePassed) _counts.MomentumFailed++;

            if (diagnostics.RetestDetected) _counts.RetestCount++;
            else if (diagnostics.MomentumPassed && diagnostics.FinalDecision != "Entry") _counts.RetestMissing++;

            if (diagnostics.ConfirmationDetected) _counts.ConfirmationCount++;
            else if (diagnostics.RetestDetected && diagnostics.FinalDecision != "Entry") _counts.ConfirmationMissing++;

            if (diagnostics.FinalDecision == "Entry") _counts.CandidateSignals++;

            if (!string.IsNullOrWhiteSpace(diagnostics.RejectionReason))
            {
                var key = diagnostics.RejectionReason;
                _counts.RejectionReasonBreakdown.TryGetValue(key, out var current);
                _counts.RejectionReasonBreakdown[key] = current + 1;
            }
        }
    }

    public void RecordTradeCreated()
    {
        lock (_sync)
        {
            _counts.TradesCreated++;
        }
    }

    public VolatilityGatedSuperTrendFunnelCounts GetSnapshot()
    {
        lock (_sync)
        {
            var snapshot = new VolatilityGatedSuperTrendFunnelCounts
            {
                Evaluations = _counts.Evaluations,
                SuperTrendBullishCount = _counts.SuperTrendBullishCount,
                SuperTrendBearishCount = _counts.SuperTrendBearishCount,
                VolatilityGatePassed = _counts.VolatilityGatePassed,
                VolatilityGateFailed = _counts.VolatilityGateFailed,
                MomentumPassed = _counts.MomentumPassed,
                MomentumFailed = _counts.MomentumFailed,
                RetestCount = _counts.RetestCount,
                RetestMissing = _counts.RetestMissing,
                ConfirmationCount = _counts.ConfirmationCount,
                ConfirmationMissing = _counts.ConfirmationMissing,
                CandidateSignals = _counts.CandidateSignals,
                TradesCreated = _counts.TradesCreated
            };
            foreach (var (key, value) in _counts.RejectionReasonBreakdown)
            {
                snapshot.RejectionReasonBreakdown[key] = value;
            }

            return snapshot;
        }
    }
}
