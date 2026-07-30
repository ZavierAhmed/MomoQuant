using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using MomoQuant.Application.Strategies;
using MomoQuant.Application.Strategies.Models;
using MomoQuant.Application.StrategyLab;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Indicators;
using MomoQuant.Domain.MarketData;
using MomoQuant.Domain.Strategies;
using MomoQuant.Domain.StrategyLab;

namespace MomoQuant.UnitTests.Strategies;

/// <summary>Mandatory three-path parity evidence assertions (Milestone 23.1B1C2).</summary>
internal static class ParityAssertionHelper
{
    public enum RawDataJsonRootState { Null, PresentJsonObject }

    public enum JsonPropertyState
    {
        Absent,
        ExplicitNull,
        EmptyString,
        WhitespaceString,
        ExactString,
        ExactNumber,
        ExactBoolean,
        ExactJsonValue
    }

    /// <summary>Immutable exact JSON-property expectation; no mutable JSON node is retained.</summary>
    public sealed record JsonPropertyExpectation(
        JsonPropertyState State,
        string? StringValue = null,
        decimal? NumberValue = null,
        bool? BooleanValue = null,
        string? CanonicalJsonValue = null)
    {
        public static JsonPropertyExpectation Absent() => new(JsonPropertyState.Absent);
        public static JsonPropertyExpectation Null() => new(JsonPropertyState.ExplicitNull);
        public static JsonPropertyExpectation Empty() => new(JsonPropertyState.EmptyString);
        public static JsonPropertyExpectation Whitespace(string value) => new(JsonPropertyState.WhitespaceString, StringValue: value);
        public static JsonPropertyExpectation String(string value) => new(JsonPropertyState.ExactString, StringValue: value);
        public static JsonPropertyExpectation Number(decimal value) => new(JsonPropertyState.ExactNumber, NumberValue: value);
        public static JsonPropertyExpectation Boolean(bool value) => new(JsonPropertyState.ExactBoolean, BooleanValue: value);
        public static JsonPropertyExpectation Json(string canonicalValue) => new(JsonPropertyState.ExactJsonValue, CanonicalJsonValue: canonicalValue);
    }

    /// <summary>Immutable root and exact-property contract for a RawDataJson payload.</summary>
    public sealed record RawDataJsonContract(
        RawDataJsonRootState RootState,
        ImmutableDictionary<string, JsonPropertyExpectation> Properties)
    {
        public static RawDataJsonContract Create(
            RawDataJsonRootState rootState,
            params (string Name, JsonPropertyExpectation Expectation)[] properties) =>
            new(rootState, properties.ToImmutableDictionary(item => item.Name, item => item.Expectation, StringComparer.Ordinal));
    }

    public abstract record FingerprintContract
    {
        public sealed record RequiredPresent(string ExpectedValue) : FingerprintContract;
        public sealed record RequiredAbsent : FingerprintContract;
    }

    public sealed class PositiveThreePathEvidence
    {
        public required IReadOnlyList<(StrategyContext Context, StrategySignalResult Result)> LabEvaluations { get; init; }
        public required IReadOnlyList<StrategyEvaluationCaptureRecord> BacktestCaptures { get; init; }
        public required IReadOnlyList<StrategyResearchCandidate> LabCandidates { get; init; }
        public string LabResultSummaryJson { get; init; } = string.Empty;
        public required StrategyCode ExpectedStrategyCode { get; init; }
        public required string ExpectedStrategyVersion { get; init; }
        public required long ExpectedStrategyLabRunId { get; init; }
        public required MarketRegime ExpectedRegime { get; init; }
        public required long ExpectedExchangeId { get; init; }
        public required long ExpectedSymbolId { get; init; }
        public required string ExpectedSymbol { get; init; }
        public required Timeframe ExpectedTimeframe { get; init; }
        public required string ExpectedTimeframeApi { get; init; }
        public required Timeframe ExpectedHigherTimeframe { get; init; }
        public required DateTime ExpectedEvaluationTimestamp { get; init; }
        public required int ExpectedCurrentCandleIndex { get; init; }
        public required long[] ExpectedExecutionCandleIds { get; init; }
        public required long[] ExpectedHtfCandleIds { get; init; }
        public required IReadOnlyDictionary<string, string> ExpectedParameters { get; init; }
        public required IndicatorSnapshot? ExpectedIndicatorSnapshot { get; init; }
        public required FingerprintContract Fingerprint { get; init; }
        public required IReadOnlyList<string> RequiredRawDataJsonProperties { get; init; }
        public required IReadOnlyList<string> RequiredStructureJsonProperties { get; init; }
        public RawDataJsonContract? RawDataContract { get; init; }
        public StrategyResearchCandidateStatus ExpectedCandidateStatus { get; init; } = StrategyResearchCandidateStatus.Detected;
    }

