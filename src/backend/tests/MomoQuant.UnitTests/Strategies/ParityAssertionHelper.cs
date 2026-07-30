using System.Text.Json;
using MomoQuant.Application.Strategies;
using MomoQuant.Application.Strategies.Models;
using MomoQuant.Application.StrategyLab;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Indicators;
using MomoQuant.Domain.MarketData;
using MomoQuant.Domain.Strategies;
using MomoQuant.Domain.StrategyLab;

namespace MomoQuant.UnitTests.Strategies;

/// <summary>Mandatory three-path parity evidence assertions (Milestone 23.1B1C1).</summary>
internal static class ParityAssertionHelper
{
    public sealed class PositiveThreePathEvidence
    {
        public required StrategyEvaluationCaptureRecord BacktestCapture { get; init; }
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
        public required IReadOnlyDictionary<string, string> ExpectedParameters { get; init; }
        public required IndicatorSnapshot? ExpectedIndicatorSnapshot { get; init; }
    }

    public sealed class RejectionThreePathEvidence
    {
        public required StrategyEvaluationCaptureRecord BacktestCapture { get; init; }
        public required MarketRegime ExpectedRegime { get; init; }
        public required string ExpectedLabRejectionCode { get; init; }
        public required string LabResultSummaryJson { get; init; }
        public required DateTime ExpectedEvaluationTimestamp { get; init; }
        public required int ExpectedCurrentCandleIndex { get; init; }
        public required long[] ExpectedExecutionCandleIds { get; init; }
        public required long[] ExpectedHtfCandleIds { get; init; }
        public required IReadOnlyDictionary<string, string> ExpectedParameters { get; init; }
        public required IndicatorSnapshot? ExpectedIndicatorSnapshot { get; init; }
        public int ExpectedFunnelCount { get; init; } = 1;
    }

    public static void AssertPositiveThreePathParity(
        StrategySignalResult direct,
        StrategyContext labContext,
        StrategySignalResult labSignal,
        StrategyResearchCandidate labCandidate,
        StrategyEvaluationResult backtestResult,
        PositiveThreePathEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(direct);
        ArgumentNullException.ThrowIfNull(labContext);
        ArgumentNullException.ThrowIfNull(labSignal);
        ArgumentNullException.ThrowIfNull(labCandidate);
        ArgumentNullException.ThrowIfNull(backtestResult);
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(evidence.BacktestCapture);

        Assert.Equal(SignalType.Entry, direct.SignalType);
        Assert.Equal(SignalType.Entry, labSignal.SignalType);
        Assert.Equal(SignalType.Entry, backtestResult.SignalType);

        Assert.NotNull(direct.EntryPrice);
        Assert.NotNull(labSignal.EntryPrice);
        Assert.NotNull(backtestResult.EntryPrice);

        AssertSignalResultsEqual(direct, labSignal, "direct", "lab");
        AssertDirectMatchesBacktest(direct, backtestResult);

        AssertContextParity(labContext, evidence.BacktestCapture, evidence);

        Assert.Equal(evidence.ExpectedRegime.ToString(), backtestResult.Regime);

        var directFingerprint = RequireFingerprint(direct.RawDataJson, "direct");
        var labFingerprint = RequireFingerprint(labSignal.RawDataJson, "lab");
        var backtestFingerprint = RequireFingerprint(backtestResult.RawDataJson, "backtest");

        Assert.Equal(directFingerprint, labFingerprint);
        Assert.Equal(directFingerprint, backtestFingerprint);
        Assert.Equal(directFingerprint, labCandidate.SetupFingerprint);

        AssertStrengthAndBreakdown(direct.RawDataJson, labSignal.RawDataJson, backtestResult.RawDataJson, labCandidate.StructureJson);

        Assert.Equal(labSignal.Direction, labCandidate.Direction);
        Assert.Equal(labSignal.EntryPrice, labCandidate.ProposedEntryPrice);
        Assert.Equal(labSignal.SuggestedStopLoss, labCandidate.StopLoss);
        Assert.Equal(labSignal.SuggestedTakeProfit, labCandidate.Target1);
        Assert.Equal(labSignal.Reason, labCandidate.StrategyReason);
    }

