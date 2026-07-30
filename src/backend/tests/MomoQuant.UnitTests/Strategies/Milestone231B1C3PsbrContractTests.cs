using System.Text.Json;
using System.Text.Json.Nodes;
using MomoQuant.Application.Strategies;
using MomoQuant.Domain.Constants;
using MomoQuant.Application.Strategies.Models;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.MarketData;
using MomoQuant.Domain.Strategies;
using MomoQuant.Domain.StrategyLab;

namespace MomoQuant.UnitTests.Strategies;

/// <summary>Milestone 23.1B1C3 — PSBR evidence is immutable and independent of executed output.</summary>
public sealed class Milestone231B1C3PsbrContractTests
{
    [Fact]
    public void PsbrContracts_AreConstructedBeforeDirectLabAndBacktestExecution()
    {
        var positive = ReadSource("Milestone231BParityTests.cs");
        var positiveStart = positive.IndexOf("CrossPath_Psbr_DirectLabBacktest_IdenticalAtSameT", StringComparison.Ordinal);
        Assert.True(positiveStart >= 0);
        var positiveContract = positive.IndexOf("CreatePsbrPositiveEvidence(candles)", positiveStart, StringComparison.Ordinal);
        Assert.True(positiveContract > positiveStart);
        Assert.True(positiveContract < positive.IndexOf("var direct = plugin.Evaluate(directContext);", positiveStart, StringComparison.Ordinal));
        Assert.True(positiveContract < positive.IndexOf("await runner.ExecuteAsync(run.Id", positiveStart, StringComparison.Ordinal));
        Assert.True(positiveContract < positive.IndexOf("await engine.ProcessCandleAtIndexAsync(", positiveStart, StringComparison.Ordinal));

        var rejection = ReadSource("Milestone231B1ATests.cs");
        var rejectionStart = rejection.IndexOf("B1A_RejectionParity_Psbr_NoCandidate", StringComparison.Ordinal);
        Assert.True(rejectionStart >= 0);
        var rejectionContract = rejection.IndexOf("RawDataJsonRootState.Null", rejectionStart, StringComparison.Ordinal);
        Assert.True(rejectionContract > rejectionStart);
        Assert.True(rejectionContract < rejection.IndexOf("var direct = plugin.Evaluate(directContext);", rejectionStart, StringComparison.Ordinal));
        Assert.True(rejectionContract < rejection.IndexOf(".ExecuteAsync(run.Id", rejectionStart, StringComparison.Ordinal));
        Assert.True(rejectionContract < rejection.IndexOf("await engine.ProcessCandleAtIndexAsync(", rejectionStart, StringComparison.Ordinal));
    }

    [Fact]
    public void PsbrContracts_UseFinalHelperWithoutPropertyPresenceFallbacks()
    {
        var positive = ReadSource("Milestone231BParityTests.cs");
        var rejection = ReadSource("Milestone231B1ATests.cs");
        var positiveStart = positive.IndexOf("CrossPath_Psbr_DirectLabBacktest_IdenticalAtSameT", StringComparison.Ordinal);
        var rejectionStart = rejection.IndexOf("B1A_RejectionParity_Psbr_NoCandidate", StringComparison.Ordinal);

        Assert.Contains("ParityAssertionHelper.AssertPositiveThreePathParity(", positive[positiveStart..], StringComparison.Ordinal);
        Assert.Contains("RawDataContract = psbrEvidence.RawDataContract", positive[positiveStart..], StringComparison.Ordinal);
        Assert.Contains("OutcomeContract = psbrEvidence.OutcomeContract", positive[positiveStart..], StringComparison.Ordinal);
        Assert.DoesNotContain("PsbrPositiveRawData", positive, StringComparison.Ordinal);
        Assert.DoesNotContain("PsbrPositiveStructure", positive, StringComparison.Ordinal);

        Assert.Contains("ParityAssertionHelper.AssertRejectionThreePathParity(", rejection[rejectionStart..], StringComparison.Ordinal);
        Assert.Contains("RawDataContract = rawDataContract", rejection[rejectionStart..], StringComparison.Ordinal);
        Assert.DoesNotContain("PsbrRejectionRawData", rejection, StringComparison.Ordinal);
    }