    public sealed class RejectionThreePathEvidence
    {
        public required IReadOnlyList<(StrategyContext Context, StrategySignalResult Result)> LabEvaluations { get; init; }
        public required IReadOnlyList<StrategyEvaluationCaptureRecord> BacktestCaptures { get; init; }
        public required IReadOnlyList<StrategyResearchCandidate> LabCandidates { get; init; }
        public required string LabResultSummaryJson { get; init; }
        /// <summary>The actual persisted row that owns the rejection funnel for production-path cases.</summary>
        public StrategyLabRun? PersistedLabRun { get; init; }
        public required long ExpectedStrategyLabRunId { get; init; }
        public required StrategyCode ExpectedStrategyCode { get; init; }
        public required string ExpectedLabRejectionCode { get; init; }
        public int ExpectedFunnelCount { get; init; } = 1;
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
        public required FingerprintContract Fingerprint { get; init; }
        public required IReadOnlyList<string> RequiredRawDataJsonProperties { get; init; }
        public RawDataJsonContract? RawDataContract { get; init; }
    }

    public static void AssertPositiveThreePathParity(
        StrategyContext directContext,
        StrategySignalResult direct,
        StrategyEvaluationResult backtestResult,
        PositiveThreePathEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(directContext);
        ArgumentNullException.ThrowIfNull(direct);
        ArgumentNullException.ThrowIfNull(backtestResult);
        ArgumentNullException.ThrowIfNull(evidence);

        var labEval = Assert.Single(evidence.LabEvaluations);
        var backtestCapture = Assert.Single(evidence.BacktestCaptures);
        var labCandidate = Assert.Single(evidence.LabCandidates);

        Assert.Equal(evidence.ExpectedEvaluationTimestamp, labEval.Context.EvaluatedAtUtc);
        Assert.Equal(evidence.ExpectedEvaluationTimestamp, backtestCapture.EvaluatedAtUtc);
        Assert.Equal(evidence.ExpectedEvaluationTimestamp, directContext.EvaluatedAtUtc);

        Assert.Equal(evidence.ExpectedStrategyCode, backtestCapture.StrategyCode);
        Assert.Equal(evidence.ExpectedStrategyCode.ToCode(), backtestResult.StrategyCode);

        AssertContextParity(directContext, labEval.Context, backtestCapture, evidence);
        AssertContextMatchesExpectedFixture(labEval.Context, backtestCapture, evidence);

        Assert.Equal(SignalType.Entry, direct.SignalType);
        Assert.Equal(SignalType.Entry, labEval.Result.SignalType);
        Assert.Equal(SignalType.Entry, backtestResult.SignalType);

        Assert.NotNull(direct.EntryPrice);
        Assert.NotNull(labEval.Result.EntryPrice);
        Assert.NotNull(backtestResult.EntryPrice);

        AssertSignalResultsEqual(direct, labEval.Result, "direct", "lab");
        AssertDirectMatchesBacktest(direct, backtestResult);

        Assert.Equal(evidence.ExpectedRegime.ToString(), backtestResult.Regime);

        AssertFingerprintContract(
            evidence.Fingerprint,
            direct.RawDataJson,
            labEval.Result.RawDataJson,
            backtestResult.RawDataJson,
            labCandidate.SetupFingerprint);

        AssertRawDataEvidence(evidence.RawDataContract, evidence.RequiredRawDataJsonProperties,
            direct.RawDataJson, labEval.Result.RawDataJson, backtestResult.RawDataJson);

        AssertStrengthAndBreakdown(
            direct.RawDataJson,
            labEval.Result.RawDataJson,
            backtestResult.RawDataJson,
            labCandidate.StructureJson);

        AssertRequiredJsonProperties(
            evidence.RequiredStructureJsonProperties,
            labCandidate.StructureJson);

        AssertCandidateParity(labEval, labCandidate, direct, evidence);
    }

    public static void AssertRejectionThreePathParity(
        StrategyContext directContext,
        StrategySignalResult direct,
        StrategyEvaluationResult backtestResult,
        RejectionThreePathEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(directContext);
        ArgumentNullException.ThrowIfNull(direct);
        ArgumentNullException.ThrowIfNull(backtestResult);
        ArgumentNullException.ThrowIfNull(evidence);

        var labEval = Assert.Single(evidence.LabEvaluations);
        var backtestCapture = Assert.Single(evidence.BacktestCaptures);
        Assert.Empty(evidence.LabCandidates);

        Assert.True(evidence.ExpectedStrategyLabRunId > 0);
        Assert.Equal(evidence.ExpectedStrategyCode, backtestCapture.StrategyCode);
        Assert.Equal(evidence.ExpectedStrategyCode.ToCode(), backtestResult.StrategyCode);

        Assert.Equal(evidence.ExpectedEvaluationTimestamp, labEval.Context.EvaluatedAtUtc);
        Assert.Equal(evidence.ExpectedEvaluationTimestamp, backtestCapture.EvaluatedAtUtc);
        Assert.Equal(evidence.ExpectedEvaluationTimestamp, directContext.EvaluatedAtUtc);

        AssertContextParity(directContext, labEval.Context, backtestCapture, evidence);
        AssertContextMatchesExpectedFixture(labEval.Context, backtestCapture, evidence);

        Assert.NotEqual(SignalType.Entry, direct.SignalType);
        Assert.NotEqual(SignalType.Entry, labEval.Result.SignalType);
        Assert.NotEqual(SignalType.Entry, backtestResult.SignalType);

        AssertSignalResultsEqual(direct, labEval.Result, "direct", "lab");
        AssertDirectMatchesBacktest(direct, backtestResult);

        Assert.Equal(evidence.ExpectedRegime.ToString(), backtestResult.Regime);

        Assert.False(string.IsNullOrWhiteSpace(evidence.ExpectedLabRejectionCode));
        Assert.Equal(evidence.ExpectedLabRejectionCode, labEval.Result.Reason);
        Assert.Equal(evidence.ExpectedLabRejectionCode, direct.Reason);
        Assert.Equal(evidence.ExpectedLabRejectionCode, backtestResult.Reason);

        AssertRejectionFunnelLinkage(evidence, capturedLabEvaluationCount: 1);

        AssertFingerprintContract(
            evidence.Fingerprint,
            direct.RawDataJson,
            labEval.Result.RawDataJson,
            backtestResult.RawDataJson,
            candidateFingerprint: null);

        AssertRawDataEvidence(evidence.RawDataContract, evidence.RequiredRawDataJsonProperties,
            direct.RawDataJson, labEval.Result.RawDataJson, backtestResult.RawDataJson);
    }

