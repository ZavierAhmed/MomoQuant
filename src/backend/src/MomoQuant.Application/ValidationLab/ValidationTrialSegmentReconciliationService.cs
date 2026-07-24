using System.Text.Json;
using MomoQuant.Application.Abstractions;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.ValidationLab;

namespace MomoQuant.Application.ValidationLab;

public sealed class ValidationTrialSegmentReconciliationReport
{
    public const string MismatchCode = "TRIAL_SEGMENT_METRIC_MISMATCH";
    public const string Version = "ValidationTrialSegmentReconciliation/v1";

    public string ReconciliationVersion { get; init; } = Version;
    public ValidationTrialSegmentReconciliationStatus Status { get; init; }
    public string? TrialMetricFingerprint { get; init; }
    public string? TrialDerivedResultFingerprint { get; init; }
    public string? SegmentResultFingerprint { get; init; }
    public IReadOnlyList<string> MismatchReasons { get; init; } = [];
}

/// <summary>
/// Milestone 23.0D WP21 — after the training segment write, the selected trial's persisted
/// ValidationMetrics/v1.3.2 snapshot must reproduce the RawStrategy training segment result.
/// Any mismatch is reported as TRIAL_SEGMENT_METRIC_MISMATCH and blocks freeze.
/// </summary>
public interface IValidationTrialSegmentReconciliationService
{
    Task<ValidationTrialSegmentReconciliationReport> ReconcileAsync(
        ValidationExperiment experiment,
        ValidationParameterTrial selectedTrial,
        CancellationToken cancellationToken = default);
}

public sealed class ValidationTrialSegmentReconciliationService : IValidationTrialSegmentReconciliationService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly IValidationSegmentResultRepository _segments;

    public ValidationTrialSegmentReconciliationService(IValidationSegmentResultRepository segments) =>
        _segments = segments;

    public async Task<ValidationTrialSegmentReconciliationReport> ReconcileAsync(
        ValidationExperiment experiment,
        ValidationParameterTrial selectedTrial,
        CancellationToken cancellationToken = default)
    {
        var segments = await _segments.GetByExperimentIdAsync(experiment.Id, cancellationToken);
        var trainingRawSegment = segments.FirstOrDefault(s =>
            s.SegmentType == ValidationSegmentType.Training
            && s.LayerType == ValidationLayerType.RawStrategy);

        return Reconcile(selectedTrial, trainingRawSegment);
    }

    public static ValidationTrialSegmentReconciliationReport Reconcile(
        ValidationParameterTrial selectedTrial,
        ValidationSegmentResult? trainingRawSegment)
    {
        var reasons = new List<string>();

        if (string.IsNullOrWhiteSpace(selectedTrial.TrialMetricSnapshotJson)
            || string.IsNullOrWhiteSpace(selectedTrial.TrialMetricFingerprint))
        {
            reasons.Add("MISSING_TRIAL_METRIC_SNAPSHOT");
        }

        if (trainingRawSegment is null)
        {
            // Integrity fixtures may select a winner with a persisted snapshot but no StrategyLab run
            // (no segment write). That is NotEvaluated — freeze only blocks on Mismatched.
            // Missing snapshot is still a hard mismatch even without a run id.
            if (selectedTrial.StrategyLabRunId is null && reasons.Count == 0)
            {
                return new ValidationTrialSegmentReconciliationReport
                {
                    Status = ValidationTrialSegmentReconciliationStatus.NotEvaluated,
                    TrialMetricFingerprint = selectedTrial.TrialMetricFingerprint,
                    MismatchReasons = ["NO_STRATEGY_LAB_RUN_FOR_SEGMENT"]
                };
            }

            reasons.Add("MISSING_TRAINING_RAWSTRATEGY_SEGMENT");
        }

        LayerSegmentMetrics? trialMetrics = null;
        if (reasons.Count == 0)
        {
            trialMetrics = TryReadSnapshotMetrics(selectedTrial.TrialMetricSnapshotJson!);
            if (trialMetrics is null)
            {
                reasons.Add("UNREADABLE_TRIAL_METRIC_SNAPSHOT");
            }
        }

        string? trialDerivedFingerprint = null;
        if (reasons.Count == 0)
        {
            // The segment writer fingerprints exactly these fields; recomputing them from the
            // trial snapshot must reproduce the persisted segment ResultFingerprint.
            var fingerprintFields = ValidationMetricsContract.BuildPathResultFingerprintFields(
                ValidationSegmentType.Training,
                ValidationLayerType.RawStrategy,
                trialMetrics!);
            trialDerivedFingerprint = ValidationLabService.ParameterFingerprint(fingerprintFields);

            if (!string.Equals(trialDerivedFingerprint, trainingRawSegment!.ResultFingerprint, StringComparison.Ordinal))
            {
                reasons.Add(ValidationTrialSegmentReconciliationReport.MismatchCode);
                AddFieldMismatchDetails(reasons, trialMetrics!, trainingRawSegment);
            }
        }

        return new ValidationTrialSegmentReconciliationReport
        {
            Status = reasons.Count == 0
                ? ValidationTrialSegmentReconciliationStatus.Matched
                : ValidationTrialSegmentReconciliationStatus.Mismatched,
            TrialMetricFingerprint = selectedTrial.TrialMetricFingerprint,
            TrialDerivedResultFingerprint = trialDerivedFingerprint,
            SegmentResultFingerprint = trainingRawSegment?.ResultFingerprint,
            MismatchReasons = reasons
        };
    }

    public static string Serialize(ValidationTrialSegmentReconciliationReport report) =>
        JsonSerializer.Serialize(report, JsonOptions);

    private static LayerSegmentMetrics? TryReadSnapshotMetrics(string snapshotJson)
    {
        try
        {
            var snapshot = JsonSerializer.Deserialize<ValidationTrialMetricSnapshot>(snapshotJson, JsonOptions);
            return snapshot?.RawStrategyTrainingMetrics;
        }
        catch
        {
            return null;
        }
    }

    private static void AddFieldMismatchDetails(
        List<string> reasons,
        LayerSegmentMetrics trialMetrics,
        ValidationSegmentResult segment)
    {
        void Compare<T>(string field, T trialValue, T segmentValue)
        {
            if (!EqualityComparer<T>.Default.Equals(trialValue, segmentValue))
            {
                reasons.Add($"{field}: trial={trialValue} segment={segmentValue}");
            }
        }

        Compare("ClosedTradeCount",
            trialMetrics.ClosedOutcomePopulationCount ?? trialMetrics.ClosedTradeCount,
            segment.ClosedTradeCount);
        Compare("NetExpectancyR", trialMetrics.NetExpectancyR, segment.NetExpectancyR);
        Compare("NetPnl", trialMetrics.NetPnl, segment.NetPnl);
        Compare("IncludedPathInputCount",
            trialMetrics.IncludedPathInputCount ?? trialMetrics.MetricIncludedCandidateCount,
            segment.MetricIncludedCandidateCount);
        Compare("ExcludedPathInputCount",
            trialMetrics.ExcludedPathInputCount ?? trialMetrics.MetricExcludedCandidateCount,
            segment.MetricExcludedCandidateCount);
    }
}