    public static void AssertRejectionThreePathParity(
        StrategySignalResult direct,
        StrategyContext labContext,
        StrategySignalResult labSignal,
        StrategyEvaluationResult backtestResult,
        RejectionThreePathEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(direct);
        ArgumentNullException.ThrowIfNull(labContext);
        ArgumentNullException.ThrowIfNull(labSignal);
        ArgumentNullException.ThrowIfNull(backtestResult);
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(evidence.BacktestCapture);

        Assert.NotEqual(SignalType.Entry, direct.SignalType);
        Assert.NotEqual(SignalType.Entry, labSignal.SignalType);
        Assert.NotEqual(SignalType.Entry, backtestResult.SignalType);

        AssertSignalResultsEqual(direct, labSignal, "direct", "lab");
        AssertDirectMatchesBacktest(direct, backtestResult);

        AssertContextParity(labContext, evidence.BacktestCapture, evidence);

        Assert.Equal(evidence.ExpectedRegime.ToString(), backtestResult.Regime);

        Assert.False(string.IsNullOrWhiteSpace(evidence.ExpectedLabRejectionCode));
        Assert.Equal(evidence.ExpectedLabRejectionCode, labSignal.Reason);
        Assert.Equal(evidence.ExpectedLabRejectionCode, direct.Reason);
        Assert.Equal(evidence.ExpectedLabRejectionCode, backtestResult.Reason);

        Assert.False(string.IsNullOrWhiteSpace(evidence.LabResultSummaryJson));
        using var doc = JsonDocument.Parse(evidence.LabResultSummaryJson);
        var funnel = doc.RootElement.GetProperty("rejectionFunnel").GetProperty("counts");
        Assert.True(
            funnel.TryGetProperty(evidence.ExpectedLabRejectionCode, out var countEl),
            $"Expected lab rejection funnel code '{evidence.ExpectedLabRejectionCode}'.");
        Assert.Equal(evidence.ExpectedFunnelCount, countEl.GetInt32());

        if (TryRequireFingerprint(direct.RawDataJson, out var directFingerprint)
            && TryRequireFingerprint(labSignal.RawDataJson, out var labFingerprint)
            && TryRequireFingerprint(backtestResult.RawDataJson, out var backtestFingerprint))
        {
            Assert.Equal(directFingerprint, labFingerprint);
            Assert.Equal(directFingerprint, backtestFingerprint);
        }
    }

    public static decimal ExtractStrengthForTest(string json)
    {
        var strength = RequireStrength(json, "test");
        return strength;
    }

    private static void AssertContextParity(
        StrategyContext labContext,
        StrategyEvaluationCaptureRecord backtestCapture,
        PositiveThreePathEvidence evidence)
    {
        Assert.NotEmpty(labContext.Candles);
        Assert.NotEmpty(backtestCapture.Candles);

        Assert.Equal(evidence.ExpectedExchangeId, labContext.ExchangeId);
        Assert.Equal(evidence.ExpectedExchangeId, backtestCapture.Candles[0].ExchangeId);

        Assert.Equal(evidence.ExpectedSymbolId, labContext.SymbolId);
        Assert.Equal(evidence.ExpectedSymbolId, backtestCapture.Candles[0].SymbolId);
        Assert.Equal(evidence.ExpectedSymbol, labContext.Symbol);

        Assert.Equal(evidence.ExpectedTimeframe, labContext.Timeframe);
        Assert.Equal(evidence.ExpectedTimeframe, backtestCapture.ExecutionTimeframe);

        Assert.Equal(evidence.ExpectedHigherTimeframe, labContext.HigherTimeframe);
        Assert.Equal(evidence.ExpectedHigherTimeframe, backtestCapture.HigherTimeframe);

        Assert.Equal(evidence.ExpectedRegime, labContext.MarketRegime);

        Assert.Equal(evidence.ExpectedEvaluationTimestamp, labContext.EvaluatedAtUtc);
        Assert.Equal(evidence.ExpectedEvaluationTimestamp, backtestCapture.EvaluatedAtUtc);

        Assert.Equal(evidence.ExpectedCurrentCandleIndex, labContext.CurrentCandleIndex);
        Assert.Equal(evidence.ExpectedCurrentCandleIndex, backtestCapture.Candles.Count - 1);

        Assert.Equal(evidence.ExpectedExecutionCandleIds, labContext.Candles.Select(c => c.Id).ToArray());
        Assert.Equal(evidence.ExpectedExecutionCandleIds, backtestCapture.Candles.Select(c => c.Id).ToArray());

        Assert.Equal(evidence.ExpectedHtfCandleIds, labContext.HigherTimeframeCandles.Select(c => c.Id).ToArray());
        Assert.Equal(evidence.ExpectedHtfCandleIds, backtestCapture.HigherTimeframeCandles.Select(c => c.Id).ToArray());

        AssertParametersEqual(evidence.ExpectedParameters, labContext.StrategyParameters);
        AssertIndicatorSnapshotIdentity(evidence.ExpectedIndicatorSnapshot, labContext.IndicatorSnapshot);
    }

