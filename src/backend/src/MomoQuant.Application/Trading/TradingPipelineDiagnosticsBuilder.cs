using System.Text.Json;
using MomoQuant.Application.Backtesting;
using MomoQuant.Application.Common;
using MomoQuant.Application.Strategies.BbLiquiditySweep;
using MomoQuant.Application.Strategies.BbLiquiditySweep.Dtos;
using MomoQuant.Application.Trading.Dtos;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Risk;

namespace MomoQuant.Application.Trading;

public static class TradingPipelineDiagnosticsBuilder
{
    public static object BuildSnapshot(
        BacktestContext context,
        int candleCount,
        int indicatorSnapshotCount,
        decimal effectiveMinConfidenceScore,
        bool aiEnabled) =>
        new
        {
            candleCount,
            indicatorSnapshotCount,
            strategyEvaluations = context.StrategiesEvaluated,
            noTradeSignals = context.NoTradeSignals,
            entrySignals = context.EntrySignals,
            candidateSignals = context.CandidateTrades.Count,
            warningSignals = context.WarningSignals,
            invalidSignals = context.InvalidSignals,
            confidenceEvaluations = context.ConfidenceEvaluations,
            confidenceApproved = context.ConfidenceApproved,
            confidenceRejected = context.ConfidenceRejected,
            riskEvaluations = context.RiskEvaluations,
            riskApproved = context.RiskApproved,
            riskRejected = context.RiskRejected,
            ordersCreated = context.Orders.Count,
            ordersFilled = context.OrderFills.Count,
            ordersMissed = context.MissedOrderLinks.Count,
            tradesOpened = context.Trades.Count,
            tradesClosed = context.Trades.Count(trade => trade.Status == TradeStatus.Closed),
            aiEnabled,
            aiDecisionsCreated = context.AiDecisions.Count,
            effectiveMinConfidenceScore,
            averageNormalizedConfidenceScore = ResolveAverageConfidence(context),
            lowestConfidenceScore = ResolveLowestConfidence(context),
            highestConfidenceScore = ResolveHighestConfidence(context),
            topRiskRejectionRules = BuildTopRejectionRules(context.RiskDecisions),
            topNoTradeReasons = BuildTopNoTradeReasons(context.NoTradeReasonEvents),
            topStrategySignalReasons = BuildTopSignalReasons(context.Signals),
            candidateTrades = context.CandidateTrades,
            shadowTrades = context.ShadowTrades,
            rejectionQuality = BuildRejectionQuality(context),
            warnings = BuildWarnings(context, candleCount, indicatorSnapshotCount, effectiveMinConfidenceScore, aiEnabled),
            bbLiquiditySweep = BuildBbLiquiditySweepDiagnostics(context)
        };

    public static string SerializeSnapshot(
        BacktestContext context,
        int candleCount,
        int indicatorSnapshotCount,
        decimal effectiveMinConfidenceScore,
        bool aiEnabled) =>
        JsonSerializer.Serialize(BuildSnapshot(context, candleCount, indicatorSnapshotCount, effectiveMinConfidenceScore, aiEnabled));

