using System.Text.Json;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.StrategyLab;

namespace MomoQuant.Application.ValidationLab;

/// <summary>
/// ValidationHoldoutExclusivity/v1 — EarlierOccurrenceOwnsFingerprint.
/// A structural SetupFingerprint may contribute to only one performance segment.
/// Training owns overlaps; validation reconfirmations are audit-only.
/// </summary>
public static class ValidationHoldoutExclusivityVersions
{
    public const string Current = "ValidationHoldoutExclusivity/v1";
}

public sealed class HoldoutExclusivityOverlap
{
    public string OverlapFingerprint { get; init; } = string.Empty;
    public long? CanonicalOccurrenceCandidateId { get; init; }
    public long? DuplicateOccurrenceCandidateId { get; init; }
    public DateTime? TrainingSetupDetectedAtUtc { get; init; }
    public DateTime? ValidationSetupDetectedAtUtc { get; init; }
    public string MetricOwner { get; init; } = "Training";
    public string ExcludedOccurrence { get; init; } = "Validation";
    public string MetricClassification { get; init; } =
        nameof(ValidationCandidateMetricClassification.CrossSegmentOverlapExcludedFromValidation);
    public string MetricExclusionReason { get; init; } =
        "Excluded from validation metrics because this structural setup fingerprint was already observed during training.";
    public string OverlapPolicyVersion { get; init; } = ValidationHoldoutExclusivityVersions.Current;
    public DateTime OverlapDetectedAtUtc { get; init; } = DateTime.UtcNow;
    public bool PortfolioMutationAllowed { get; init; }
}

public sealed class CandidateMetricClassificationRow
{
    public long CandidateId { get; init; }
    public string SetupFingerprint { get; init; } = string.Empty;
    public string Segment { get; init; } = string.Empty;
    public ValidationCandidateMetricClassification MetricClassification { get; init; }
    public string? MetricExclusionReason { get; init; }
    public bool PortfolioMutationAllowed { get; init; } = true;
    public long? CanonicalOccurrenceCandidateId { get; init; }
    public long? DuplicateOccurrenceCandidateId { get; init; }
}

public sealed class HoldoutExclusivityReport
{
    public string PolicyVersion { get; init; } = ValidationHoldoutExclusivityVersions.Current;
    public string Policy { get; init; } = "EarlierOccurrenceOwnsFingerprint";
    public int TrainingPersistedRowCount { get; init; }
    public int ValidationPersistedRowCount { get; init; }
    public int TrainingMetricIncludedCount { get; init; }
    public int ValidationMetricIncludedCount { get; init; }
    public int CrossSegmentOverlapCount { get; init; }
    public int BoundaryCensoredCount { get; init; }
    public IReadOnlyList<string> TrainingMetricFingerprints { get; init; } = [];
    public IReadOnlyList<string> ValidationMetricFingerprints { get; init; } = [];
    public IReadOnlyList<HoldoutExclusivityOverlap> Overlaps { get; init; } = [];
    public IReadOnlyList<CandidateMetricClassificationRow> Classifications { get; init; } = [];
    public bool MetricIntersectionEmpty { get; init; }
    public bool UnionReconcilesWithProvidedRange { get; init; } = true;
    public string Explanation { get; init; } = string.Empty;
}

public sealed class ExclusivityPartition
{
    public IReadOnlyList<StrategyResearchCandidate> MetricIncluded { get; init; } = [];
    public IReadOnlyList<StrategyResearchCandidate> AuditOnly { get; init; } = [];
}

public interface IValidationHoldoutExclusivityService
{
    HoldoutExclusivityReport Apply(
        IReadOnlyList<StrategyResearchCandidate> trainingCandidates,
        IReadOnlyList<StrategyResearchCandidate> validationCandidates,
        DateTime? validationStartUtc = null,
        IReadOnlyList<StrategyResearchCandidate>? boundaryCensoredTraining = null);

    ExclusivityPartition ApplyExclusivityToValidationCandidates(
        IReadOnlyList<StrategyResearchCandidate> validationCandidates,
        HoldoutExclusivityReport report);
}

