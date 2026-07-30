namespace MomoQuant.UnitTests.Strategies;

/// <summary>
/// Explicit three-path result-evidence contracts (Milestone 23.1B1C2).
/// Required properties are fixed per strategy/case — never derived from live SUT output.
/// </summary>
internal static class ParityEvidenceContracts
{
    public static readonly IReadOnlyList<string> AdaptivePositiveRawData =
        ["setupFingerprint", "strengthBreakdown", "setup", "version", "reasonCode"];

    public static readonly IReadOnlyList<string> AdaptivePositiveStructure =
        ["strength", "strengthBreakdown"];

    public static readonly IReadOnlyList<string> AdaptiveRejectionRawData =
        Array.Empty<string>();

    public static readonly IReadOnlyList<string> RangePositiveRawData =
        ["setupFingerprint", "version", "diagnostics"];

    public static readonly IReadOnlyList<string> RangePositiveStructure =
        ["strength", "strengthBreakdown"];

    public static readonly IReadOnlyList<string> RangeRejectionRawData =
        ["strategyCode", "version", "reason", "symbolId", "timeframe", "marketRegime", "evaluatedAtUtc"];

    public static readonly IReadOnlyList<string> PsbrPositiveRawData =
        ["setupFingerprint", "structure", "version", "strengthBreakdown"];

    public static readonly IReadOnlyList<string> PsbrPositiveStructure =
        ["strength", "strengthBreakdown"];

    public static readonly IReadOnlyList<string> PsbrRejectionRawData =
        Array.Empty<string>();

    /// <summary>
    /// Production Adaptive/PSBR/Range NoTrade paths do not emit setupFingerprint.
    /// </summary>
    public static ParityAssertionHelper.FingerprintContract RejectionFingerprintAbsent { get; } =
        new ParityAssertionHelper.FingerprintContract.RequiredAbsent();

    public static ParityAssertionHelper.FingerprintContract PositiveFingerprint(string expectedNonEmptyValue)
    {
        if (string.IsNullOrWhiteSpace(expectedNonEmptyValue))
        {
            throw new ArgumentException(
                "Positive fingerprint contract requires a non-empty canonical value.",
                nameof(expectedNonEmptyValue));
        }

        return new ParityAssertionHelper.FingerprintContract.RequiredPresent(expectedNonEmptyValue);
    }
}
