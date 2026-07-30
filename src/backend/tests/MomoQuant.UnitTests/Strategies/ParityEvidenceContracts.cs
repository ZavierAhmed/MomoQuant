namespace MomoQuant.UnitTests.Strategies;

/// <summary>
/// Explicit three-path result-evidence contracts (Milestone 23.1B1C2).
/// Required properties are fixed per strategy/case — never derived from live SUT output.
/// </summary>
internal static class ParityEvidenceContracts
{
    public const string AdaptivePositiveFingerprint = "8DC2EABFE2BA0A5E";

    // Independently frozen from the deterministic 600-candle Adaptive fixture before any
    // direct, Lab, or Backtest invocation. The exact-object contract also makes every
    // unlisted root property absent by construction.
    public static ParityAssertionHelper.RawDataJsonContract CreateAdaptivePositiveRawDataContract() =>
        ParityAssertionHelper.RawDataJsonContract.Create(
            ParityAssertionHelper.RawDataJsonRootState.PresentJsonObject,
            ("setupFingerprint", ParityAssertionHelper.JsonPropertyExpectation.String(AdaptivePositiveFingerprint)),
            ("strengthBreakdown", ParityAssertionHelper.JsonPropertyExpectation.Json("""
                {"htfAlignment":100,"executionTrend":26.219979440800739041582574400,"volatilityQuality":88.41181306137762195365436734,"breakoutQuality":100,"momentum":44.303996640375170448326299680,"retestQuality":70.488207300021319344742447990,"total":71.570666073762475131384281568}
                """)),
            ("setup", ParityAssertionHelper.JsonPropertyExpectation.Json("""
                {"setupType":"MtfTrendBreakoutRetest","direction":"Long","brokenLevel":51364,"breakoutTimeUtc":"2026-01-10T12:20:00Z","retestTimeUtc":"2026-01-10T12:25:00Z","confirmationTimeUtc":"2026-01-10T12:30:00Z","breakoutIndex":597,"retestIndex":598,"confirmationIndex":599,"adaptiveBuffer":0.2154778505099169588368980612,"volRatio":1.7698523367327797255793204083,"breakoutAtrFast":349.05954976083357099324744597,"breakoutAtrSlow":197.22523880450493957184368274,"retestAtrFast":340.78386763505974449372977126,"confirmationAtrFast":340.78386763505974449372977126,"retestExtreme":51328.8,"stopBufferAtr":0.20}
                """)),
            ("version", ParityAssertionHelper.JsonPropertyExpectation.String("1.0.0")),
            ("reasonCode", ParityAssertionHelper.JsonPropertyExpectation.String("EntryConfirmed")));

    public static ParityAssertionHelper.PositiveOutcomeContract CreateAdaptivePositiveOutcomeContract() =>
        new(
            Direction: MomoQuant.Domain.Enums.TradeDirection.Long,
            EntryPrice: 51540.000m,
            StopLoss: 51260.643226472988051101254046m,
            TakeProfit: 52238.391933817529872246864885m,
            Strength: 71.570666073762475131384281568m,
            Reason: "Long MTF trend breakout retest confirmed.");

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