    private static void AssertRejectionFunnelLinkage(RejectionThreePathEvidence evidence, int capturedLabEvaluationCount)
    {
        var resultSummaryJson = evidence.LabResultSummaryJson;
        if (evidence.PersistedLabRun is { } persistedRun)
        {
            Assert.Equal(evidence.ExpectedStrategyLabRunId, persistedRun.Id);
            Assert.Equal(evidence.ExpectedStrategyCode.ToCode(), persistedRun.StrategyCode);
            Assert.False(string.IsNullOrWhiteSpace(persistedRun.ResultSummaryJson));
            resultSummaryJson = persistedRun.ResultSummaryJson;
        }

        Assert.False(string.IsNullOrWhiteSpace(resultSummaryJson));
        using var doc = JsonDocument.Parse(resultSummaryJson);
        Assert.True(
            doc.RootElement.TryGetProperty("rejectionFunnel", out var rejectionFunnel),
            "Lab result summary must contain rejectionFunnel.");
        Assert.True(
            rejectionFunnel.TryGetProperty("counts", out var funnel),
            "rejectionFunnel.counts is required.");
        Assert.True(
            funnel.TryGetProperty(evidence.ExpectedLabRejectionCode, out var countEl),
            $"Expected lab rejection funnel code '{evidence.ExpectedLabRejectionCode}'.");
        Assert.Equal(evidence.ExpectedFunnelCount, countEl.GetInt32());

        Assert.True(
            rejectionFunnel.TryGetProperty("evaluations", out var evaluationsEl),
            "rejectionFunnel.evaluations is required for captured-rejection linkage.");
        Assert.Equal(capturedLabEvaluationCount, evaluationsEl.GetInt32());

        Assert.True(
            rejectionFunnel.TryGetProperty("entryConfirmed", out var entryConfirmedEl),
            "rejectionFunnel.entryConfirmed is required.");
        Assert.Equal(0, entryConfirmedEl.GetInt32());
    }

    public static decimal ExtractStrengthForTest(string json)
    {
        var strength = RequireStrength(json, "test");
        return strength;
    }

    private static void AssertContextParity(
        StrategyContext directContext,
        StrategyContext labContext,
        StrategyEvaluationCaptureRecord backtestCapture,
        PositiveThreePathEvidence evidence) =>
        AssertContextParityCore(directContext, labContext, backtestCapture);

    private static void AssertContextParity(
        StrategyContext directContext,
        StrategyContext labContext,
        StrategyEvaluationCaptureRecord backtestCapture,
        RejectionThreePathEvidence evidence) =>
        AssertContextParityCore(directContext, labContext, backtestCapture);

    private static void AssertContextParityCore(
        StrategyContext directContext,
        StrategyContext labContext,
        StrategyEvaluationCaptureRecord backtestCapture)
    {
        Assert.NotEmpty(labContext.Candles);
        Assert.NotEmpty(backtestCapture.Candles);
        Assert.NotEmpty(directContext.Candles);

        Assert.Equal(labContext.ExchangeId, directContext.ExchangeId);
        Assert.Equal(labContext.SymbolId, directContext.SymbolId);
        Assert.Equal(labContext.Symbol, directContext.Symbol);
        Assert.Equal(labContext.Timeframe, directContext.Timeframe);
        Assert.Equal(labContext.HigherTimeframe, directContext.HigherTimeframe);
        Assert.Equal(labContext.MarketRegime, directContext.MarketRegime);
        Assert.Equal(labContext.EvaluatedAtUtc, directContext.EvaluatedAtUtc);
        Assert.Equal(labContext.CurrentCandleIndex, directContext.CurrentCandleIndex);

        Assert.Equal(backtestCapture.ExchangeId, labContext.ExchangeId);
        Assert.Equal(backtestCapture.SymbolId, labContext.SymbolId);
        Assert.Equal(backtestCapture.Symbol, labContext.Symbol);
        Assert.Equal(backtestCapture.ExecutionTimeframe, labContext.Timeframe);
        Assert.Equal(backtestCapture.HigherTimeframe, labContext.HigherTimeframe);
        Assert.Equal(backtestCapture.MarketRegime, labContext.MarketRegime);
        Assert.Equal(backtestCapture.EvaluatedAtUtc, labContext.EvaluatedAtUtc);
        Assert.Equal(backtestCapture.CurrentCandleIndex, labContext.CurrentCandleIndex);

        AssertCandlesEqual(labContext.Candles, backtestCapture.Candles, "lab vs backtest LTF");
        AssertCandlesEqual(directContext.Candles, labContext.Candles, "direct vs lab LTF");
        AssertCandlesEqual(
            labContext.HigherTimeframeCandles,
            backtestCapture.HigherTimeframeCandles,
            "lab vs backtest HTF");
        AssertCandlesEqual(
            directContext.HigherTimeframeCandles,
            labContext.HigherTimeframeCandles,
            "direct vs lab HTF");

        AssertParametersEqual(labContext.StrategyParameters, backtestCapture.StrategyParameters);
        AssertParametersEqual(labContext.StrategyParameters, directContext.StrategyParameters);
        AssertIndicatorSnapshotIdentity(labContext.IndicatorSnapshot, backtestCapture.IndicatorSnapshot);
        AssertIndicatorSnapshotIdentity(labContext.IndicatorSnapshot, directContext.IndicatorSnapshot);
    }

