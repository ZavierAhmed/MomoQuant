using System.Text.Json;
using MomoQuant.Application.Strategies;
using MomoQuant.Application.Strategies.Models;
using MomoQuant.Application.StrategyLab;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.MarketData;
using MomoQuant.Domain.Strategies;
using MomoQuant.Domain.StrategyLab;

namespace MomoQuant.UnitTests.Strategies;

/// <summary>Non-vacuous cross-path parity assertions (Milestone 23.1B1B).</summary>
internal static class ParityAssertionHelper
{
    public sealed class ParityPaths
    {
        public required StrategySignalResult Direct { get; init; }
        public required StrategyResearchCandidate Lab { get; init; }
        public required StrategyEvaluationResult Backtest { get; init; }
        public StrategyEvaluationCaptureRecord? Capture { get; init; }
        public MarketRegime? ExpectedRegime { get; init; }
        public Timeframe ExpectedHigherTimeframe { get; init; }
        public long ExpectedExchangeId { get; init; } = 1;
        public long ExpectedSymbolId { get; init; } = 1;
        public string ExpectedSymbol { get; init; } = "BTCUSDT";
        public Timeframe ExpectedTimeframe { get; init; } = Timeframe.M5;
    }

    public sealed class RejectionPaths
    {
        public required StrategySignalResult Direct { get; init; }
        public required StrategyEvaluationResult Backtest { get; init; }
        public string? LabResultSummaryJson { get; init; }
        public string? ExpectedLabRejectionCode { get; init; }
    }

    public static void AssertPositiveEntryParity(ParityPaths paths)
    {
        Assert.Equal(SignalType.Entry, paths.Direct.SignalType);
        Assert.NotNull(paths.Direct.EntryPrice);
        Assert.Equal(SignalType.Entry, paths.Backtest.SignalType);
        Assert.NotNull(paths.Backtest.EntryPrice);

        Assert.Equal(paths.Direct.Direction, paths.Lab.Direction);
        Assert.Equal(paths.Direct.EntryPrice, paths.Lab.ProposedEntryPrice);
        Assert.Equal(paths.Direct.SuggestedStopLoss, paths.Lab.StopLoss);
        Assert.Equal(paths.Direct.SuggestedTakeProfit, paths.Lab.Target1);

        Assert.Equal(paths.Direct.Reason, paths.Backtest.Reason);
        Assert.Equal(paths.Direct.Direction, paths.Backtest.Direction);
        Assert.Equal(paths.Direct.EntryPrice, paths.Backtest.EntryPrice);
        Assert.Equal(paths.Direct.SuggestedStopLoss, paths.Backtest.SuggestedStopLoss);
        Assert.Equal(paths.Direct.SuggestedTakeProfit, paths.Backtest.SuggestedTakeProfit);
        Assert.Equal(paths.Direct.Strength, paths.Backtest.Strength);
        Assert.Equal(paths.Direct.RawDataJson, paths.Backtest.RawDataJson);

        var directFp = StrategyLabRunner.ExtractFingerprint(paths.Direct.RawDataJson ?? "{}");
        Assert.False(string.IsNullOrWhiteSpace(directFp));
        Assert.Equal(directFp, paths.Lab.SetupFingerprint);
        Assert.Equal(directFp, Milestone231BParityFixtures.ExtractFingerprint(paths.Backtest.RawDataJson));

        AssertStrength(paths.Direct.Strength, paths.Lab.StructureJson, paths.Backtest.RawDataJson);
        Assert.Equal(
            Milestone231BParityFixtures.ExtractStrengthBreakdown(paths.Direct.RawDataJson),
            Milestone231BParityFixtures.ExtractStrengthBreakdown(paths.Lab.StructureJson));
        Assert.Equal(
            Milestone231BParityFixtures.ExtractStrengthBreakdown(paths.Direct.RawDataJson),
            Milestone231BParityFixtures.ExtractStrengthBreakdown(paths.Backtest.RawDataJson));

        if (paths.ExpectedRegime is { } regime)
        {
            Assert.Equal(regime.ToString(), paths.Backtest.Regime);
        }

        if (paths.Capture is not null)
        {
            Assert.Equal(paths.ExpectedExchangeId, paths.Capture.Candles.FirstOrDefault()?.ExchangeId ?? paths.ExpectedExchangeId);
            Assert.Equal(paths.ExpectedSymbolId, paths.Capture.Candles.FirstOrDefault()?.SymbolId ?? paths.ExpectedSymbolId);
            Assert.Equal(paths.ExpectedTimeframe, paths.Capture.ExecutionTimeframe);
            Assert.Equal(paths.ExpectedHigherTimeframe, paths.Capture.HigherTimeframe);
        }
    }

    public static void AssertRejectionParity(RejectionPaths paths)
    {
        Assert.NotEqual(SignalType.Entry, paths.Direct.SignalType);
        Assert.NotEqual(SignalType.Entry, paths.Backtest.SignalType);
        Assert.Equal(paths.Direct.SignalType, paths.Backtest.SignalType);
        Assert.Equal(paths.Direct.Reason, paths.Backtest.Reason);
        Assert.Equal(paths.Direct.Direction, paths.Backtest.Direction);
        Assert.Equal(paths.Direct.Strength, paths.Backtest.Strength);

        var directFp = StrategyLabRunner.ExtractFingerprint(paths.Direct.RawDataJson ?? "{}") ?? string.Empty;
        var backtestFp = Milestone231BParityFixtures.ExtractFingerprint(paths.Backtest.RawDataJson) ?? string.Empty;
        Assert.Equal(directFp, backtestFp);

        if (!string.IsNullOrWhiteSpace(paths.ExpectedLabRejectionCode)
            && !string.IsNullOrWhiteSpace(paths.LabResultSummaryJson))
        {
            using var doc = JsonDocument.Parse(paths.LabResultSummaryJson);
            var funnel = doc.RootElement.GetProperty("rejectionFunnel").GetProperty("counts");
            Assert.True(
                funnel.TryGetProperty(paths.ExpectedLabRejectionCode, out _),
                $"Expected lab rejection funnel code '{paths.ExpectedLabRejectionCode}'.");
        }
    }

    private static void AssertStrength(decimal directStrength, string? labStructure, string? backtestRaw)
    {
        var labStrength = ExtractStrength(labStructure);
        var backtestStrength = ExtractStrength(backtestRaw);

        Assert.Equal(directStrength, labStrength);
        Assert.Equal(directStrength, backtestStrength);

        Assert.Equal(
            Milestone231BParityFixtures.ExtractStrengthBreakdown(labStructure),
            Milestone231BParityFixtures.ExtractStrengthBreakdown(backtestRaw));
    }

    public static decimal? ExtractStrengthForTest(string? json) => ExtractStrength(json);

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
