namespace MomoQuant.Application.Options;

/// <summary>
/// Configurable settings for the SK System Analyzer.
/// These are approximation/configurable parameters, not certified proprietary rules.
/// Analysis only — never used for trade execution.
/// </summary>
public sealed class SkSystemSettings
{
    public const string SectionName = "SkSystem";

    public string SwingSensitivity { get; set; } = "Balanced";

    public decimal MinSwingDistancePercent { get; set; } = 0.4m;

    public int MinSwingCandles { get; set; } = 3;

    public List<decimal> FibonacciCorrectionLevels { get; set; } =
        [0.382m, 0.5m, 0.559m, 0.618m, 0.667m];

    public List<decimal> FibonacciExtensionLevels { get; set; } =
        [1.0m, 1.272m, 1.618m, 2.0m];

    public decimal GoldenPocketMin { get; set; } = 0.618m;

    public decimal GoldenPocketMax { get; set; } = 0.667m;

    public bool UseWicksForSwingPoints { get; set; } = true;

    public int MaxSequenceCandidates { get; set; } = 5;

    public bool RequireHigherTimeframeAgreement { get; set; }

    public bool AiSummaryEnabledDefault { get; set; } = true;
}