    public static PipelineDiagnosticsDto ToDto(
        BacktestContext? context,
        PipelineDiagnosticsCounters? persistedCounters,
        int candleCount,
        int indicatorSnapshotCount,
        decimal effectiveMinConfidenceScore,
        bool aiEnabled,
        IReadOnlyList<RiskDecision> persistedRiskDecisions,
        IReadOnlyList<Domain.Signals.StrategySignal> persistedSignals,
        int ordersCreated,
        int ordersFilled,
        int ordersMissed,
        int tradesOpened,
        int tradesClosed,
        int aiDecisionsCreated)
    {
        var riskDecisions = context?.RiskDecisions.Count > 0 ? context.RiskDecisions : persistedRiskDecisions;
        var signals = context?.Signals.Count > 0 ? context.Signals : persistedSignals;

        var strategyEvaluations = context?.StrategiesEvaluated ?? persistedCounters?.StrategiesEvaluated ?? 0;
        var noTradeSignals = context?.NoTradeSignals ?? persistedCounters?.NoTradeSignals ?? 0;
        var entrySignals = context?.EntrySignals ?? persistedCounters?.EntrySignals ?? signals.Count(signal => signal.SignalType == SignalType.Entry);
        var candidateSignals = context?.CandidateTrades.Count ?? persistedCounters?.CandidateSignals ?? entrySignals;
        var warningSignals = context?.WarningSignals ?? persistedCounters?.WarningSignals ?? 0;
        var invalidSignals = context?.InvalidSignals ?? persistedCounters?.InvalidSignals ?? 0;
        var confidenceEvaluations = context?.ConfidenceEvaluations ?? persistedCounters?.ConfidenceEvaluations ?? entrySignals;
        var confidenceApproved = context?.ConfidenceApproved ?? persistedCounters?.ConfidenceApproved ?? 0;
        var confidenceRejected = context?.ConfidenceRejected ?? persistedCounters?.ConfidenceRejected ?? 0;
        var riskEvaluations = context?.RiskEvaluations ?? persistedCounters?.RiskEvaluations ?? riskDecisions.Count;
        var riskApproved = context?.RiskApproved ?? persistedCounters?.RiskApproved ?? riskDecisions.Count(decision => decision.Decision == RiskDecisionType.Approved);
        var riskRejected = context?.RiskRejected ?? persistedCounters?.RiskRejected ?? riskDecisions.Count(decision => decision.Decision == RiskDecisionType.Rejected);

        var confidenceScores = signals
            .Where(signal => signal.SignalType == SignalType.Entry)
            .Select(signal => ConfidenceScoreNormalizer.Normalize(signal.Strength))
            .ToList();

        return new PipelineDiagnosticsDto
        {
            CandleCount = candleCount,
            IndicatorSnapshotCount = indicatorSnapshotCount,
            StrategyEvaluations = strategyEvaluations,
            NoTradeSignals = noTradeSignals,
            EntrySignals = entrySignals,
            CandidateSignals = candidateSignals,
            WarningSignals = warningSignals,
            InvalidSignals = invalidSignals,
            ConfidenceEvaluations = confidenceEvaluations,
            ConfidenceApproved = confidenceApproved,
            ConfidenceRejected = confidenceRejected,
            RiskEvaluations = riskEvaluations,
            RiskApproved = riskApproved,
            RiskRejected = riskRejected,
            OrdersCreated = context?.Orders.Count ?? ordersCreated,
            OrdersFilled = context?.OrderFills.Count ?? ordersFilled,
            OrdersMissed = context?.MissedOrderLinks.Count ?? ordersMissed,
            TradesOpened = context?.Trades.Count ?? tradesOpened,
            TradesClosed = context?.Trades.Count(trade => trade.Status == TradeStatus.Closed) ?? tradesClosed,
            AiEnabled = aiEnabled,
            AiDecisionsCreated = context?.AiDecisions.Count ?? aiDecisionsCreated,
            EffectiveMinConfidenceScore = effectiveMinConfidenceScore,
            AverageNormalizedConfidenceScore = confidenceScores.Count > 0 ? confidenceScores.Average() : null,
            LowestConfidenceScore = confidenceScores.Count > 0 ? confidenceScores.Min() : null,
            HighestConfidenceScore = confidenceScores.Count > 0 ? confidenceScores.Max() : null,
            TopRiskRejectionRules = BuildTopRejectionRules(riskDecisions)
                .Select(item => new PipelineRuleCountDto { RuleKey = item.RuleKey, Count = item.Count })
                .ToList(),
            TopNoTradeReasons = BuildTopNoTradeReasons(context?.NoTradeReasonEvents ?? [])
                .Select(item => new PipelineReasonCountDto { StrategyCode = item.StrategyCode, Reason = item.Reason, Count = item.Count })
                .ToList(),
            TopStrategySignalReasons = BuildTopSignalReasons(signals)
                .Select(item => new PipelineReasonCountDto { Reason = item.Reason, Count = item.Count })
                .ToList(),
            CandidateTrades = context?.CandidateTrades ?? [],
            ShadowTrades = context?.ShadowTrades ?? [],
            RejectionQuality = context is null ? new RejectionQualityDto() : BuildRejectionQuality(context),
            Warnings = BuildWarnings(context, candleCount, indicatorSnapshotCount, effectiveMinConfidenceScore, aiEnabled, entrySignals, riskApproved, riskRejected, ordersCreated, ordersFilled),
            BbLiquiditySweep = context?.BbLiquiditySweepFunnel is null ? null : MapBbDiagnostics(context)
        };
    }

