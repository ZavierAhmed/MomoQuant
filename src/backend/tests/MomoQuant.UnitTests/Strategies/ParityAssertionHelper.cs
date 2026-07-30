using System.Text.Json;
using MomoQuant.Application.Strategies;
using MomoQuant.Application.Strategies.Models;
using MomoQuant.Application.StrategyLab;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.MarketData;
using MomoQuant.Domain.Strategies;
using MomoQuant.Domain.StrategyLab;

namespace MomoQuant.UnitTests.Strategies;

/// <summary>Mandatory cross-path parity evidence assertions (Milestone 23.1B1C).</summary>
internal static class ParityAssertionHelper
{
    public sealed class PositiveParityEvidence
    {
        public required StrategyEvaluationCaptureRecord Capture { get; init; }
        public required MarketRegime ExpectedRegime { get; init; }
        public required long ExpectedExchangeId { get; init; }
        public required long ExpectedSymbolId { get; init; }
        public required string ExpectedSymbol { get; init; }
        public required Timeframe ExpectedTimeframe { get; init; }
        public required Timeframe ExpectedHigherTimeframe { get; init; }
        public required DateTime ExpectedEvaluationTimestamp { get; init; }
        public required int ExpectedCurrentCandleIndex { get; init; }
        public required long[] ExpectedExecutionCandleIds { get; init; }
        public required long[] ExpectedHtfCandleIds { get; init; }
    }

    public sealed class RejectionParityEvidence
    {
        public required StrategyEvaluationCaptureRecord Capture { get; init; }
        public required MarketRegime ExpectedRegime { get; init; }
        public required string ExpectedLabRejectionCode { get; init; }
        public required string LabResultSummaryJson { get; init; }
        public required DateTime ExpectedEvaluationTimestamp { get; init; }
        public required int ExpectedCurrentCandleIndex { get; init; }
        public required long[] ExpectedExecutionCandleIds { get; init; }
        public required long[] ExpectedHtfCandleIds { get; init; }
    }

    public static void AssertPositiveEntryParity(
        StrategySignalResult direct,
        StrategyResearchCandidate lab,
        StrategyEvaluationResult backtest,
        PositiveParityEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(direct);
        ArgumentNullException.ThrowIfNull(lab);
        ArgumentNullException.ThrowIfNull(backtest);
        ArgumentNullException.ThrowIfNull(evidence);

        Assert.Equal(SignalType.Entry, direct.SignalType);
        Assert.NotNull(direct.EntryPrice);
        Assert.Equal(SignalType.Entry, backtest.SignalType);
        Assert.NotNull(backtest.EntryPrice);

        Assert.Equal(direct.Direction, lab.Direction);
        Assert.Equal(direct.EntryPrice, lab.ProposedEntryPrice);
        Assert.Equal(direct.SuggestedStopLoss, lab.StopLoss);
        Assert.Equal(direct.SuggestedTakeProfit, lab.Target1);

        Assert.Equal(direct.Reason, backtest.Reason);
        Assert.Equal(direct.Direction, backtest.Direction);
        Assert.Equal(direct.EntryPrice, backtest.EntryPrice);
        Assert.Equal(direct.SuggestedStopLoss, backtest.SuggestedStopLoss);
        Assert.Equal(direct.SuggestedTakeProfit, backtest.SuggestedTakeProfit);
        Assert.Equal(direct.Strength, backtest.Strength);
        Assert.Equal(direct.RawDataJson, backtest.RawDataJson);

        var directFp = StrategyLabRunner.ExtractFingerprint(direct.RawDataJson ?? "{}");
        Assert.False(string.IsNullOrWhiteSpace(directFp));
        Assert.Equal(directFp, lab.SetupFingerprint);
        Assert.Equal(directFp, Milestone231BParityFixtures.ExtractFingerprint(backtest.RawDataJson));

        AssertStrength(direct.Strength, lab.StructureJson, backtest.RawDataJson);
        Assert.Equal(
            Milestone231BParityFixtures.ExtractStrengthBreakdown(direct.RawDataJson),
            Milestone231BParityFixtures.ExtractStrengthBreakdown(lab.StructureJson));
        Assert.Equal(
            Milestone231BParityFixtures.ExtractStrengthBreakdown(direct.RawDataJson),
            Milestone231BParityFixtures.ExtractStrengthBreakdown(backtest.RawDataJson));

        Assert.Equal(evidence.ExpectedRegime.ToString(), backtest.Regime);
        Assert.Equal(evidence.ExpectedEvaluationTimestamp, evidence.Capture.EvaluatedAtUtc);
        Assert.Equal(evidence.ExpectedTimeframe, evidence.Capture.ExecutionTimeframe);
        Assert.Equal(evidence.ExpectedHigherTimeframe, evidence.Capture.HigherTimeframe);
        Assert.Equal(evidence.ExpectedExecutionCandleIds, evidence.Capture.Candles.Select(c => c.Id).ToArray());
        Assert.Equal(evidence.ExpectedHtfCandleIds, evidence.Capture.HigherTimeframeCandles.Select(c => c.Id).ToArray());

        Assert.NotEmpty(evidence.Capture.Candles);
        Assert.Equal(evidence.ExpectedExchangeId, evidence.Capture.Candles[0].ExchangeId);
        Assert.Equal(evidence.ExpectedSymbolId, evidence.Capture.Candles[0].SymbolId);
        Assert.Equal(evidence.ExpectedCurrentCandleIndex, evidence.Capture.Candles.Count - 1);
    }