    [Fact]
    public void PsbrContracts_AreImmutableAndRejectWrongStructureBreakdownOrCandidateValue()
    {
        var evidence = ParityEvidenceContracts.CreatePsbrPositiveEvidence(Milestone231BParityFixtures.BuildPsbrLongScenario());
        var contract = evidence.RawDataContract;
        var expectedRootNames = new[] { "setupFingerprint", "structure", "version", "strengthBreakdown" };
        var expectedStructureNames = new[]
        {
            "setupType", "direction", "brokenOrSweptLevel", "swingTimeUtc", "breakoutOrSweepTimeUtc",
            "retestOrReclaimTimeUtc", "confirmationTimeUtc", "swingIndex", "breakoutIndex", "retestIndex", "confirmationIndex"
        };
        var expectedStrengthNames = new[]
        {
            "total", "breakoutDistance", "retestQuality", "confirmationQuality", "rewardRiskValidity"
        };

        Assert.Equal(ParityAssertionHelper.RawDataJsonRootState.PresentJsonObject, contract.RootState);
        Assert.Equal(expectedRootNames.OrderBy(name => name), contract.Properties.Keys.OrderBy(name => name));
        Assert.Equal(ParityEvidenceContracts.PsbrPositiveFingerprint, contract.Properties["setupFingerprint"].StringValue);
        Assert.Equal(expectedStructureNames.OrderBy(name => name), PropertyNames(contract.Properties["structure"].CanonicalJsonValue!).OrderBy(name => name));
        Assert.Equal(expectedStrengthNames.OrderBy(name => name), PropertyNames(contract.Properties["strengthBreakdown"].CanonicalJsonValue!).OrderBy(name => name));

        var replacement = contract.Properties.SetItem("version", ParityAssertionHelper.JsonPropertyExpectation.String("wrong"));
        Assert.Equal("1.1.0", contract.Properties["version"].StringValue);
        Assert.Equal("wrong", replacement["version"].StringValue);

        ParityAssertionHelper.AssertRawDataJsonContract(contract, BuildPositiveRawData(contract));

        var wrongStructure = JsonNode.Parse(contract.Properties["structure"].CanonicalJsonValue!)!.AsObject();
        wrongStructure["brokenOrSweptLevel"] = 99;
        Assert.ThrowsAny<Exception>(() => ParityAssertionHelper.AssertRawDataJsonContract(
            contract, BuildPositiveRawData(contract, wrongStructure.ToJsonString())));
        Assert.ThrowsAny<Exception>(() => AssertPositiveParityThroughFinalHelper(
            evidence, 0m, BuildPositiveRawData(contract, wrongStructure.ToJsonString())));

        var wrongBreakdown = JsonNode.Parse(contract.Properties["strengthBreakdown"].CanonicalJsonValue!)!.AsObject();
        wrongBreakdown["retestQuality"] = 0;
        Assert.ThrowsAny<Exception>(() => ParityAssertionHelper.AssertRawDataJsonContract(
            contract, BuildPositiveRawData(contract, strengthBreakdown: wrongBreakdown.ToJsonString())));

        // Candidate StructureJson is the production RawDataJson payload, so the same final contract
        // path rejects a wrong persisted-candidate structure value too.
        Assert.ThrowsAny<Exception>(() => AssertPositiveParityThroughFinalHelper(
            evidence, 0m, BuildPositiveRawData(contract), BuildPositiveRawData(contract, wrongStructure.ToJsonString())));
    }

    [Fact]
    public void PsbrContracts_RejectWrongPositiveOutcomeThroughFinalHelper()
    {
        var positive = ParityEvidenceContracts.CreatePsbrPositiveEvidence(Milestone231BParityFixtures.BuildPsbrLongScenario());
        Assert.ThrowsAny<Exception>(() => AssertPositiveParityThroughFinalHelper(
            positive, 1m, BuildPositiveRawData(positive.RawDataContract)));
    }

    [Fact]
    public void PsbrContracts_RejectNonNullRejectionEvidence()
    {
        var rejection = ParityAssertionHelper.RawDataJsonContract.Create(ParityAssertionHelper.RawDataJsonRootState.Null);
        ParityAssertionHelper.AssertRawDataJsonContract(rejection, null);
        foreach (var substitute in new[] { "", " ", "null", "{}", "[]", "\"value\"", "1", "true", "{\"invented\":true}" })
        {
            Assert.ThrowsAny<Exception>(() => ParityAssertionHelper.AssertRawDataJsonContract(rejection, substitute));
        }
    }

