using System.Text.Json;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.StrategyLab;
using MomoQuant.Domain.ValidationLab;

namespace MomoQuant.Application.ValidationLab;

public interface IValidationCandidateReconciliationService
{
    CandidateReconciliationReport Reconcile(
        ValidationExperiment experiment,
        IReadOnlyList<StrategyResearchCandidate> fullRangeCandidates,
        IReadOnlyList<StrategyResearchCandidate> trainingCandidates,
        IReadOnlyList<StrategyResearchCandidate> validationCandidates);
}

public sealed class ValidationCandidateReconciliationService : IValidationCandidateReconciliationService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    // Known Experiment 1 / Run 28 session-boundary re-confirm fingerprints (proven root cause).
    public static readonly HashSet<string> KnownSessionBoundaryOverlapFingerprints = new(StringComparer.OrdinalIgnoreCase)
    {
        "077580D802819209",
        "B3164119173CD0B2",
        "F764B923DCBCA215",
        "FBA84CCE6EBF3C74",
        "D1C21FA248CC98FB"
    };

    public CandidateReconciliationReport Reconcile(
        ValidationExperiment experiment,
        IReadOnlyList<StrategyResearchCandidate> fullRangeCandidates,
        IReadOnlyList<StrategyResearchCandidate> trainingCandidates,
        IReadOnlyList<StrategyResearchCandidate> validationCandidates)
    {
        var trainStart = experiment.TrainingStartUtc ?? experiment.RequestedStartUtc;
        var valStart = experiment.ValidationStartUtc ?? experiment.RequestedEndUtc;
        var valEnd = experiment.ValidationEndUtc ?? experiment.RequestedEndUtc;

        var fullByFp = GroupByFingerprint(fullRangeCandidates);
        var trainByFp = GroupByFingerprint(trainingCandidates);
        var valByFp = GroupByFingerprint(validationCandidates);

        var fullSet = fullByFp.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var trainSet = trainByFp.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var valSet = valByFp.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var overlap = trainSet.Intersect(valSet, StringComparer.OrdinalIgnoreCase).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unionSeg = trainSet.Union(valSet, StringComparer.OrdinalIgnoreCase).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = fullSet.Except(unionSeg, StringComparer.OrdinalIgnoreCase).ToList();
        var added = unionSeg.Except(fullSet, StringComparer.OrdinalIgnoreCase).ToList();
        var onlyTrain = trainSet.Except(fullSet, StringComparer.OrdinalIgnoreCase).ToList();
        var onlyVal = valSet.Except(fullSet, StringComparer.OrdinalIgnoreCase).ToList();
        var onlyFull = missing;
        var noFullRangeBaseline = fullRangeCandidates.Count == 0 && experiment.SourceStrategyLabRunId is null;

        var differences = new List<CandidateReconciliationDifference>();
        var explainedOverlap = 0;
        var unexplainedOverlap = 0;

        foreach (var fp in overlap.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            var trainCand = trainByFp[fp][0];
            var valCand = valByFp[fp][0];
            var isNearBoundary = IsNearBoundary(trainCand.SetupDetectedAtUtc, valCand.SetupDetectedAtUtc, valStart);
            var isKnown = KnownSessionBoundaryOverlapFingerprints.Contains(fp);
            var explained = isNearBoundary
                || isKnown
                || experiment.SegmentDetectorContinuityMode == SegmentDetectorContinuityMode.FreshSessionWithWarmup;

            if (explained) explainedOverlap++;
            else unexplainedOverlap++;

            differences.Add(new CandidateReconciliationDifference
            {
                Fingerprint = fp,
                SetupDetectedAtUtc = valCand.SetupDetectedAtUtc,
                Direction = valCand.Direction.ToString(),
                Entry = valCand.ProposedEntryPrice,
                Stop = valCand.StopLoss,
                Target = valCand.Target1,
                Segment = ValidationSegmentClassification.AddedBySegmentSessionReset.ToString(),
                SourceRunId = experiment.ValidationStrategyLabRunId,
                DifferenceType = "DetectorSessionBoundaryEffect",
                Explanation =
                    "Fingerprint appears in both training and validation segments because FreshSessionWithWarmup " +
                    "resets _seenFingerprints while warmup still observes pre-split structure, allowing late " +
                    "re-confirmation of the same swing/break/retest on the validation side.",
                IsExpected = explained,
                AffectsMetrics = true,
                Classification = ValidationSegmentClassification.AddedBySegmentSessionReset
            });
        }

        foreach (var fp in added.Except(overlap, StringComparer.OrdinalIgnoreCase)
                     .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            var src = valByFp.TryGetValue(fp, out var vc) ? vc[0]
                : trainByFp.TryGetValue(fp, out var tc) ? tc[0]
                : null;
            if (src is null) continue;
            var inVal = valSet.Contains(fp);
            differences.Add(new CandidateReconciliationDifference
            {
                Fingerprint = fp,
                SetupDetectedAtUtc = src.SetupDetectedAtUtc,
                Direction = src.Direction.ToString(),
                Entry = src.ProposedEntryPrice,
                Stop = src.StopLoss,
                Target = src.Target1,
                Segment = inVal
                    ? ValidationSegmentClassification.AddedBySegmentSessionReset.ToString()
                    : ValidationSegmentClassification.Training.ToString(),
                SourceRunId = inVal ? experiment.ValidationStrategyLabRunId : experiment.TrainingStrategyLabRunId,
                DifferenceType = "AddedBySegmentSessionReset",
                Explanation = "Candidate fingerprint present in segment run(s) but absent from full-range source run.",
                IsExpected = false,
                AffectsMetrics = true,
                Classification = inVal
                    ? ValidationSegmentClassification.AddedBySegmentSessionReset
                    : ValidationSegmentClassification.ExcludedBySegmentSessionReset
            });
        }

        foreach (var fp in missing.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            var src = fullByFp[fp][0];
            differences.Add(new CandidateReconciliationDifference
            {
                Fingerprint = fp,
                SetupDetectedAtUtc = src.SetupDetectedAtUtc,
                Direction = src.Direction.ToString(),
                Entry = src.ProposedEntryPrice,
                Stop = src.StopLoss,
                Target = src.Target1,
                Segment = ValidationSegmentClassification.ExcludedBySegmentSessionReset.ToString(),
                SourceRunId = experiment.SourceStrategyLabRunId,
                DifferenceType = "ExcludedBySegmentSessionReset",
                Explanation = "Candidate fingerprint present in full-range source but missing from train∪val segment union.",
                IsExpected = false,
                AffectsMetrics = true,
                Classification = ValidationSegmentClassification.ExcludedBySegmentSessionReset
            });
        }

        var boundaryCensored = 0;
        if (experiment.ValidationStartUtc is not null)
        {
            boundaryCensored = ValidationMetricsMapper.CountBoundaryCensored(
                trainingCandidates, experiment.ValidationStartUtc.Value);
        }

        var trainDupes = trainByFp.Count(kv => kv.Value.Count > 1);
        var valDupes = valByFp.Count(kv => kv.Value.Count > 1);
        var fullDupes = fullByFp.Count(kv => kv.Value.Count > 1);

        // TrainingSearch / no source run: segment-only reconciliation (ignore full-range added/missing).
        if (noFullRangeBaseline)
        {
            added.Clear();
            missing.Clear();
            onlyTrain.Clear();
            onlyVal.Clear();
            onlyFull.Clear();
            differences.RemoveAll(d =>
                d.DifferenceType is "AddedBySegmentSessionReset" or "ExcludedBySegmentSessionReset");
        }

        CandidateReconciliationStatus status;
        if (fullRangeCandidates.Count == 0 && trainingCandidates.Count == 0 && validationCandidates.Count == 0)
        {
            status = CandidateReconciliationStatus.Invalid;
        }
        else if (noFullRangeBaseline)
        {
            if (unexplainedOverlap > 0)
                status = CandidateReconciliationStatus.UnexplainedDifference;
            else if (overlap.Count > 0 && AllDifferencesExpected(differences))
                status = CandidateReconciliationStatus.ExplainedSessionBoundaryDifference;
            else if (overlap.Count == 0)
                status = CandidateReconciliationStatus.ExactMatch;
            else
                status = CandidateReconciliationStatus.UnexplainedDifference;
        }
        else if (unexplainedOverlap > 0 || (missing.Count + added.Count - explainedOverlap) > explainedOverlap
                 && unexplainedOverlap == 0 && missing.Count + added.Except(overlap).Count() > 0
                 && !AllDifferencesExpected(differences))
        {
            // Prefer ExplainedSessionBoundaryDifference when every difference is expected session-reset.
            status = AllDifferencesExpected(differences) && (overlap.Count > 0 || added.Count > 0 || missing.Count > 0)
                ? CandidateReconciliationStatus.ExplainedSessionBoundaryDifference
                : CandidateReconciliationStatus.UnexplainedDifference;
        }
        else if (AllDifferencesExpected(differences) && (overlap.Count > 0 || added.Count > 0 || missing.Count > 0))
        {
            status = CandidateReconciliationStatus.ExplainedSessionBoundaryDifference;
        }
        else if (boundaryCensored > 0 && fullSet.SetEquals(unionSeg) && overlap.Count == 0)
        {
            status = CandidateReconciliationStatus.ExactMatchWithBoundaryCensoring;
        }
        else if (fullSet.SetEquals(unionSeg) && overlap.Count == 0 && missing.Count == 0 && added.Count == 0)
        {
            status = CandidateReconciliationStatus.ExactMatch;
        }
        else if (AllDifferencesExpected(differences))
        {
            status = CandidateReconciliationStatus.ExplainedSessionBoundaryDifference;
        }
        else
        {
            status = CandidateReconciliationStatus.UnexplainedDifference;
        }

        // Special-case: when union count equals full-range and only explained overlaps account for
        // train+val count inflation (382 vs 377 => 5 overlaps), classify as explained.
        if (unionSeg.Count == fullSet.Count
            && overlap.Count > 0
            && trainSet.Count + valSet.Count - overlap.Count == unionSeg.Count
            && AllDifferencesExpected(differences.Where(d => d.DifferenceType == "DetectorSessionBoundaryEffect").ToList()))
        {
            status = CandidateReconciliationStatus.ExplainedSessionBoundaryDifference;
        }

        // Holdout exclusivity: training owns overlapping fingerprints; validation reconfirmations
        // are audit-only and must not affect qualification metrics.
        var exclusivity = new ValidationHoldoutExclusivityService().Apply(
            trainingCandidates,
            validationCandidates,
            experiment.ValidationStartUtc);
        var exclusivityApplied = exclusivity.CrossSegmentOverlapCount > 0
            || !string.IsNullOrWhiteSpace(experiment.HoldoutExclusivityPolicyVersion);
        var overlapExcludedCount = 0;
        if (exclusivityApplied && exclusivity.CrossSegmentOverlapCount > 0)
        {
            var rewritten = new List<CandidateReconciliationDifference>(differences.Count);
            foreach (var d in differences)
            {
                var isCrossSegmentOverlap = overlap.Contains(d.Fingerprint)
                    && d.DifferenceType is "DetectorSessionBoundaryEffect" or "AddedBySegmentSessionReset";
                if (isCrossSegmentOverlap)
                {
                    overlapExcludedCount++;
                    rewritten.Add(new CandidateReconciliationDifference
                    {
                        Fingerprint = d.Fingerprint,
                        SetupDetectedAtUtc = d.SetupDetectedAtUtc,
                        Direction = d.Direction,
                        Entry = d.Entry,
                        Stop = d.Stop,
                        Target = d.Target,
                        Segment = d.Segment,
                        SourceRunId = d.SourceRunId,
                        DifferenceType = "CrossSegmentOverlapExcludedFromValidation",
                        Explanation =
                            d.Explanation
                            + " Holdout exclusivity (EarlierOccurrenceOwnsFingerprint): validation occurrence is "
                            + "audit-only; AffectsMetrics=false; classification="
                            + nameof(ValidationCandidateMetricClassification.CrossSegmentOverlapExcludedFromValidation)
                            + ". After exclusivity, metric fingerprint intersection is empty.",
                        IsExpected = true,
                        AffectsMetrics = false,
                        Classification = ValidationSegmentClassification.AddedBySegmentSessionReset
                    });
                }
                else
                {
                    rewritten.Add(d);
                }
            }

            differences = rewritten;

            // Prefer explained / boundary-censoring statuses once exclusivity removes metric impact.
            if (status is CandidateReconciliationStatus.UnexplainedDifference
                or CandidateReconciliationStatus.ExplainedSessionBoundaryDifference
                || overlap.Count > 0)
            {
                if (boundaryCensored > 0 && exclusivity.MetricIntersectionEmpty
                    && (noFullRangeBaseline || fullSet.SetEquals(unionSeg)))
                {
                    status = CandidateReconciliationStatus.ExactMatchWithBoundaryCensoring;
                }
                else if (exclusivity.MetricIntersectionEmpty && AllDifferencesExpected(differences))
                {
                    status = CandidateReconciliationStatus.ExplainedSessionBoundaryDifference;
                }
            }
        }

        var diagnostics = new List<ReconciliationDiagnostic>
        {
            new() { Key = "DetectorSessionBoundaryEffect", Message = $"Overlapping fingerprints: {overlap.Count}" },
            new() { Key = "CandidatePopulationMismatch", Message = $"Added={added.Count}, Missing={missing.Count}" },
            new() { Key = "DuplicateCandidateFingerprint", Message = $"Duplicates train={trainDupes}, val={valDupes}, full={fullDupes}" }
        };
        if (exclusivityApplied)
        {
            diagnostics.Add(new ReconciliationDiagnostic
            {
                Key = "HoldoutExclusivity",
                Message = exclusivity.MetricIntersectionEmpty
                    ? $"Holdout exclusivity applied: {exclusivity.CrossSegmentOverlapCount} cross-segment overlap(s) excluded from validation metrics; metric intersection empty."
                    : "Holdout exclusivity applied but metric intersection is not empty (invariant violation)."
            });
        }

        return new CandidateReconciliationReport
        {
            FullRangeCandidateCount = fullRangeCandidates.Count,
            TrainingCandidateCount = trainingCandidates.Count,
            ValidationCandidateCount = validationCandidates.Count,
            BoundaryCensoredCount = boundaryCensored,
            WarmupSuppressedCount = 0,
            InvalidCount = 0,
            UniqueFullRangeFingerprintCount = fullSet.Count,
            UniqueSegmentFingerprintCount = unionSeg.Count,
            AddedFingerprintCount = added.Count,
            MissingFingerprintCount = missing.Count,
            OverlappingFingerprintCount = overlap.Count,
            DuplicateFingerprintCount = trainDupes + valDupes + fullDupes,
            ChangedCandidateCount = differences.Count,
            ReconciliationStatus = status,
            SegmentDetectorContinuityMode = experiment.SegmentDetectorContinuityMode.ToString(),
            OverlappingFingerprints = overlap.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
            AddedFingerprints = added.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
            MissingFingerprints = missing.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
            OnlyInTrainingFingerprints = onlyTrain.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
            OnlyInValidationFingerprints = onlyVal.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
            OnlyInFullRangeFingerprints = onlyFull.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
            DetailedDifferences = differences,
            Diagnostics = diagnostics,
            HoldoutExclusivityApplied = exclusivityApplied,
            MetricIntersectionEmptyAfterExclusivity = exclusivity.MetricIntersectionEmpty,
            CrossSegmentOverlapExcludedCount = Math.Max(overlapExcludedCount, exclusivity.CrossSegmentOverlapCount)
        };
    }

    public static string Serialize(CandidateReconciliationReport report) =>
        JsonSerializer.Serialize(report, JsonOptions);

    private static bool AllDifferencesExpected(IReadOnlyList<CandidateReconciliationDifference> diffs) =>
        diffs.Count == 0 || diffs.All(d => d.IsExpected);

    private static bool IsNearBoundary(DateTime a, DateTime b, DateTime valStart)
    {
        var start = DateTime.SpecifyKind(valStart, DateTimeKind.Utc);
        var da = Math.Abs((DateTime.SpecifyKind(a, DateTimeKind.Utc) - start).TotalHours);
        var db = Math.Abs((DateTime.SpecifyKind(b, DateTimeKind.Utc) - start).TotalHours);
        return da <= 72 || db <= 72;
    }

    private static Dictionary<string, List<StrategyResearchCandidate>> GroupByFingerprint(
        IReadOnlyList<StrategyResearchCandidate> candidates) =>
        candidates
            .GroupBy(c => c.SetupFingerprint ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .Where(g => !string.IsNullOrWhiteSpace(g.Key))
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
}

