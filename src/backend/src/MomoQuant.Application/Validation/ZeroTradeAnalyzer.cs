using MomoQuant.Application.Strategies.VolatilityGatedSuperTrend;
using MomoQuant.Application.Strategies.VolatilityGatedSuperTrend.Dtos;
using MomoQuant.Application.Validation.Dtos;
using MomoQuant.Domain.Constants;

namespace MomoQuant.Application.Validation;

public static class ZeroTradeAnalyzer
{
    public const string ReasonNoCandlesLoaded = "NoCandlesLoaded";
    public const string ReasonNotEnoughAfterWarmup = "NotEnoughCandlesAfterWarmup";
    public const string ReasonEngineEvaluationBug = "EngineEvaluationBug";
    public const string ReasonVolatilityGateFailed = "VolatilityGateFailed";
    public const string ReasonMomentumFailed = "MomentumFailed";
    public const string ReasonNoRetest = "NoRetest";
    public const string ReasonNoConfirmation = "NoConfirmation";
    public const string ReasonCandidatesRejected = "CandidatesRejected";

    public static ZeroTradeAnalysisDto? Analyze(
        string strategyCode,
        StrategyFunnelDiagnosticsDto? funnel,
        int tradeCount,
        int evaluationCandleCount,
        int warmupCandles,
        int evaluationCount,
        int riskRejectedCount,
        bool engineEvaluationBug = false)
    {
        if (tradeCount > 0)
        {
            return null;
        }

        if (evaluationCandleCount == 0)
        {
            return Blocker(
                "No candles loaded for evaluation window",
                "Check date range and import candles from Market Watch.",
                null,
                "No candles were loaded for this evaluation window.",
                ReasonNoCandlesLoaded);
        }

        if (evaluationCandleCount <= warmupCandles)
        {
            return Blocker(
                "Not enough candles after warmup",
                "Extend the date range or reduce warmup requirements.",
                null,
                "The evaluation window does not contain enough candles after warmup.",
                ReasonNotEnoughAfterWarmup);
        }

        if (engineEvaluationBug || (evaluationCount == 0 && evaluationCandleCount > warmupCandles))
        {
            return Blocker(
                "Engine issue: candles loaded but not evaluated",
                "Candles are available, but the strategy engine did not evaluate them. Check strategy execution pipeline.",
                null,
                "Candles were loaded but the strategy did not evaluate them. This is an engine/diagnostics issue, not a market no-trade result.",
                ReasonEngineEvaluationBug);
        }

        if (string.Equals(strategyCode, StrategyCodes.VolatilityGatedSupertrendMomentum, StringComparison.OrdinalIgnoreCase)
            && funnel is not null)
        {
            var vgFunnel = new VolatilityGatedSuperTrendFunnelCounts
            {
                Evaluations = funnel.Evaluations,
                VolatilityGatePassed = funnel.VolatilityGatePassedCount,
                MomentumPassed = funnel.MomentumPassedCount,
                RetestCount = funnel.RetestDetectedCount,
                ConfirmationCount = funnel.ConfirmationDetectedCount,
                CandidateSignals = funnel.CandidateSignals,
                TradesCreated = funnel.TradesCreated
            };
            foreach (var (key, value) in funnel.RejectionReasonBreakdown)
            {
                vgFunnel.RejectionReasonBreakdown[key] = value;
            }

            return VgWhyZeroTradesAnalyzer.Analyze(
                vgFunnel,
                riskRejectedCount,
                evaluationCandleCount,
                warmupCandles,
                Math.Max(evaluationCount, funnel.Evaluations));
        }

        if (funnel is null || funnel.Evaluations == 0)
        {
            return Blocker(
                "No strategy evaluations recorded",
                "Check candle coverage, indicators, and date range.",
                null,
                "No strategy evaluations were recorded for this window.",
                ReasonEngineEvaluationBug);
        }

        if (funnel.VolatilityGatePassedCount == 0)
        {
            return Blocker(
                "Volatility gate blocked all candles",
                "Try Exploratory profile or lower minVolatilityRatio for research.",
                "minVolatilityRatio",
                "Volatility gate blocked all candles.",
                ReasonVolatilityGateFailed);
        }

        if (funnel.MomentumPassedCount == 0)
        {
            return Blocker(
                "Momentum confirmation failed",
                "Lower minHistogramStrength or review MACD settings.",
                "minHistogramStrength",
                "Momentum histogram did not confirm direction.",
                ReasonMomentumFailed);
        }

        if (funnel.CandidateSignals > 0)
        {
            return Blocker(
                "Candidates rejected before trade creation",
                "Review risk profile, confidence thresholds, and execution settings.",
                "MinStrength",
                "Candidates were found, but risk/execution filters rejected them.",
                ReasonCandidatesRejected);
        }

        return Blocker(
            "No qualifying strategy signals",
            "Review strategy parameters or extend the date range.",
            null,
            "The strategy pipeline ran but produced no entry candidates.",
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