    private static BbLiquiditySweepPipelineDiagnosticsDto MapBbDiagnostics(BacktestContext context)
    {
        var funnel = context.BbLiquiditySweepFunnel!;
        var useRsi = funnel.RsiPrimedEvaluations > 0;
        return new BbLiquiditySweepPipelineDiagnosticsDto
        {
            FunnelCounts = funnel,
            NoTradeReasonBreakdown = funnel.NoTradeReasonBreakdown,
            PipelineSummary = funnel.BuildPipelineSummary(),
            WhyZeroTradesAnalysis = funnel.FinalCandidateSignals == 0
                ? BbWhyZeroTradesAnalyzer.Analyze(funnel, useRsi)
                : null,
            TopNoTradeReason = funnel.TopNoTradeReason,
            TopNoTradeReasonCount = funnel.TopNoTradeReasonCount,
            SampleRejectedEvaluations = context.BbLiquiditySweepSampleRejections.Take(50).ToList()
        };
    }

    private static object? BuildBbLiquiditySweepDiagnostics(BacktestContext context)
    {
        if (context.BbLiquiditySweepFunnel is null)
        {
            return null;
        }

        return MapBbDiagnostics(context);
    }

    private static RejectionQualityDto BuildRejectionQuality(BacktestContext context)
    {
        var rejected = context.CandidateTrades
            .Select((candidate, index) => new
            {
                candidate,
                shadow = context.ShadowTrades.FirstOrDefault(item => item.CandidateTradeIndex == index)
            })
            .Where(item => item.candidate.FinalDecision is CandidateTradeFinalDecision.RejectedByConfidence or CandidateTradeFinalDecision.RejectedByRisk or CandidateTradeFinalDecision.RejectedByBoth or CandidateTradeFinalDecision.RejectedInvalidTrade)
            .ToList();

        var rejectedWouldHaveWon = rejected.Count(item => item.shadow?.OutcomeClassification == ShadowOutcomeClassification.WouldHaveWon);
        var rejectedWouldHaveLost = rejected.Count(item => item.shadow?.OutcomeClassification == ShadowOutcomeClassification.WouldHaveLost);
        var rejectedBreakEven = rejected.Count(item => item.shadow?.OutcomeClassification == ShadowOutcomeClassification.BreakEven);
        var rejectedNotEnoughData = rejected.Count(item => item.shadow is null || item.shadow.OutcomeClassification == ShadowOutcomeClassification.NotEnoughFutureData);

        var confidenceRejected = rejected.Where(item =>
                item.candidate.FinalDecision is CandidateTradeFinalDecision.RejectedByConfidence or CandidateTradeFinalDecision.RejectedByBoth)
            .ToList();
        var riskRejected = rejected.Where(item =>
                item.candidate.FinalDecision is CandidateTradeFinalDecision.RejectedByRisk or CandidateTradeFinalDecision.RejectedByBoth)
            .ToList();

        return new RejectionQualityDto
        {
            RejectedCandidateCount = rejected.Count,
            RejectedByConfidenceCount = confidenceRejected.Count,
            RejectedByRiskCount = riskRejected.Count,
            RejectedByBothCount = rejected.Count(item => item.candidate.FinalDecision == CandidateTradeFinalDecision.RejectedByBoth),
            ShadowTradesSimulated = rejected.Count(item => item.shadow is not null),
            RejectedWouldHaveWon = rejectedWouldHaveWon,
            RejectedWouldHaveLost = rejectedWouldHaveLost,
            RejectedBreakEven = rejectedBreakEven,
            RejectedNotEnoughData = rejectedNotEnoughData,
            ShadowNetPnl = rejected.Sum(item => item.shadow?.ShadowNetPnl ?? 0m),
            ConfidenceFalseRejectCount = confidenceRejected.Count(item => item.shadow?.ShadowNetPnl > 0m),
            RiskFalseRejectCount = riskRejected.Count(item => item.shadow?.ShadowNetPnl > 0m),
            ConfidenceCorrectRejectCount = confidenceRejected.Count(item => item.shadow?.ShadowNetPnl <= 0m),
            RiskCorrectRejectCount = riskRejected.Count(item => item.shadow?.ShadowNetPnl <= 0m)
        };
    }

    private static decimal? ResolveAverageConfidence(BacktestContext context)
    {
        var scores = context.Signals
            .Where(signal => signal.SignalType == SignalType.Entry)
            .Select(signal => ConfidenceScoreNormalizer.Normalize(signal.Strength))
            .ToList();
        return scores.Count > 0 ? scores.Average() : null;
    }