    private static void AssertCandlesEqual(
        IReadOnlyList<Candle> left,
        IReadOnlyList<Candle> right,
        string label)
    {
        Assert.Equal(left.Count, right.Count);
        for (var i = 0; i < left.Count; i++)
        {
            AssertCandleContentEqual(left[i], right[i], $"{label}[{i}]");
        }
    }

    private static void AssertCandlesEqual(
        IReadOnlyList<Candle> left,
        IReadOnlyList<StrategyEvaluationCandleSnapshot> right,
        string label)
    {
        Assert.Equal(left.Count, right.Count);
        for (var i = 0; i < left.Count; i++)
        {
            AssertCandleContentEqual(left[i], right[i], $"{label}[{i}]");
        }
    }

    private static void AssertCandleContentEqual(Candle left, Candle right, string label)
    {
        Assert.Equal(left.Id, right.Id);
        Assert.Equal(left.ExchangeId, right.ExchangeId);
        Assert.Equal(left.SymbolId, right.SymbolId);
        Assert.Equal(left.Timeframe, right.Timeframe);
        Assert.Equal(left.OpenTimeUtc, right.OpenTimeUtc);
        Assert.Equal(left.CloseTimeUtc, right.CloseTimeUtc);
        Assert.Equal(left.Open, right.Open);
        Assert.Equal(left.High, right.High);
        Assert.Equal(left.Low, right.Low);
        Assert.Equal(left.Close, right.Close);
        Assert.Equal(left.Volume, right.Volume);
        Assert.Equal(left.QuoteVolume, right.QuoteVolume);
        Assert.Equal(left.TradeCount, right.TradeCount);
        Assert.Equal(left.IsClosed, right.IsClosed);
        Assert.Equal(left.CreatedAtUtc, right.CreatedAtUtc);
        _ = label;
    }

    private static void AssertCandleContentEqual(
        Candle left,
        StrategyEvaluationCandleSnapshot right,
        string label)
    {
        Assert.Equal(left.Id, right.Id);
        Assert.Equal(left.ExchangeId, right.ExchangeId);
        Assert.Equal(left.SymbolId, right.SymbolId);
        Assert.Equal(left.Timeframe, right.Timeframe);
        Assert.Equal(left.OpenTimeUtc, right.OpenTimeUtc);
        Assert.Equal(left.CloseTimeUtc, right.CloseTimeUtc);
        Assert.Equal(left.Open, right.Open);
        Assert.Equal(left.High, right.High);
        Assert.Equal(left.Low, right.Low);
        Assert.Equal(left.Close, right.Close);
        Assert.Equal(left.Volume, right.Volume);
        Assert.Equal(left.QuoteVolume, right.QuoteVolume);
        Assert.Equal(left.TradeCount, right.TradeCount);
        Assert.Equal(left.IsClosed, right.IsClosed);
        Assert.Equal(left.CreatedAtUtc, right.CreatedAtUtc);
        _ = label;
    }

    private static void AssertContextMatchesExpectedFixture(
        StrategyContext labContext,
        StrategyEvaluationCaptureRecord backtestCapture,
        PositiveThreePathEvidence evidence)
    {
        AssertExpectedContextFields(
            labContext,
            backtestCapture,
            evidence.ExpectedExchangeId,
            evidence.ExpectedSymbolId,
            evidence.ExpectedSymbol,
            evidence.ExpectedTimeframe,
            evidence.ExpectedHigherTimeframe,
            evidence.ExpectedRegime,
            evidence.ExpectedEvaluationTimestamp,
            evidence.ExpectedCurrentCandleIndex,
            evidence.ExpectedExecutionCandleIds,
            evidence.ExpectedHtfCandleIds,
            evidence.ExpectedParameters,
            evidence.ExpectedIndicatorSnapshot);
    }

    private static void AssertContextMatchesExpectedFixture(
        StrategyContext labContext,
        StrategyEvaluationCaptureRecord backtestCapture,
        RejectionThreePathEvidence evidence)
    {
        AssertExpectedContextFields(
            labContext,
            backtestCapture,
            evidence.ExpectedExchangeId,
            evidence.ExpectedSymbolId,
            evidence.ExpectedSymbol,
            evidence.ExpectedTimeframe,
            evidence.ExpectedHigherTimeframe,
            evidence.ExpectedRegime,
            evidence.ExpectedEvaluationTimestamp,
            evidence.ExpectedCurrentCandleIndex,
            evidence.ExpectedExecutionCandleIds,
            evidence.ExpectedHtfCandleIds,
            evidence.ExpectedParameters,
            evidence.ExpectedIndicatorSnapshot);
    }

