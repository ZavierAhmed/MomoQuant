using MomoQuant.Application.Strategies.VolatilityGatedSuperTrend.Dtos;
using MomoQuant.Application.Validation;
using MomoQuant.Application.Validation.Dtos;

namespace MomoQuant.Application.Strategies.VolatilityGatedSuperTrend;

public static class VgWhyZeroTradesAnalyzer
{
    public static ZeroTradeAnalysisDto Analyze(
        VolatilityGatedSuperTrendFunnelCounts funnel,
        int riskRejectedCount,
        int evaluationCandleCount,
        int warmupCandles,
        int evaluationCount)
    {
        if (evaluationCandleCount == 0)
        {
            return Blocker(
                "No candles loaded for evaluation window",
                "Check the date range and import candles from Market Watch.",
                null,
                "No candles were loaded for this evaluation window.",
                ZeroTradeAnalyzer.ReasonNoCandlesLoaded);
        }

        if (evaluationCandleCount <= warmupCandles)
        {
            return Blocker(
                "Not enough candles after warmup",
                "Extend the date range to include more evaluation candles.",
                null,
                "The evaluation window does not contain enough candles after warmup.",
                ZeroTradeAnalyzer.ReasonNotEnoughAfterWarmup);
        }

        if (evaluationCount == 0 || funnel.Evaluations == 0)
        {
            return Blocker(
                "Engine issue: candles loaded but not evaluated",
                "Candles are available, but the strategy engine did not evaluate them. Check strategy execution pipeline.",
                null,
                "Candles were loaded but the strategy did not evaluate them. This is an engine/diagnostics issue, not a market no-trade result.",
                ZeroTradeAnalyzer.ReasonEngineEvaluationBug);
        }

        if (funnel.CandidateSignals > 0 && funnel.TradesCreated == 0 && riskRejectedCount > 0)
        {
            return Blocker(
                "Risk or execution filters rejected candidates",
                "Review risk profile rules, minimum confidence, and execution mode.",
                "MinStrength",
                "Candidates were found, but risk/execution filters rejected them.",
                ZeroTradeAnalyzer.ReasonCandidatesRejected);
        }

        if (funnel.CandidateSignals > 0 && funnel.TradesCreated == 0)
        {
            return Blocker(
                "Candidates did not become trades",
                "Inspect confidence gating, execution mode, and order fill simulation.",
                "MinStrength",
                "Candidate signals were generated but no trades were created.",
                ZeroTradeAnalyzer.ReasonCandidatesRejected);
        }

        if (funnel.VolatilityGatePassed == 0)
        {
            return Blocker(
                "Volatility gate blocked all candles",
                "Try Exploratory profile or lower minVolatilityRatio for research.",
                "minVolatilityRatio",
                "Volatility gate blocked all candles. The selected period may be too flat or minVolatilityRatio is too high.",
                ZeroTradeAnalyzer.ReasonVolatilityGateFailed);
        }

        if (funnel.MomentumPassed == 0)
        {
            return Blocker(
                "Momentum confirmation failed",
                "Lower minHistogramStrength or review MACD settings.",
                "minHistogramStrength",
                "Momentum histogram did not confirm direction. MACD/momentum settings may be too strict.",
                ZeroTradeAnalyzer.ReasonMomentumFailed);
        }

        if (funnel.RetestCount == 0)
        {
            return Blocker(
                "No SuperTrend retest detected",
                "Increase retestAtrTolerance or disable requireRetest for research.",
                "retestAtrTolerance",
                "Trend existed, but price did not retest the SuperTrend line within tolerance.",
                ZeroTradeAnalyzer.ReasonNoRetest);
        }

        if (funnel.ConfirmationCount == 0)
        {
            return Blocker(
                "No confirmation candle detected",
                "Review confirmation logic or relax retest/continuation settings.",
                "requireRetest",
                "Retests occurred, but no confirmation candle was detected.",
                ZeroTradeAnalyzer.ReasonNoConfirmation);
        }

        return Blocker(
            "Pipeline produced no qualifying entries",
            "Run Exploratory profile or optimization to find looser parameter sets.",
            "requireRetest",
            "The strategy pipeline ran but no entry candidates met all filters.",
            null);
    }

    private static ZeroTradeAnalysisDto Blocker(
        string blocker,
        string action,
        string? parameter,
        string explanation,
        string? reasonCode) => new()
    {
        MostLikelyBlocker = blocker,
        SuggestedNextAction = action,
        RelatedParameter = parameter,
        Explanation = explanation,
        ReasonCode = reasonCode
    };
}