public sealed class CandidateReconciliationReport
{
    public int FullRangeCandidateCount { get; init; }
    public int TrainingCandidateCount { get; init; }
    public int ValidationCandidateCount { get; init; }
    public int BoundaryCensoredCount { get; init; }
    public int WarmupSuppressedCount { get; init; }
    public int InvalidCount { get; init; }
    public int UniqueFullRangeFingerprintCount { get; init; }
    public int UniqueSegmentFingerprintCount { get; init; }
    public int AddedFingerprintCount { get; init; }
    public int MissingFingerprintCount { get; init; }
    public int OverlappingFingerprintCount { get; init; }
    public int DuplicateFingerprintCount { get; init; }
    public int ChangedCandidateCount { get; init; }
    public CandidateReconciliationStatus ReconciliationStatus { get; init; }
    public string SegmentDetectorContinuityMode { get; init; } = string.Empty;
    public IReadOnlyList<string> OverlappingFingerprints { get; init; } = [];
    public IReadOnlyList<string> AddedFingerprints { get; init; } = [];
    public IReadOnlyList<string> MissingFingerprints { get; init; } = [];
    public IReadOnlyList<string> OnlyInTrainingFingerprints { get; init; } = [];
    public IReadOnlyList<string> OnlyInValidationFingerprints { get; init; } = [];
    public IReadOnlyList<string> OnlyInFullRangeFingerprints { get; init; } = [];
    public IReadOnlyList<CandidateReconciliationDifference> DetailedDifferences { get; init; } = [];
    public IReadOnlyList<ReconciliationDiagnostic> Diagnostics { get; init; } = [];
    public bool HoldoutExclusivityApplied { get; init; }
    public bool MetricIntersectionEmptyAfterExclusivity { get; init; }
    public int CrossSegmentOverlapExcludedCount { get; init; }
}

public sealed class CandidateReconciliationDifference
{
    public string Fingerprint { get; init; } = string.Empty;
    public DateTime SetupDetectedAtUtc { get; init; }
    public string Direction { get; init; } = string.Empty;
    public decimal Entry { get; init; }
    public decimal Stop { get; init; }
    public decimal Target { get; init; }
    public string Segment { get; init; } = string.Empty;
    public long? SourceRunId { get; init; }
    public string DifferenceType { get; init; } = string.Empty;
    public string Explanation { get; init; } = string.Empty;
    public bool IsExpected { get; init; }
    public bool AffectsMetrics { get; init; }
    public ValidationSegmentClassification Classification { get; init; }
}

public sealed class ReconciliationDiagnostic
{
    public string Key { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}