    private static void AssertContextParity(
        StrategyContext labContext,
        StrategyEvaluationCaptureRecord backtestCapture,
        RejectionThreePathEvidence evidence)
    {
        Assert.NotEmpty(labContext.Candles);
        Assert.NotEmpty(backtestCapture.Candles);

        Assert.Equal(evidence.ExpectedEvaluationTimestamp, labContext.EvaluatedAtUtc);
        Assert.Equal(evidence.ExpectedEvaluationTimestamp, backtestCapture.EvaluatedAtUtc);

        Assert.Equal(evidence.ExpectedCurrentCandleIndex, labContext.CurrentCandleIndex);
        Assert.Equal(evidence.ExpectedCurrentCandleIndex, backtestCapture.Candles.Count - 1);

        Assert.Equal(evidence.ExpectedExecutionCandleIds, labContext.Candles.Select(c => c.Id).ToArray());
        Assert.Equal(evidence.ExpectedExecutionCandleIds, backtestCapture.Candles.Select(c => c.Id).ToArray());

        Assert.Equal(evidence.ExpectedHtfCandleIds, labContext.HigherTimeframeCandles.Select(c => c.Id).ToArray());
        Assert.Equal(evidence.ExpectedHtfCandleIds, backtestCapture.HigherTimeframeCandles.Select(c => c.Id).ToArray());

        Assert.Equal(evidence.ExpectedRegime, labContext.MarketRegime);

        AssertParametersEqual(evidence.ExpectedParameters, labContext.StrategyParameters);
        AssertIndicatorSnapshotIdentity(evidence.ExpectedIndicatorSnapshot, labContext.IndicatorSnapshot);
    }

    private static void AssertSignalResultsEqual(
        StrategySignalResult left,
        StrategySignalResult right,
        string leftLabel,
        string rightLabel)
    {
        Assert.Equal(left.SignalType, right.SignalType);
        Assert.Equal(left.Reason, right.Reason);
        Assert.Equal(left.Direction, right.Direction);
        Assert.Equal(left.EntryPrice, right.EntryPrice);
        Assert.Equal(left.SuggestedStopLoss, right.SuggestedStopLoss);
        Assert.Equal(left.SuggestedTakeProfit, right.SuggestedTakeProfit);
        Assert.Equal(left.Strength, right.Strength);
        Assert.Equal(left.ConfidenceContribution, right.ConfidenceContribution);
        Assert.Equal(left.RawDataJson, right.RawDataJson);
    }

    private static void AssertDirectMatchesBacktest(StrategySignalResult direct, StrategyEvaluationResult backtest)
    {
        Assert.Equal(direct.SignalType, backtest.SignalType);
        Assert.Equal(direct.Reason, backtest.Reason);
        Assert.Equal(direct.Direction, backtest.Direction);
        Assert.Equal(direct.EntryPrice, backtest.EntryPrice);
        Assert.Equal(direct.SuggestedStopLoss, backtest.SuggestedStopLoss);
        Assert.Equal(direct.SuggestedTakeProfit, backtest.SuggestedTakeProfit);
        Assert.Equal(direct.Strength, backtest.Strength);
        Assert.Equal(direct.ConfidenceContribution, backtest.ConfidenceContribution);
        Assert.Equal(direct.RawDataJson, backtest.RawDataJson);
    }

