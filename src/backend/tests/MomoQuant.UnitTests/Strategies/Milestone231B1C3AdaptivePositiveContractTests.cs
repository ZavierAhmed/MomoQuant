using System.Text.Json.Nodes;

namespace MomoQuant.UnitTests.Strategies;

/// <summary>Milestone 23.1B1C3 â€” Adaptive positive evidence is immutable before execution.</summary>
public sealed class Milestone231B1C3AdaptivePositiveContractTests
{
    [Fact]
    public void AdaptivePositiveContract_ConstructedBeforeDirectLabAndBacktestExecution()
    {
        var source = ReadAdaptivePositiveParitySource();
        var methodStart = source.IndexOf(
            "public async Task CrossPath_Adaptive_DirectLabBacktest_IdenticalAtSameT()",
            StringComparison.Ordinal);
        Assert.True(methodStart >= 0);

        var contractConstruction = source.IndexOf("var rawDataContract =", methodStart, StringComparison.Ordinal);
        var directExecution = source.IndexOf("var direct = plugin.Evaluate(context);", methodStart, StringComparison.Ordinal);
        var labExecution = source.IndexOf(".ExecuteAsync(labRun.Id", methodStart, StringComparison.Ordinal);
        var backtestExecution = source.IndexOf("await engine.ProcessCandleAtIndexAsync(", methodStart, StringComparison.Ordinal);

        Assert.True(contractConstruction >= methodStart);
        Assert.True(directExecution > contractConstruction);
        Assert.True(labExecution > contractConstruction);
        Assert.True(backtestExecution > contractConstruction);
    }

    [Fact]
    public void AdaptivePositiveContract_RealParityCaseRoutesAllPathsThroughFinalHelper()
    {
        var source = ReadAdaptivePositiveParitySource();
        var methodStart = source.IndexOf(
            "public async Task CrossPath_Adaptive_DirectLabBacktest_IdenticalAtSameT()",
            StringComparison.Ordinal);
        Assert.True(methodStart >= 0);

        var finalHelper = source.IndexOf("ParityAssertionHelper.AssertPositiveThreePathParity(", methodStart, StringComparison.Ordinal);
        var nextParityCase = source.IndexOf("public async Task CrossPath_Range_", methodStart, StringComparison.Ordinal);

        Assert.InRange(finalHelper, methodStart, nextParityCase - 1);
        Assert.InRange(source.IndexOf("RawDataContract = rawDataContract", finalHelper, StringComparison.Ordinal), finalHelper, nextParityCase - 1);
        Assert.InRange(source.IndexOf("OutcomeContract = outcomeContract", finalHelper, StringComparison.Ordinal), finalHelper, nextParityCase - 1);
    }

    [Fact]
    public void AdaptivePositiveContract_RootAndNestedExpectations_CannotBeMutatedAfterConstruction()
    {
        var contract = ParityEvidenceContracts.CreateAdaptivePositiveRawDataContract();
        var expectedRootNames = new[]
        {
            "setupFingerprint", "strengthBreakdown", "setup", "version", "reasonCode"
        };
        var expectedFingerprint = ParityEvidenceContracts.AdaptivePositiveFingerprint;
        var strengthBefore = contract.Properties["strengthBreakdown"].CanonicalJsonValue;
        var setupBefore = contract.Properties["setup"].CanonicalJsonValue;

        Assert.Equal(ParityAssertionHelper.RawDataJsonRootState.PresentJsonObject, contract.RootState);
        Assert.Equal(expectedRootNames.OrderBy(name => name), contract.Properties.Keys.OrderBy(name => name));
        Assert.Equal(expectedFingerprint, contract.Properties["setupFingerprint"].StringValue);

        var mutatedStrengthCopy = JsonNode.Parse(strengthBefore!)!.AsObject();
        mutatedStrengthCopy["total"] = 0;
        var mutatedSetupCopy = JsonNode.Parse(setupBefore!)!.AsObject();
        mutatedSetupCopy["direction"] = "Short";

        var replacementProperties = contract.Properties
            .SetItem("strengthBreakdown", ParityAssertionHelper.JsonPropertyExpectation.Json(mutatedStrengthCopy.ToJsonString()))
            .SetItem("setup", ParityAssertionHelper.JsonPropertyExpectation.Json(mutatedSetupCopy.ToJsonString()));
        var replacementContract = contract with { Properties = replacementProperties };

        Assert.Equal(strengthBefore, contract.Properties["strengthBreakdown"].CanonicalJsonValue);
        Assert.Equal(setupBefore, contract.Properties["setup"].CanonicalJsonValue);
        Assert.Equal(expectedFingerprint, contract.Properties["setupFingerprint"].StringValue);
        Assert.NotEqual(strengthBefore, replacementContract.Properties["strengthBreakdown"].CanonicalJsonValue);
        Assert.NotEqual(setupBefore, replacementContract.Properties["setup"].CanonicalJsonValue);
    }

    private static string ReadAdaptivePositiveParitySource()
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "Strategies",
            "Milestone231BParityTests.cs"));
        Assert.True(File.Exists(path), $"Expected Adaptive parity source at {path}");
        return File.ReadAllText(path);
    }
}