    private static void AssertPositiveParityThroughFinalHelper(
        ParityEvidenceContracts.PsbrPositiveEvidence expected,
        decimal entryOffset,
        string rawDataJson,
        string? candidateStructureJson = null)
    {
        var candles = Milestone231BParityFixtures.BuildPsbrLongScenario();
        var evaluationTimeUtc = candles[^1].CloseTimeUtc;
        var context = new StrategyContext
        {
            ExchangeId = 1,
            SymbolId = 1,
            Symbol = "BTCUSDT",
            Timeframe = Timeframe.M5,
            HigherTimeframe = Timeframe.H1,
            MarketRegime = MarketRegime.Unknown,
            Candles = candles,
            IndicatorSnapshot = null,
            StrategyParameters = new Dictionary<string, string> { ["__seenFingerprints"] = "[]" },
            EvaluatedAtUtc = evaluationTimeUtc,
            CurrentCandleIndex = candles.Count - 1
        };
        var wrong = new StrategySignalResult
        {
            SignalType = SignalType.Entry,
            Direction = TradeDirection.Long,
            EntryPrice = expected.OutcomeContract.EntryPrice + entryOffset,
            SuggestedStopLoss = expected.OutcomeContract.StopLoss,
            SuggestedTakeProfit = expected.OutcomeContract.TakeProfit,
            Strength = expected.OutcomeContract.Strength,
            ConfidenceContribution = 0m,
            Reason = expected.OutcomeContract.Reason,
            RawDataJson = rawDataJson
        };
        var backtest = new StrategyEvaluationResult
        {
            StrategyCode = StrategyCodes.PriceStructureBreakoutRetest,
            StrategyName = "PSBR",
            Evaluated = true,
            IsValid = true,
            Skipped = false,
            SignalType = wrong.SignalType,
            Direction = wrong.Direction,
            EntryPrice = wrong.EntryPrice,
            SuggestedStopLoss = wrong.SuggestedStopLoss,
            SuggestedTakeProfit = wrong.SuggestedTakeProfit,
            Strength = wrong.Strength,
            ConfidenceContribution = wrong.ConfidenceContribution,
            Reason = wrong.Reason,
            Regime = MarketRegime.Unknown.ToString(),
            RawDataJson = wrong.RawDataJson
        };
        var capture = new StrategyEvaluationCaptureRecord(
            StrategyCode.PriceStructureBreakoutRetest,
            evaluationTimeUtc,
            Timeframe.M5,
            Timeframe.H1,
            candles,
            Array.Empty<Candle>(),
            1,
            1,
            "BTCUSDT",
            MarketRegime.Unknown,
            candles.Count - 1,
            null,
            new Dictionary<string, string>(context.StrategyParameters));

        ParityAssertionHelper.AssertPositiveThreePathParity(
            context,
            wrong,
            backtest,
            new ParityAssertionHelper.PositiveThreePathEvidence
            {
                LabEvaluations = [(context, wrong)],
                BacktestCaptures = [capture],
                LabCandidates = [new StrategyResearchCandidate { StructureJson = candidateStructureJson ?? rawDataJson }],
                ExpectedStrategyCode = StrategyCode.PriceStructureBreakoutRetest,
                ExpectedStrategyVersion = "1.1.0",
                ExpectedStrategyLabRunId = 1,
                ExpectedCandidateStatus = StrategyResearchCandidateStatus.Detected,
                ExpectedRegime = MarketRegime.Unknown,
                ExpectedExchangeId = 1,
                ExpectedSymbolId = 1,
                ExpectedSymbol = "BTCUSDT",
                ExpectedTimeframe = Timeframe.M5,
                ExpectedTimeframeApi = "5m",
                ExpectedHigherTimeframe = Timeframe.H1,
                ExpectedEvaluationTimestamp = evaluationTimeUtc,
                ExpectedCurrentCandleIndex = candles.Count - 1,
                ExpectedExecutionCandleIds = candles.Select(candle => candle.Id).ToArray(),
                ExpectedHtfCandleIds = Array.Empty<long>(),
                ExpectedParameters = context.StrategyParameters,
                ExpectedIndicatorSnapshot = null,
                Fingerprint = ParityEvidenceContracts.PositiveFingerprint(ParityEvidenceContracts.PsbrPositiveFingerprint),
                RawDataContract = expected.RawDataContract,
                OutcomeContract = expected.OutcomeContract,
                RequiredRawDataJsonProperties = ["setupFingerprint", "structure", "version", "strengthBreakdown"],
                RequiredStructureJsonProperties = Array.Empty<string>()
            });
    }

    private static string BuildPositiveRawData(
        ParityAssertionHelper.RawDataJsonContract contract,
        string? structure = null,
        string? strengthBreakdown = null) => JsonSerializer.Serialize(new
        {
            setupFingerprint = contract.Properties["setupFingerprint"].StringValue,
            structure = JsonNode.Parse(structure ?? contract.Properties["structure"].CanonicalJsonValue!),
            version = contract.Properties["version"].StringValue,
            strengthBreakdown = JsonNode.Parse(strengthBreakdown ?? contract.Properties["strengthBreakdown"].CanonicalJsonValue!)
        });

    private static IReadOnlyList<string> PropertyNames(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.EnumerateObject().Select(property => property.Name).ToArray();
    }

    private static string ReadSource(string file) => File.ReadAllText(Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "Strategies", file)));
}