public sealed class ValidationHoldoutExclusivityService : IValidationHoldoutExclusivityService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public HoldoutExclusivityReport Apply(
        IReadOnlyList<StrategyResearchCandidate> trainingCandidates,
        IReadOnlyList<StrategyResearchCandidate> validationCandidates,
        DateTime? validationStartUtc = null,
        IReadOnlyList<StrategyResearchCandidate>? boundaryCensoredTraining = null)
    {
        var trainByFp = trainingCandidates
            .Where(c => !string.IsNullOrWhiteSpace(c.SetupFingerprint))
            .GroupBy(c => c.SetupFingerprint, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(x => x.SetupDetectedAtUtc).ThenBy(x => x.Id).First(),
                StringComparer.OrdinalIgnoreCase);

        var valByFp = validationCandidates
            .Where(c => !string.IsNullOrWhiteSpace(c.SetupFingerprint))
            .GroupBy(c => c.SetupFingerprint, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(x => x.SetupDetectedAtUtc).ThenBy(x => x.Id).First(),
                StringComparer.OrdinalIgnoreCase);

        var classifications = new List<CandidateMetricClassificationRow>();
        var overlaps = new List<HoldoutExclusivityOverlap>();
        var trainMetricFps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var valMetricFps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var boundarySet = (boundaryCensoredTraining ?? [])
            .Select(c => c.Id)
            .ToHashSet();

        if (boundaryCensoredTraining is null && validationStartUtc is not null)
        {
            var start = DateTime.SpecifyKind(validationStartUtc.Value, DateTimeKind.Utc);
            foreach (var c in trainingCandidates.Where(c =>
                         c.SetupDetectedAtUtc < start
                         && c.RawExitTimeUtc.HasValue
                         && DateTime.SpecifyKind(c.RawExitTimeUtc.Value, DateTimeKind.Utc) >= start))
            {
                boundarySet.Add(c.Id);
            }
        }

        foreach (var c in trainingCandidates)
        {
            if (boundarySet.Contains(c.Id))
            {
                classifications.Add(new CandidateMetricClassificationRow
                {
                    CandidateId = c.Id,
                    SetupFingerprint = c.SetupFingerprint,
                    Segment = "Training",
                    MetricClassification = ValidationCandidateMetricClassification.BoundaryCensored,
                    MetricExclusionReason = "Boundary-censored: setup in training, exit at/after validation start.",
                    PortfolioMutationAllowed = false
                });
                continue;
            }

            classifications.Add(new CandidateMetricClassificationRow
            {
                CandidateId = c.Id,
                SetupFingerprint = c.SetupFingerprint,
                Segment = "Training",
                MetricClassification = ValidationCandidateMetricClassification.TrainingIncluded,
                PortfolioMutationAllowed = true
            });
            if (!string.IsNullOrWhiteSpace(c.SetupFingerprint))
            {
                trainMetricFps.Add(c.SetupFingerprint);
            }
        }

        foreach (var c in validationCandidates)
        {
            var isOverlap = !string.IsNullOrWhiteSpace(c.SetupFingerprint)
                && trainByFp.ContainsKey(c.SetupFingerprint);

            if (isOverlap)
            {
                var trainCanon = trainByFp[c.SetupFingerprint];
                classifications.Add(new CandidateMetricClassificationRow
                {
                    CandidateId = c.Id,
                    SetupFingerprint = c.SetupFingerprint,
                    Segment = "Validation",
                    MetricClassification =
                        ValidationCandidateMetricClassification.CrossSegmentOverlapExcludedFromValidation,
                    MetricExclusionReason =
                        "Excluded from validation metrics because this structural setup fingerprint was already observed during training.",
                    PortfolioMutationAllowed = false,
                    CanonicalOccurrenceCandidateId = trainCanon.Id,
                    DuplicateOccurrenceCandidateId = c.Id
                });

                if (overlaps.All(o =>
                        !string.Equals(o.OverlapFingerprint, c.SetupFingerprint, StringComparison.OrdinalIgnoreCase)))
                {
                    overlaps.Add(new HoldoutExclusivityOverlap
                    {
                        OverlapFingerprint = c.SetupFingerprint,
                        CanonicalOccurrenceCandidateId = trainCanon.Id,
                        DuplicateOccurrenceCandidateId = c.Id,
                        TrainingSetupDetectedAtUtc = trainCanon.SetupDetectedAtUtc,
                        ValidationSetupDetectedAtUtc = c.SetupDetectedAtUtc,
                        MetricOwner = "Training",
                        ExcludedOccurrence = "Validation",
                        OverlapDetectedAtUtc = DateTime.UtcNow
                    });
                }
            }
            else
            {
                classifications.Add(new CandidateMetricClassificationRow
                {
                    CandidateId = c.Id,
                    SetupFingerprint = c.SetupFingerprint,
                    Segment = "Validation",
                    MetricClassification = ValidationCandidateMetricClassification.ValidationIncluded,
                    PortfolioMutationAllowed = true
                });
                if (!string.IsNullOrWhiteSpace(c.SetupFingerprint))
                {
                    valMetricFps.Add(c.SetupFingerprint);
                }
            }
        }

        var intersectionEmpty = !trainMetricFps.Overlaps(valMetricFps);
        var unionCount = trainMetricFps.Union(valMetricFps, StringComparer.OrdinalIgnoreCase).Count();
        var persistedUnique = trainByFp.Keys
            .Union(valByFp.Keys, StringComparer.OrdinalIgnoreCase)
            .Count();
        // Metric union + boundary-censored fingerprints should cover unique persisted FPs when exclusivity holds.
        var boundaryFps = classifications
            .Where(c => c.MetricClassification == ValidationCandidateMetricClassification.BoundaryCensored)
            .Select(c => c.SetupFingerprint)
            .Where(fp => !string.IsNullOrWhiteSpace(fp))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unionPlusBoundary = trainMetricFps
            .Union(valMetricFps, StringComparer.OrdinalIgnoreCase)
            .Union(boundaryFps, StringComparer.OrdinalIgnoreCase)
            .Count();
        var unionReconciles = intersectionEmpty && unionPlusBoundary >= persistedUnique;

        return new HoldoutExclusivityReport
        {
            PolicyVersion = ValidationHoldoutExclusivityVersions.Current,
            Policy = "EarlierOccurrenceOwnsFingerprint",
            TrainingPersistedRowCount = trainingCandidates.Count,
            ValidationPersistedRowCount = validationCandidates.Count,
            TrainingMetricIncludedCount = classifications.Count(c =>
                c.MetricClassification == ValidationCandidateMetricClassification.TrainingIncluded),
            ValidationMetricIncludedCount = classifications.Count(c =>
                c.MetricClassification == ValidationCandidateMetricClassification.ValidationIncluded),
            CrossSegmentOverlapCount = overlaps.Count,
            BoundaryCensoredCount = boundarySet.Count,
            TrainingMetricFingerprints = trainMetricFps.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
            ValidationMetricFingerprints = valMetricFps.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
            Overlaps = overlaps,
            Classifications = classifications,
            MetricIntersectionEmpty = intersectionEmpty,
            UnionReconcilesWithProvidedRange = unionReconciles,
            Explanation = intersectionEmpty
                ? $"Holdout exclusivity applied: {overlaps.Count} cross-segment fingerprint(s) excluded from validation metrics; training owns earlier occurrences. Metric union size={unionCount}."
                : "INVARIANT VIOLATION: training and validation metric fingerprint sets still overlap after exclusivity."
        };
    }

    public ExclusivityPartition ApplyExclusivityToValidationCandidates(
        IReadOnlyList<StrategyResearchCandidate> validationCandidates,
        HoldoutExclusivityReport report)
    {
        var excludedIds = report.Classifications
            .Where(c =>
                c.MetricClassification ==
                ValidationCandidateMetricClassification.CrossSegmentOverlapExcludedFromValidation)
            .Select(c => c.CandidateId)
            .ToHashSet();

        var metricIncluded = new List<StrategyResearchCandidate>();
        var auditOnly = new List<StrategyResearchCandidate>();
        foreach (var c in validationCandidates)
        {
            if (excludedIds.Contains(c.Id))
            {
                auditOnly.Add(c);
            }
            else
            {
                metricIncluded.Add(c);
            }
        }

        return new ExclusivityPartition
        {
            MetricIncluded = metricIncluded,
            AuditOnly = auditOnly
        };
    }

    public static IReadOnlyList<StrategyResearchCandidate> SelectMetricIncludedValidation(
        IReadOnlyList<StrategyResearchCandidate> validationCandidates,
        HoldoutExclusivityReport report) =>
        new ValidationHoldoutExclusivityService()
            .ApplyExclusivityToValidationCandidates(validationCandidates, report)
            .MetricIncluded;

    public static string Serialize(HoldoutExclusivityReport report) =>
        JsonSerializer.Serialize(report, JsonOptions);
}