    private static void AssertStrengthAndBreakdown(
        string? directJson,
        string? labJson,
        string? backtestJson,
        string? labStructureJson)
    {
        var directStrength = RequireStrength(directJson, "direct");
        var labStrength = RequireStrength(labJson, "lab");
        var backtestStrength = RequireStrength(backtestJson, "backtest");
        var candidateStrength = RequireStrength(labStructureJson, "lab candidate");

        Assert.Equal(directStrength, labStrength);
        Assert.Equal(directStrength, backtestStrength);
        Assert.Equal(directStrength, candidateStrength);

        var directBreakdown = RequireStrengthBreakdown(directJson, "direct");
        var labBreakdown = RequireStrengthBreakdown(labJson, "lab");
        var backtestBreakdown = RequireStrengthBreakdown(backtestJson, "backtest");
        var candidateBreakdown = RequireStrengthBreakdown(labStructureJson, "lab candidate");

        Assert.Equal(directBreakdown, labBreakdown);
        Assert.Equal(directBreakdown, backtestBreakdown);
        Assert.Equal(directBreakdown, candidateBreakdown);
    }

    private static void AssertParametersEqual(
        IReadOnlyDictionary<string, string> expected,
        IReadOnlyDictionary<string, string> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        foreach (var (key, value) in expected)
        {
            Assert.True(actual.TryGetValue(key, out var actualValue), $"Missing parameter '{key}'.");
            Assert.Equal(value, actualValue);
        }
    }

    private static void AssertIndicatorSnapshotIdentity(IndicatorSnapshot? expected, IndicatorSnapshot? actual)
    {
        if (expected is null)
        {
            Assert.Null(actual);
            return;
        }

        Assert.NotNull(actual);
        Assert.Equal(expected.CandleId, actual.CandleId);
        Assert.Equal(expected.SymbolId, actual.SymbolId);
        Assert.Equal(expected.Timeframe, actual.Timeframe);
        Assert.Equal(expected.Ema20, actual.Ema20);
        Assert.Equal(expected.Ema50, actual.Ema50);
        Assert.Equal(expected.Ema200, actual.Ema200);
        Assert.Equal(expected.Atr14, actual.Atr14);
    }

    private static string RequireFingerprint(string? rawDataJson, string sourceLabel)
    {
        Assert.True(
            TryRequireFingerprint(rawDataJson, out var fingerprint),
            $"{sourceLabel} setupFingerprint property is missing.");
        return fingerprint;
    }

    private static bool TryRequireFingerprint(string? rawDataJson, out string fingerprint)
    {
        fingerprint = string.Empty;
        if (string.IsNullOrWhiteSpace(rawDataJson))
        {
            return false;
        }

        using var doc = JsonDocument.Parse(rawDataJson);
        if (TryGetPropertyIgnoreCase(doc.RootElement, "setupFingerprint", out var fp)
            || TryGetPropertyIgnoreCase(doc.RootElement, "SetupFingerprint", out fp))
        {
            var value = fp.GetString();
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            fingerprint = value;
            return true;
        }

        return false;
    }

    private static decimal RequireStrength(string? json, string sourceLabel)
    {
        Assert.False(string.IsNullOrWhiteSpace(json), $"{sourceLabel} JSON is required for strength.");
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

        Assert.Fail($"{sourceLabel} strength property is missing.");
        return default;
    }

    private static string RequireStrengthBreakdown(string? json, string sourceLabel)
    {
        var breakdown = Milestone231BParityFixtures.ExtractStrengthBreakdown(json);
        Assert.False(string.IsNullOrWhiteSpace(breakdown), $"{sourceLabel} strengthBreakdown is required.");
        return breakdown;
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

    private static bool TryGetPropertyIgnoreCase(JsonElement element, string name, out JsonElement value)
    {
        if (element.TryGetProperty(name, out value))
        {
            return true;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}