    private static decimal? ResolveLowestConfidence(BacktestContext context)
    {
        var scores = context.Signals
            .Where(signal => signal.SignalType == SignalType.Entry)
            .Select(signal => ConfidenceScoreNormalizer.Normalize(signal.Strength))
            .ToList();
        return scores.Count > 0 ? scores.Min() : null;
    }

    private static decimal? ResolveHighestConfidence(BacktestContext context)
    {
        var scores = context.Signals
            .Where(signal => signal.SignalType == SignalType.Entry)
            .Select(signal => ConfidenceScoreNormalizer.Normalize(signal.Strength))
            .ToList();
        return scores.Count > 0 ? scores.Max() : null;
    }

    private static IReadOnlyList<(string RuleKey, int Count)> BuildTopRejectionRules(IEnumerable<RiskDecision> decisions) =>
        decisions
            .Where(decision => decision.Decision == RiskDecisionType.Rejected && !string.IsNullOrWhiteSpace(decision.RejectedRuleKey))
            .GroupBy(decision => decision.RejectedRuleKey!)
            .Select(group => (RuleKey: group.Key, Count: group.Count()))
            .OrderByDescending(item => item.Count)
            .Take(10)
            .ToList();

    private static IReadOnlyList<(string StrategyCode, string Reason, int Count)> BuildTopNoTradeReasons(
        IEnumerable<(string StrategyCode, string Reason)> events) =>
        events
            .Where(item => !string.IsNullOrWhiteSpace(item.Reason))
            .GroupBy(item => (item.StrategyCode, item.Reason))
            .Select(group => (group.Key.StrategyCode, group.Key.Reason, Count: group.Count()))
            .OrderByDescending(item => item.Count)
            .Take(20)
            .ToList();

    private static IReadOnlyList<(string Reason, int Count)> BuildTopSignalReasons(IEnumerable<Domain.Signals.StrategySignal> signals) =>
        signals
            .Where(signal => signal.SignalType == SignalType.Entry && !string.IsNullOrWhiteSpace(signal.Reason))
            .GroupBy(signal => signal.Reason)
            .Select(group => (Reason: group.Key, Count: group.Count()))
            .OrderByDescending(item => item.Count)
            .Take(10)
            .ToList();

    private static IReadOnlyList<string> BuildWarnings(
        BacktestContext? context,
        int candleCount,
        int indicatorSnapshotCount,
        decimal effectiveMinConfidenceScore,
        bool aiEnabled,
        int entrySignals = 0,
        int riskApproved = 0,
        int riskRejected = 0,
        int ordersCreated = 0,
        int ordersFilled = 0)
    {
        entrySignals = context?.EntrySignals ?? entrySignals;
        riskApproved = context?.RiskApproved ?? riskApproved;
        riskRejected = context?.RiskRejected ?? riskRejected;
        ordersCreated = context?.Orders.Count ?? ordersCreated;
        ordersFilled = context?.OrderFills.Count ?? ordersFilled;

        var warnings = new List<string>();
        if (candleCount == 0)
        {
            warnings.Add("No candles found for selected range.");
        }

        if (indicatorSnapshotCount == 0)
        {
            warnings.Add("No indicator snapshots found for selected range. Recalculate indicators first.");
        }

        if (entrySignals == 0)
        {
            warnings.Add("No entry signals were generated by selected strategies.");
        }

        if (entrySignals > 0 && riskRejected > 0 && riskApproved == 0 &&
            context?.RiskDecisions.Any(decision => decision.RejectedRuleKey == RiskRuleKeys.MinConfidenceScore) == true)
        {
            var average = ResolveAverageConfidence(context!) ?? 0m;
            warnings.Add(
                $"All entry signals were rejected by MinConfidenceScore. Average confidence was {ConfidenceScoreNormalizer.Format(average)} and effective minimum was {ConfidenceScoreNormalizer.Format(effectiveMinConfidenceScore)}.");
        }

        if (!aiEnabled)
        {
            warnings.Add("AI scoring was disabled for this run.");
        }

        if (ordersCreated > 0 && ordersFilled == 0 && context?.Settings.ExecutionMode == ExecutionMode.MakerOnly)
        {
            warnings.Add("MakerOnly execution missed all orders.");
        }

        return warnings;
    }
}
