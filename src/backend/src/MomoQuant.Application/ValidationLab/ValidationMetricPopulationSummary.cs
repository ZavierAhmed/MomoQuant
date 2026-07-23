namespace MomoQuant.Application.ValidationLab;

/// <summary>
/// Explicit population counts for ValidationMetrics/v1.3.2 (ValidationMetricPopulation/v1).
/// Different metric families may use different valid populations.
/// </summary>
public sealed class ValidationMetricPopulationSummary
{
    public const string Version = "ValidationMetricPopulation/v1";

    public int CandidatePopulationCount { get; init; }
    public int BoundaryEligibleCandidateCount { get; init; }
    public int PathInputPopulationCount { get; init; }
    public int IncludedPathInputCount { get; init; }
    public int ExcludedPathInputCount { get; init; }
    public int WarningBearingIncludedCount { get; init; }
    public int ClosedOutcomePopulationCount { get; init; }
    public int MonetaryPnlPopulationCount { get; init; }
    public int GrossRPopulationCount { get; init; }
    public int NetRPopulationCount { get; init; }
    public int WinnerPopulationCount { get; init; }
    public int LoserPopulationCount { get; init; }
    public int NeutralPopulationCount { get; init; }

    /// <summary>Exclusion reason → count (path inputs with MetricInclusionStatus == Excluded).</summary>
    public IReadOnlyDictionary<string, int> ExclusionCountsByReason { get; init; } =
        new Dictionary<string, int>(StringComparer.Ordinal);

    /// <summary>Warning code → count across warning-bearing included path inputs.</summary>
    public IReadOnlyDictionary<string, int> WarningCountsByCode { get; init; } =
        new Dictionary<string, int>(StringComparer.Ordinal);

    public string PopulationContractVersion { get; init; } = Version;
}
