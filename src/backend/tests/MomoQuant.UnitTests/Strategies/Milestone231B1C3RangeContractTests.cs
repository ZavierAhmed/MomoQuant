using System.Text.Json;

namespace MomoQuant.UnitTests.Strategies;

/// <summary>Milestone 23.1B1C3 â€” Range evidence is fixed before three-path execution.</summary>
public sealed class Milestone231B1C3RangeContractTests
{
    [Fact]
    public void RangeContracts_AreConstructedBeforeDirectLabAndBacktestExecution()
    {
        var positive = ReadSource("Milestone231BParityTests.cs");
        var positiveStart = positive.IndexOf("CrossPath_Range_DirectLabBacktest_IdenticalAtSameT", StringComparison.Ordinal);
        Assert.True(positive.IndexOf("CreateRangePositiveEvidence(candles)", positiveStart, StringComparison.Ordinal)
            < positive.IndexOf("var direct = plugin.Evaluate(directContext);", positiveStart, StringComparison.Ordinal));

        var rejection = ReadSource("Milestone231B1ATests.cs");
        var rejectionStart = rejection.IndexOf("B1A_RejectionParity_Range_NoCandidate", StringComparison.Ordinal);
        Assert.True(rejection.IndexOf("CreateRangeRejectionEnvelopeContract(", rejectionStart, StringComparison.Ordinal)
            < rejection.IndexOf("var direct = plugin.Evaluate(directContext);", rejectionStart, StringComparison.Ordinal));
    }

    [Fact]
    public void RangeContracts_UseFinalHelperWithoutPresenceOnlyFallbacks()
    {
        var positive = ReadSource("Milestone231BParityTests.cs");
        var rejection = ReadSource("Milestone231B1ATests.cs");
        Assert.Contains("RawDataContract = rangeEvidence.RawDataContract", positive, StringComparison.Ordinal);
        Assert.Contains("OutcomeContract = rangeEvidence.OutcomeContract", positive, StringComparison.Ordinal);
        Assert.Contains("AssertPositiveThreePathParity", positive, StringComparison.Ordinal);
        Assert.Contains("RawDataContract = rawDataContract", rejection, StringComparison.Ordinal);
        Assert.Contains("AssertRejectionThreePathParity", rejection, StringComparison.Ordinal);
        Assert.DoesNotContain("RangePositiveRawData", positive, StringComparison.Ordinal);
        Assert.DoesNotContain("RangeRejectionRawData", rejection, StringComparison.Ordinal);
    }

    [Fact]
    public void RangeContracts_AreImmutableAndRejectWrongNestedCandidateOrEnvelope()
    {
        var candles = MomoVolatilityRangeReversionFormulaTests.BuildValidLong().TakeLast(600).ToList();
        var positive = ParityEvidenceContracts.CreateRangePositiveEvidence(candles).RawDataContract;
        var replacement = positive.Properties.SetItem("version", ParityAssertionHelper.JsonPropertyExpectation.String("wrong"));
        Assert.Equal("1.0.0", positive.Properties["version"].StringValue);
        Assert.Equal("wrong", replacement["version"].StringValue);

        const string wrongCandidateDiagnostics =
            "{\"setupFingerprint\":\"43E14ED345E566C3\",\"version\":\"1.0.0\",\"diagnostics\":{\"version\":\"wrong\"}}";
        Assert.ThrowsAny<Exception>(() => ParityAssertionHelper.AssertRawDataJsonContract(positive, wrongCandidateDiagnostics));

        var rejection = ParityEvidenceContracts.CreateRangeRejectionEnvelopeContract(
            "MOMO_VOLATILITY_RANGE_REVERSION", "1.0.0", "TrendFilterFailed", 1, "ETHUSDT", "5m", "Trending",
            new DateTime(2024, 3, 3, 5, 55, 0, DateTimeKind.Utc));
        var wrongEnvelope = JsonSerializer.Serialize(new
        {
            strategyCode = "MOMO_VOLATILITY_RANGE_REVERSION", version = "1.0.0", reason = "TrendFilterFailed",
            symbolId = 1, symbol = "ETHUSDT", timeframe = "5m", marketRegime = "Ranging",
            evaluatedAtUtc = new DateTime(2024, 3, 3, 6, 0, 0, DateTimeKind.Utc)
        });
        Assert.ThrowsAny<Exception>(() => ParityAssertionHelper.AssertRawDataJsonContract(rejection, wrongEnvelope));
    }

    private static string ReadSource(string file) => File.ReadAllText(Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "Strategies", file)));
}