    private static void AssertExpectedContextFields(
        StrategyContext labContext,
        StrategyEvaluationCaptureRecord backtestCapture,
        long expectedExchangeId,
        long expectedSymbolId,
        string expectedSymbol,
        Timeframe expectedTimeframe,
        Timeframe expectedHigherTimeframe,
        MarketRegime expectedRegime,
        DateTime expectedEvaluationTimestamp,
        int expectedCurrentCandleIndex,
        long[] expectedExecutionCandleIds,
        long[] expectedHtfCandleIds,
        IReadOnlyDictionary<string, string> expectedParameters,
        IndicatorSnapshot? expectedIndicatorSnapshot)
    {
        Assert.Equal(expectedExchangeId, labContext.ExchangeId);
        Assert.Equal(expectedExchangeId, backtestCapture.ExchangeId);

        Assert.Equal(expectedSymbolId, labContext.SymbolId);
        Assert.Equal(expectedSymbolId, backtestCapture.SymbolId);
        Assert.Equal(expectedSymbol, labContext.Symbol);
        Assert.Equal(expectedSymbol, backtestCapture.Symbol);

        Assert.Equal(expectedTimeframe, labContext.Timeframe);
        Assert.Equal(expectedTimeframe, backtestCapture.ExecutionTimeframe);

        Assert.Equal(expectedHigherTimeframe, labContext.HigherTimeframe);
        Assert.Equal(expectedHigherTimeframe, backtestCapture.HigherTimeframe);

        Assert.Equal(expectedRegime, labContext.MarketRegime);
        Assert.Equal(expectedRegime, backtestCapture.MarketRegime);

        Assert.Equal(expectedEvaluationTimestamp, labContext.EvaluatedAtUtc);
        Assert.Equal(expectedEvaluationTimestamp, backtestCapture.EvaluatedAtUtc);

        Assert.Equal(expectedCurrentCandleIndex, labContext.CurrentCandleIndex);
        Assert.Equal(expectedCurrentCandleIndex, backtestCapture.CurrentCandleIndex);

        Assert.Equal(expectedExecutionCandleIds, labContext.Candles.Select(c => c.Id).ToArray());
        Assert.Equal(expectedExecutionCandleIds, backtestCapture.Candles.Select(c => c.Id).ToArray());

        Assert.Equal(expectedHtfCandleIds, labContext.HigherTimeframeCandles.Select(c => c.Id).ToArray());
        Assert.Equal(expectedHtfCandleIds, backtestCapture.HigherTimeframeCandles.Select(c => c.Id).ToArray());

        AssertParametersEqual(expectedParameters, labContext.StrategyParameters);
        AssertParametersEqual(expectedParameters, backtestCapture.StrategyParameters);
        AssertIndicatorSnapshotIdentity(expectedIndicatorSnapshot, labContext.IndicatorSnapshot);
        AssertIndicatorSnapshotIdentity(expectedIndicatorSnapshot, backtestCapture.IndicatorSnapshot);
    }

    private static void AssertCandidateParity(
        (StrategyContext Context, StrategySignalResult Result) labEval,
        StrategyResearchCandidate labCandidate,
        StrategySignalResult direct,
        PositiveThreePathEvidence evidence)
    {
        Assert.Equal(evidence.ExpectedStrategyLabRunId, labCandidate.StrategyLabRunId);
        Assert.Equal(evidence.ExpectedStrategyCode.ToCode(), labCandidate.StrategyCode);
        Assert.Equal(evidence.ExpectedStrategyVersion, labCandidate.StrategyVersion);
        Assert.Equal(evidence.ExpectedExchangeId, labCandidate.ExchangeId);
        Assert.Equal(evidence.ExpectedSymbolId, labCandidate.SymbolId);
        Assert.Equal(evidence.ExpectedSymbol, labCandidate.Symbol);
        Assert.Equal(evidence.ExpectedTimeframeApi, labCandidate.Timeframe);

        Assert.Equal(labEval.Result.Direction, labCandidate.Direction);
        Assert.Equal(labEval.Context.EvaluatedAtUtc, labCandidate.SetupDetectedAtUtc);
        Assert.Equal(labEval.Context.EvaluatedAtUtc, labCandidate.ProposedEntryTimeUtc);
        Assert.Equal(labEval.Result.EntryPrice, labCandidate.ProposedEntryPrice);
        Assert.Equal(labEval.Result.SuggestedStopLoss, labCandidate.StopLoss);
        Assert.Equal(labEval.Result.SuggestedTakeProfit, labCandidate.Target1);
        Assert.Null(labCandidate.Target2);
        Assert.Equal(labEval.Result.Reason, labCandidate.StrategyReason);
        Assert.Equal(evidence.ExpectedCandidateStatus, labCandidate.CandidateStatus);

        var risk = labCandidate.Direction == TradeDirection.Long
            ? labCandidate.ProposedEntryPrice - labCandidate.StopLoss
            : labCandidate.StopLoss - labCandidate.ProposedEntryPrice;
        var expectedRr = risk > 0
            ? Math.Abs((labCandidate.Target1 - labCandidate.ProposedEntryPrice) / risk)
            : 0m;
        Assert.Equal(expectedRr, labCandidate.RewardRisk);

        AssertParametersJsonMatchesContext(labCandidate.ParametersJson, labEval.Context.StrategyParameters);

        switch (evidence.Fingerprint)
        {
            case FingerprintContract.RequiredPresent present:
                Assert.Equal(present.ExpectedValue, labCandidate.SetupFingerprint);
                break;
            case FingerprintContract.RequiredAbsent:
                Assert.True(string.IsNullOrWhiteSpace(labCandidate.SetupFingerprint));
                break;
        }

        Assert.Equal(direct.Strength, ParityAssertionHelper.ExtractStrengthForTest(labCandidate.StructureJson));
        Assert.Equal(
            Milestone231BParityFixtures.ExtractStrengthBreakdown(direct.RawDataJson),
            Milestone231BParityFixtures.ExtractStrengthBreakdown(labCandidate.StructureJson));
    }