    public static void AssertRejectionParity(
        StrategySignalResult direct,
        StrategyEvaluationResult backtest,
        RejectionParityEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(direct);
        ArgumentNullException.ThrowIfNull(backtest);
        ArgumentNullException.ThrowIfNull(evidence);

        Assert.NotEqual(SignalType.Entry, direct.SignalType);
        Assert.NotEqual(SignalType.Entry, backtest.SignalType);
        Assert.Equal(direct.SignalType, backtest.SignalType);
        Assert.Equal(direct.Reason, backtest.Reason);
        Assert.Equal(direct.Direction, backtest.Direction);
        Assert.Equal(direct.Strength, backtest.Strength);
        Assert.Equal(evidence.ExpectedRegime.ToString(), backtest.Regime);

        var directFp = StrategyLabRunner.ExtractFingerprint(direct.RawDataJson ?? "{}") ?? string.Empty;
        var backtestFp = Milestone231BParityFixtures.ExtractFingerprint(backtest.RawDataJson) ?? string.Empty;
        Assert.Equal(directFp, backtestFp);

        Assert.False(string.IsNullOrWhiteSpace(evidence.ExpectedLabRejectionCode));
        Assert.False(string.IsNullOrWhiteSpace(evidence.LabResultSummaryJson));

        using var doc = JsonDocument.Parse(evidence.LabResultSummaryJson);
        var funnel = doc.RootElement.GetProperty("rejectionFunnel").GetProperty("counts");
        Assert.True(
            funnel.TryGetProperty(evidence.ExpectedLabRejectionCode, out var countEl),
            $"Expected lab rejection funnel code '{evidence.ExpectedLabRejectionCode}'.");
        Assert.True(countEl.GetInt32() > 0, $"Lab rejection code '{evidence.ExpectedLabRejectionCode}' must have a positive count.");

        Assert.Equal(evidence.ExpectedEvaluationTimestamp, evidence.Capture.EvaluatedAtUtc);
        Assert.Equal(evidence.ExpectedExecutionCandleIds, evidence.Capture.Candles.Select(c => c.Id).ToArray());
        Assert.Equal(evidence.ExpectedHtfCandleIds, evidence.Capture.HigherTimeframeCandles.Select(c => c.Id).ToArray());
        Assert.Equal(evidence.ExpectedCurrentCandleIndex, evidence.Capture.Candles.Count - 1);
    }

    public static decimal ExtractStrengthForTest(string? json)
    {
        var strength = ExtractStrength(json);
        Assert.NotNull(strength);
        return strength.Value;
    }

    private static void AssertStrength(decimal directStrength, string? labStructure, string? backtestRaw)
    {
        var labStrength = ExtractStrength(labStructure);
        var backtestStrength = ExtractStrength(backtestRaw);
        Assert.NotNull(labStrength);
        Assert.NotNull(backtestStrength);

        Assert.Equal(directStrength, labStrength.Value);
        Assert.Equal(directStrength, backtestStrength.Value);

        Assert.Equal(
            Milestone231BParityFixtures.ExtractStrengthBreakdown(labStructure),
            Milestone231BParityFixtures.ExtractStrengthBreakdown(backtestRaw));
    }

    private static decimal? ExtractStrength(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (TryExtractStrengthFromElement(root, out var strength))
        {
            return strength;
        }

        if (root.TryGetProperty("diagnostics", out var diagnostics)
            && TryExtractStrengthFromElement(diagnostics, out strength))
        {
            return strength;
        }

        return null;
    }

    private static bool TryExtractStrengthFromElement(JsonElement element, out decimal strength)
    {
        if (element.TryGetProperty("strength", out var s) && s.TryGetDecimal(out strength))
        {
            return true;
        }

        if (element.TryGetProperty("strengthBreakdown", out var b)
            && b.TryGetProperty("total", out var t)
            && t.TryGetDecimal(out strength))
        {
            return true;
        }

        strength = default;
        return false;
    }
}
