namespace MomoQuant.UnitTests.Strategies;

/// <summary>Focused guards for the B1C3 candidate and persisted-funnel closure contracts.</summary>
public sealed class Milestone231B1C3CandidateEvidenceClosureTests
{
    [Fact]
    public void CandidateContract_RealPositiveCasesConstructBeforeExecutionAndUseFinalHelper()
    {
        var source = Read("Milestone231BParityTests.cs");
        foreach (var method in new[]
        {
            "CrossPath_Adaptive_DirectLabBacktest_IdenticalAtSameT",
            "CrossPath_Range_DirectLabBacktest_IdenticalAtSameT",
            "CrossPath_Psbr_DirectLabBacktest_IdenticalAtSameT"
        })
        {
            var start = source.IndexOf(method, StringComparison.Ordinal);
            var contract = source.IndexOf("CreatePositiveCandidateContract(", start, StringComparison.Ordinal);
            var direct = source.IndexOf("plugin.Evaluate(", start, StringComparison.Ordinal);
            var helper = source.IndexOf("CandidateContract = candidateContract", start, StringComparison.Ordinal);
            Assert.True(start >= 0 && contract > start && contract < direct && helper > direct);
        }
    }

    [Fact]
    public void CandidateCreatedAtUtc_UsesInjectedFixedTimeProvider()
    {
        var runner = ReadApplication("StrategyLab", "StrategyLabRunner.cs");
        Assert.Contains("TimeProvider? timeProvider = null", runner, StringComparison.Ordinal);
        Assert.Contains("_timeProvider = timeProvider ?? TimeProvider.System", runner, StringComparison.Ordinal);
        Assert.Contains("CreatedAtUtc = _timeProvider.GetUtcNow().UtcDateTime", runner, StringComparison.Ordinal);

        var parity = Read("Milestone231BParityTests.cs");
        Assert.Equal(3, Count(parity, "new ControllableTimeProvider(candidateCreatedAtUtc)"));
    }

    [Fact]
    public void CandidateFailureMatrix_IsRoutedThroughFinalHelper()
    {
        var source = Read("Milestone231B1CParityEvidenceTests.cs");
        Assert.Contains("PositiveParity_CandidateContractFieldMismatch_Fails", source, StringComparison.Ordinal);
        Assert.Contains("AssertPositive(directContext, direct, lab, backtest, capture, candidate)", source, StringComparison.Ordinal);
        Assert.Contains("CandidateContract = new ParityAssertionHelper.CandidateContract", source, StringComparison.Ordinal);
    }

    [Fact]
    public void PersistedFunnelContract_RealRejectionCasesUseExactContract()
    {
        var source = Read("Milestone231B1ATests.cs");
        Assert.Equal(3, Count(source, "RejectionFunnelContract = ParityEvidenceContracts.CreateOneEvaluationRejectionFunnelContract("));
        var helper = Read("ParityAssertionHelper.cs");
        Assert.Contains("AssertExactRejectionFunnel", helper, StringComparison.Ordinal);
        Assert.Contains("Assert.Equal(0, persistedRun.RawCandidateCount)", helper, StringComparison.Ordinal);
    }

    [Fact]
    public void PersistedFunnelFailureMatrix_IsRoutedThroughFinalHelper()
    {
        var source = Read("Milestone231B1CParityEvidenceTests.cs");
        Assert.Contains("RejectionParity_EmptyLabSummary_Fails", source, StringComparison.Ordinal);
        Assert.Contains("RejectionParity_MissingFunnelCode_Fails", source, StringComparison.Ordinal);
        Assert.Contains("RejectionParity_WrongFunnelCount_Fails", source, StringComparison.Ordinal);
        Assert.Contains("RejectionParity_FunnelEvaluationsMismatch_Fails", source, StringComparison.Ordinal);
    }

    private static string Read(string file) => File.ReadAllText(Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "Strategies", file)));

    private static string ReadApplication(params string[] path) => File.ReadAllText(Path.GetFullPath(Path.Combine(
        new[] { AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "MomoQuant.Application" }.Concat(path).ToArray())));

    private static int Count(string source, string value) => source.Split(value, StringSplitOptions.None).Length - 1;
}