    private static void AssertParametersJsonMatchesContext(
        string parametersJson,
        IReadOnlyDictionary<string, string> contextParameters)
    {
        Assert.False(string.IsNullOrWhiteSpace(parametersJson));
        using var doc = JsonDocument.Parse(parametersJson);
        var parsed = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in doc.RootElement.EnumerateObject())
        {
            parsed[property.Name] = property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString() ?? string.Empty,
                JsonValueKind.Number => property.Value.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => property.Value.GetRawText()
            };
        }

        Assert.False(
            parsed.ContainsKey("__seenFingerprints"),
            "Persisted candidate parameters must explicitly exclude evaluator-local __seenFingerprints state.");
        var expected = contextParameters
            .Where(kvp => !string.Equals(kvp.Key, "__seenFingerprints", StringComparison.Ordinal))
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.Ordinal);
        var actual = parsed;

        AssertParametersEqual(expected, actual);
    }

    private static void AssertFingerprintContract(
        FingerprintContract contract,
        string? directJson,
        string? labJson,
        string? backtestJson,
        string? candidateFingerprint)
    {
        switch (contract)
        {
            case FingerprintContract.RequiredPresent present:
                var directFingerprint = RequireFingerprint(directJson, "direct");
                var labFingerprint = RequireFingerprint(labJson, "lab");
                var backtestFingerprint = RequireFingerprint(backtestJson, "backtest");
                Assert.Equal(present.ExpectedValue, directFingerprint);
                Assert.Equal(present.ExpectedValue, labFingerprint);
                Assert.Equal(present.ExpectedValue, backtestFingerprint);
                if (candidateFingerprint is not null)
                {
                    Assert.Equal(present.ExpectedValue, candidateFingerprint);
                }

                break;
            case FingerprintContract.RequiredAbsent:
                AssertFingerprintAbsent(directJson, "direct");
                AssertFingerprintAbsent(labJson, "lab");
                AssertFingerprintAbsent(backtestJson, "backtest");
                if (candidateFingerprint is not null)
                {
                    Assert.True(
                        string.IsNullOrWhiteSpace(candidateFingerprint),
                        "candidate must not include setupFingerprint when contract requires absence.");
                }

                break;
        }
    }

    private static void AssertFingerprintAbsent(string? rawDataJson, string sourceLabel)
    {
        if (string.IsNullOrWhiteSpace(rawDataJson))
        {
            return;
        }

        using var doc = JsonDocument.Parse(rawDataJson);
        if (TryGetPropertyIgnoreCase(doc.RootElement, "setupFingerprint", out var fp)
            || TryGetPropertyIgnoreCase(doc.RootElement, "SetupFingerprint", out fp))
        {
            Assert.True(
                fp.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
                || (fp.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(fp.GetString())),
                $"{sourceLabel} must not include a non-empty setupFingerprint when contract requires absence.");
        }
    }

    private static void AssertRequiredJsonProperties(
        IReadOnlyList<string> requiredProperties,
        params string?[] jsonSources)
    {
        foreach (var property in requiredProperties)
        {
            foreach (var json in jsonSources)
            {
                Assert.True(
                    HasJsonProperty(json, property),
                    $"Required JSON property '{property}' is missing.");
            }
        }
    }

    private static bool HasJsonProperty(string? json, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        if (string.Equals(propertyName, "strengthBreakdown", StringComparison.OrdinalIgnoreCase))
        {
            return Milestone231BParityFixtures.HasStrengthBreakdown(json);
        }

        if (string.Equals(propertyName, "setupFingerprint", StringComparison.OrdinalIgnoreCase))
        {
            return !string.IsNullOrWhiteSpace(Milestone231BParityFixtures.ExtractFingerprint(json));
        }

        if (string.Equals(propertyName, "strength", StringComparison.OrdinalIgnoreCase))
        {
            using var strengthDoc = JsonDocument.Parse(json);
            if (TryExtractStrengthFromElement(strengthDoc.RootElement, out _))
            {
                return true;
            }

            return strengthDoc.RootElement.TryGetProperty("diagnostics", out var strengthDiagnostics)
                   && TryExtractStrengthFromElement(strengthDiagnostics, out _);
        }

        using var doc = JsonDocument.Parse(json);
        if (TryGetPropertyIgnoreCase(doc.RootElement, propertyName, out _))
        {
            return true;
        }

        return doc.RootElement.TryGetProperty("diagnostics", out var diagnostics)
               && TryGetPropertyIgnoreCase(diagnostics, propertyName, out _);
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
        AssertJsonSemanticallyEqual(left.RawDataJson, right.RawDataJson, leftLabel, rightLabel);
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
        AssertJsonSemanticallyEqual(direct.RawDataJson, backtest.RawDataJson, "direct", "backtest");
    }

    private static void AssertJsonSemanticallyEqual(
        string? left,
        string? right,
        string leftLabel,
        string rightLabel)
    {
        if (string.IsNullOrWhiteSpace(left) && string.IsNullOrWhiteSpace(right))
        {
            return;
        }

        Assert.False(string.IsNullOrWhiteSpace(left), $"{leftLabel} RawDataJson is required when {rightLabel} has JSON.");
        Assert.False(string.IsNullOrWhiteSpace(right), $"{rightLabel} RawDataJson is required when {leftLabel} has JSON.");
        Assert.True(
            JsonNode.DeepEquals(JsonNode.Parse(left!), JsonNode.Parse(right!)),
            $"{leftLabel} and {rightLabel} RawDataJson must match semantically.");
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
        AssertIndicatorSnapshotFields(expected, actual.Id, actual.SymbolId, actual.Timeframe, actual.CandleId,
            actual.CalculatedAtUtc, actual.Ema20, actual.Ema50, actual.Ema200, actual.Vwap, actual.Rsi14,
            actual.Atr14, actual.VolumeSma20, actual.SwingHigh, actual.SwingLow, actual.MarketStructure,
            actual.BollingerMiddle20, actual.BollingerUpper20, actual.BollingerLower20, actual.BollingerBandwidth20,
            actual.DonchianHigh20, actual.DonchianLow20, actual.MacdLine, actual.MacdSignal, actual.MacdHistogram,
            actual.Supertrend, actual.SupertrendDirection, actual.SupportLevel, actual.ResistanceLevel, actual.CreatedAtUtc);
    }

    private static void AssertRawDataEvidence(
        RawDataJsonContract? contract,
        IReadOnlyList<string> legacyRequiredProperties,
        params string?[] jsonSources)
    {
        if (contract is null)
        {
            AssertRequiredJsonProperties(legacyRequiredProperties, jsonSources);
            return;
        }

        foreach (var json in jsonSources)
        {
            AssertRawDataJsonContract(contract, json);
        }
    }

    public static void AssertRawDataJsonContract(RawDataJsonContract contract, string? actualJson)
    {
        ArgumentNullException.ThrowIfNull(contract);
        if (contract.RootState == RawDataJsonRootState.Null)
        {
            Assert.Null(actualJson);
            return;
        }

        Assert.NotNull(actualJson);
        Assert.False(string.IsNullOrEmpty(actualJson));
        Assert.False(string.IsNullOrWhiteSpace(actualJson));

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(actualJson!);
        }
        catch (JsonException ex)
        {
            Assert.Fail($"RawDataJson must be a valid JSON object: {ex.Message}");
            return;
        }

        using (document)
        {
            var root = document.RootElement;
            Assert.Equal(JsonValueKind.Object, root.ValueKind);
            var actualProperties = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var property in root.EnumerateObject())
            {
                Assert.True(actualProperties.TryAdd(property.Name, property.Value),
                    $"RawDataJson contains duplicate property '{property.Name}'.");
            }

            Assert.Equal(
                contract.Properties.Count(item => item.Value.State != JsonPropertyState.Absent),
                actualProperties.Count);
            foreach (var (name, expectation) in contract.Properties)
            {
                var exists = actualProperties.TryGetValue(name, out var actual);
                AssertJsonPropertyState(name, expectation, exists, actual);
            }
        }
    }

    private static void AssertJsonPropertyState(
        string propertyName,
        JsonPropertyExpectation expectation,
        bool exists,
        JsonElement actual)
    {
        if (expectation.State == JsonPropertyState.Absent)
        {
            Assert.False(exists, $"Property '{propertyName}' must be absent.");
            return;
        }

        Assert.True(exists, $"Required property '{propertyName}' is missing.");
        switch (expectation.State)
        {
            case JsonPropertyState.ExplicitNull:
                Assert.Equal(JsonValueKind.Null, actual.ValueKind);
                break;
            case JsonPropertyState.EmptyString:
                Assert.Equal(JsonValueKind.String, actual.ValueKind);
                Assert.Equal(string.Empty, actual.GetString());
                break;
            case JsonPropertyState.WhitespaceString:
                Assert.Equal(JsonValueKind.String, actual.ValueKind);
                Assert.Equal(expectation.StringValue, actual.GetString());
                Assert.True(string.IsNullOrWhiteSpace(expectation.StringValue));
                break;
            case JsonPropertyState.ExactString:
                Assert.Equal(JsonValueKind.String, actual.ValueKind);
                Assert.Equal(expectation.StringValue, actual.GetString());
                break;
            case JsonPropertyState.ExactNumber:
                Assert.Equal(JsonValueKind.Number, actual.ValueKind);
                Assert.Equal(expectation.NumberValue, actual.GetDecimal());
                break;
            case JsonPropertyState.ExactBoolean:
                Assert.True(actual.ValueKind is JsonValueKind.True or JsonValueKind.False);
                Assert.Equal(expectation.BooleanValue, actual.GetBoolean());
                break;
            case JsonPropertyState.ExactJsonValue:
                Assert.False(string.IsNullOrWhiteSpace(expectation.CanonicalJsonValue));
                Assert.True(JsonNode.DeepEquals(JsonNode.Parse(expectation.CanonicalJsonValue), JsonNode.Parse(actual.GetRawText())),
                    $"Property '{propertyName}' differs from its canonical JSON value.");
                break;
            default:
                Assert.Fail($"Unsupported property state '{expectation.State}'.");
                break;
        }
    }

    private static void AssertIndicatorSnapshotIdentity(
        IndicatorSnapshot? expected,
        StrategyEvaluationIndicatorSnapshot? actual)
    {
        if (expected is null)
        {
            Assert.Null(actual);
            return;
        }

        Assert.NotNull(actual);
        AssertIndicatorSnapshotFields(expected, actual.Id, actual.SymbolId, actual.Timeframe, actual.CandleId,
            actual.CalculatedAtUtc, actual.Ema20, actual.Ema50, actual.Ema200, actual.Vwap, actual.Rsi14,
            actual.Atr14, actual.VolumeSma20, actual.SwingHigh, actual.SwingLow, actual.MarketStructure,
            actual.BollingerMiddle20, actual.BollingerUpper20, actual.BollingerLower20, actual.BollingerBandwidth20,
            actual.DonchianHigh20, actual.DonchianLow20, actual.MacdLine, actual.MacdSignal, actual.MacdHistogram,
            actual.Supertrend, actual.SupertrendDirection, actual.SupportLevel, actual.ResistanceLevel, actual.CreatedAtUtc);
    }

    private static void AssertIndicatorSnapshotFields(
        IndicatorSnapshot expected,
        long actualId,
        long actualSymbolId,
        Timeframe actualTimeframe,
        long actualCandleId,
        DateTime actualCalculatedAtUtc,
        decimal? actualEma20,
        decimal? actualEma50,
        decimal? actualEma200,
        decimal? actualVwap,
        decimal? actualRsi14,
        decimal? actualAtr14,
        decimal? actualVolumeSma20,
        decimal? actualSwingHigh,
        decimal? actualSwingLow,
        MarketStructure actualMarketStructure,
        decimal? actualBollingerMiddle20,
        decimal? actualBollingerUpper20,
        decimal? actualBollingerLower20,
        decimal? actualBollingerBandwidth20,
        decimal? actualDonchianHigh20,
        decimal? actualDonchianLow20,
        decimal? actualMacdLine,
        decimal? actualMacdSignal,
        decimal? actualMacdHistogram,
        decimal? actualSupertrend,
        int? actualSupertrendDirection,
        decimal? actualSupportLevel,
        decimal? actualResistanceLevel,
        DateTime actualCreatedAtUtc)
    {
        Assert.Equal(expected.Id, actualId);
        Assert.Equal(expected.SymbolId, actualSymbolId);
        Assert.Equal(expected.Timeframe, actualTimeframe);
        Assert.Equal(expected.CandleId, actualCandleId);
        Assert.Equal(expected.CalculatedAtUtc, actualCalculatedAtUtc);
        Assert.Equal(expected.Ema20, actualEma20);
        Assert.Equal(expected.Ema50, actualEma50);
        Assert.Equal(expected.Ema200, actualEma200);
        Assert.Equal(expected.Vwap, actualVwap);
        Assert.Equal(expected.Rsi14, actualRsi14);
        Assert.Equal(expected.Atr14, actualAtr14);
        Assert.Equal(expected.VolumeSma20, actualVolumeSma20);
        Assert.Equal(expected.SwingHigh, actualSwingHigh);
        Assert.Equal(expected.SwingLow, actualSwingLow);
        Assert.Equal(expected.MarketStructure, actualMarketStructure);
        Assert.Equal(expected.BollingerMiddle20, actualBollingerMiddle20);
        Assert.Equal(expected.BollingerUpper20, actualBollingerUpper20);
        Assert.Equal(expected.BollingerLower20, actualBollingerLower20);
        Assert.Equal(expected.BollingerBandwidth20, actualBollingerBandwidth20);
        Assert.Equal(expected.DonchianHigh20, actualDonchianHigh20);
        Assert.Equal(expected.DonchianLow20, actualDonchianLow20);
        Assert.Equal(expected.MacdLine, actualMacdLine);
        Assert.Equal(expected.MacdSignal, actualMacdSignal);
        Assert.Equal(expected.MacdHistogram, actualMacdHistogram);
        Assert.Equal(expected.Supertrend, actualSupertrend);
        Assert.Equal(expected.SupertrendDirection, actualSupertrendDirection);
        Assert.Equal(expected.SupportLevel, actualSupportLevel);
        Assert.Equal(expected.ResistanceLevel, actualResistanceLevel);
        Assert.Equal(expected.CreatedAtUtc, actualCreatedAtUtc);
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
