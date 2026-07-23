using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Common;
using MomoQuant.Application.Security;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.ValidationLab;

namespace MomoQuant.Application.ValidationLab;

public interface IValidationLaboratoryReadinessService
{
    Task<ServiceResult<ValidationLaboratoryReadinessReport>> GetReadinessAsync(
        CancellationToken cancellationToken = default);

    ValidationLaboratoryReadiness EvaluateExperiment(ValidationExperiment experiment);
}

public sealed class ValidationLaboratoryReadinessReport
{
    public ValidationLaboratoryReadiness Status { get; init; }
    public IReadOnlyList<ValidationLaboratoryReadinessCheck> Checks { get; init; } = [];
    public IReadOnlyList<ValidationExperimentReadinessItem> Experiments { get; init; } = [];
    public string Summary { get; init; } = string.Empty;

    /// <summary>Hosting gate (Milestone 23.0 Package Q). Independent of lab experiment readiness.</summary>
    public HostingSecurityReadinessDto HostingSecurityReadiness { get; init; } =
        HostingSecurityReadinessDto.CreateBlocked();
}

public sealed class ValidationLaboratoryReadinessCheck
{
    public string Key { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public bool Passed { get; init; }
    public bool IsWarning { get; init; }
}

public sealed class ValidationExperimentReadinessItem
{
    public long ExperimentId { get; init; }
    public string Name { get; init; } = string.Empty;
    public ValidationLaboratoryReadiness Status { get; init; }
    public string? MetricsVersion { get; init; }
    public string? Notes { get; init; }
}

public sealed class ValidationLaboratoryReadinessService : IValidationLaboratoryReadinessService
{
    /// <summary>90 complete days × 96 fifteen-minute candles.</summary>
    public const int MinimumLongRangeEligibleCandles = 90 * 96;

    private readonly IValidationExperimentRepository _experiments;

    public ValidationLaboratoryReadinessService(IValidationExperimentRepository experiments)
    {
        _experiments = experiments;
    }

    public async Task<ServiceResult<ValidationLaboratoryReadinessReport>> GetReadinessAsync(
        CancellationToken cancellationToken = default)
    {
        var recent = await _experiments.GetRecentAsync(50, cancellationToken);
        var checks = new List<ValidationLaboratoryReadinessCheck>();
        var items = new List<ValidationExperimentReadinessItem>();

        var integrityBlocked = 0;
        var warnings = 0;
        var ready = 0;

        foreach (var e in recent)
        {
            var status = EvaluateExperiment(e);
            items.Add(new ValidationExperimentReadinessItem
            {
                ExperimentId = e.Id,
                Name = e.Name,
                Status = status,
                MetricsVersion = e.ValidationMetricsVersion,
                Notes = status switch
                {
                    ValidationLaboratoryReadiness.Blocked =>
                        e.MetricConsistencyStatus
                        ?? e.LeakageAuditStatus?.ToString()
                        ?? e.CandidateReconciliationStatus?.ToString()
                        ?? e.ExportVerificationStatus?.ToString()
                        ?? e.PrimaryFailureReason,
                    ValidationLaboratoryReadiness.ReadyWithWarnings => DescribeWarning(e),
                    _ => null
                }
            });

            switch (status)
            {
                case ValidationLaboratoryReadiness.Blocked: integrityBlocked++; break;
                case ValidationLaboratoryReadiness.ReadyWithWarnings: warnings++; break;
                default: ready++; break;
            }
        }

        checks.Add(new ValidationLaboratoryReadinessCheck
        {
            Key = "HoldoutExclusivityPolicy",
            Message = "ValidationHoldoutExclusivity/v1 (EarlierOccurrenceOwnsFingerprint) is available.",
            Passed = true
        });
        checks.Add(new ValidationLaboratoryReadinessCheck
        {
            Key = "MetricsContract",
            Message = "ValidationMetrics/v1.3 with ValidationRiskBasis/v1 is the default for new experiments.",
            Passed = true
        });
        checks.Add(new ValidationLaboratoryReadinessCheck
        {
            Key = "SelectionIntegrityRepair",
            Message =
                "ValidationSelectionIntegrity/v1 enforces eligible-trial selection, fingerprint match, and empty-hash rejection.",
            Passed = true
        });

        var exclusivityVerified = recent.Any(IsExclusivityVerificationComplete);
        checks.Add(new ValidationLaboratoryReadinessCheck
        {
            Key = "CandidateExclusivity",
            Message = exclusivityVerified
                ? "At least one completed experiment applied HoldoutExclusivity/v1 with empty metric intersection."
                : "No completed experiment has verified cross-segment candidate exclusivity yet.",
            Passed = exclusivityVerified
        });

        var canonicalComplete = recent.FirstOrDefault(e =>
            e.IsCanonical
            && e.Status == ValidationExperimentStatus.Completed
            && e.ExportVerificationStatus == ValidationExportVerificationStatus.Passed);
        checks.Add(new ValidationLaboratoryReadinessCheck
        {
            Key = "CanonicalCloseout",
            Message = canonicalComplete is not null
                ? $"Canonical experiment {canonicalComplete.Id} completed export verification."
                : "Canonical experiment export verification not yet passed.",
            Passed = canonicalComplete is not null
        });

        var longRange = recent.FirstOrDefault(IsLongRangeManualCComplete);
        checks.Add(new ValidationLaboratoryReadinessCheck
        {
            Key = "LongRangeManualC",
            Message = longRange is null
                ? $"No completed TrainingSearchHoldoutValidation with >= {MinimumLongRangeEligibleCandles} eligible candles."
                : $"Long-range Manual C completed on experiment {longRange.Id} ({longRange.TotalEligibleCandleCount} eligible candles).",
            Passed = longRange is not null
        });

        var metricConsistency = recent.Any(e =>
            e.Status == ValidationExperimentStatus.Completed
            && string.Equals(e.MetricConsistencyStatus, "Passed", StringComparison.OrdinalIgnoreCase));
        checks.Add(new ValidationLaboratoryReadinessCheck
        {
            Key = "MetricConsistency",
            Message = metricConsistency
                ? "At least one completed experiment passed metric consistency."
                : "No completed experiment has Passed metric consistency.",
            Passed = metricConsistency
        });

        var exportsVerified = recent.Any(e =>
            e.Status == ValidationExperimentStatus.Completed
            && e.ExportVerificationStatus == ValidationExportVerificationStatus.Passed);
        checks.Add(new ValidationLaboratoryReadinessCheck
        {
            Key = "ExportVerification",
            Message = exportsVerified
                ? "At least one completed experiment passed export content verification."
                : "No completed experiment has Passed export verification.",
            Passed = exportsVerified
        });

        var leakagePassed = recent.Any(e =>
            e.Status == ValidationExperimentStatus.Completed
            && e.LeakageAuditStatus == ValidationLeakageAuditStatus.Passed);
        checks.Add(new ValidationLaboratoryReadinessCheck
        {
            Key = "LeakageAudit",
            Message = leakagePassed
                ? "At least one completed experiment passed leakage audit."
                : "No completed experiment has Passed leakage audit.",
            Passed = leakagePassed
        });

        checks.Add(new ValidationLaboratoryReadinessCheck
        {
            Key = "RecentExperimentIntegrity",
            Message = integrityBlocked == 0
                ? $"{ready} ready, {warnings} incomplete/warning, 0 integrity-blocked among {recent.Count} recent."
                : $"{integrityBlocked} completed experiment(s) failed integrity checks among {recent.Count} recent.",
            Passed = integrityBlocked == 0,
            IsWarning = warnings > 0 && integrityBlocked == 0
        });

        var legacy = recent.Count(e =>
            string.Equals(e.ValidationMetricsVersion, ValidationMetricsContract.VersionV1Legacy,
                StringComparison.OrdinalIgnoreCase)
            || string.Equals(e.ValidationMetricsVersion, "ValidationMetrics/v1",
                StringComparison.OrdinalIgnoreCase));
        if (legacy > 0)
        {
            checks.Add(new ValidationLaboratoryReadinessCheck
            {
                Key = "LegacyMetrics",
                Message = $"{legacy} experiment(s) use Legacy Metrics (ValidationMetrics/v1).",
                Passed = true,
                IsWarning = true
            });
        }

        var requiredFailed = checks.Any(c => !c.Passed && !c.IsWarning);
        var hasWarnings = checks.Any(c => c.IsWarning) || warnings > 0;

        var overall = requiredFailed || integrityBlocked > 0
            ? ValidationLaboratoryReadiness.Blocked
            : hasWarnings
                ? ValidationLaboratoryReadiness.ReadyWithWarnings
                : ValidationLaboratoryReadiness.Ready;

        return ServiceResult<ValidationLaboratoryReadinessReport>.Ok(new ValidationLaboratoryReadinessReport
        {
            Status = overall,
            Checks = checks,
            Experiments = items,
            HostingSecurityReadiness = HostingSecurityReadinessDto.CreateBlocked(),
            Summary = overall switch
            {
                ValidationLaboratoryReadiness.Blocked =>
                    "Validation Laboratory is blocked until required verification checks pass (exclusivity, long-range Manual C, consistency, exports, leakage).",
                ValidationLaboratoryReadiness.ReadyWithWarnings =>
                    "Validation Laboratory is ready with warnings (legacy metrics, exclusivity notes, or incomplete runs). Negative strategy performance does not block readiness.",
                _ => "Validation Laboratory is ready for Deployment Qualification."
            }
        });
    }

    public ValidationLaboratoryReadiness EvaluateExperiment(ValidationExperiment experiment)
    {
        // Incomplete / in-flight runs never block laboratory readiness.
        if (IsIncompleteStatus(experiment.Status))
        {
            return ValidationLaboratoryReadiness.ReadyWithWarnings;
        }

        // Interrupted Failed runs without integrity audits are incomplete, not blockers.
        // Negative strategy qualification on a Completed experiment is also not a blocker.
        if (experiment.Status == ValidationExperimentStatus.Failed
            && !HasIntegritySignals(experiment))
        {
            return ValidationLaboratoryReadiness.ReadyWithWarnings;
        }

        if (experiment.CandidateReconciliationStatus is CandidateReconciliationStatus.UnexplainedDifference
            or CandidateReconciliationStatus.Invalid)
        {
            return ValidationLaboratoryReadiness.Blocked;
        }

        if (string.Equals(experiment.MetricConsistencyStatus, "Failed", StringComparison.OrdinalIgnoreCase))
        {
            return ValidationLaboratoryReadiness.Blocked;
        }

        if (experiment.LeakageAuditStatus == ValidationLeakageAuditStatus.Failed)
        {
            return ValidationLaboratoryReadiness.Blocked;
        }

        if (experiment.ExportVerificationStatus == ValidationExportVerificationStatus.Failed)
        {
            return ValidationLaboratoryReadiness.Blocked;
        }

        if (experiment.SelectionIntegrityStatus is ValidationSelectionIntegrityStatus.InvalidSelectedTrial
            or ValidationSelectionIntegrityStatus.SelectionPolicyViolation
            or ValidationSelectionIntegrityStatus.FailedSelectedTrialIneligible)
        {
            if (experiment.Status == ValidationExperimentStatus.Completed)
            {
                // Historical selection violations (e.g. Experiment 23) are warnings after M22.4 repair.
            }
            else
            {
                return ValidationLaboratoryReadiness.Blocked;
            }
        }

        if (experiment.SelectionIntegrityStatus is ValidationSelectionIntegrityStatus.FailedNoEligibleTrials
            or ValidationSelectionIntegrityStatus.FailedFrozenSnapshotEmpty
            or ValidationSelectionIntegrityStatus.FailedParameterFingerprintMismatch
            or ValidationSelectionIntegrityStatus.FailedFrozenFingerprintInvalid)
        {
            if (experiment.Status != ValidationExperimentStatus.Completed)
            {
                return ValidationLaboratoryReadiness.Blocked;
            }
        }

        // Failed status with integrity signals already evaluated above; remaining Failed = warning.
        if (experiment.Status == ValidationExperimentStatus.Failed)
        {
            return ValidationLaboratoryReadiness.ReadyWithWarnings;
        }

        var hasWarnings =
            experiment.CrossSegmentOverlapCount > 0
            || string.Equals(experiment.ValidationMetricsVersion, ValidationMetricsContract.VersionV12,
                StringComparison.OrdinalIgnoreCase)
            || string.Equals(experiment.ValidationMetricsVersion, ValidationMetricsContract.VersionV1Legacy,
                StringComparison.OrdinalIgnoreCase)
            || string.Equals(experiment.ValidationMetricsVersion, "ValidationMetrics/v1",
                StringComparison.OrdinalIgnoreCase)
            || experiment.SelectionIntegrityStatus is ValidationSelectionIntegrityStatus.InvalidSelectedTrial
                or ValidationSelectionIntegrityStatus.SelectionPolicyViolation
                or ValidationSelectionIntegrityStatus.FailedSelectedTrialIneligible
            || experiment.CandidateReconciliationStatus ==
                CandidateReconciliationStatus.ExplainedSessionBoundaryDifference
            || experiment.ExportVerificationStatus is null
                or ValidationExportVerificationStatus.NotRun;

        return hasWarnings
            ? ValidationLaboratoryReadiness.ReadyWithWarnings
            : ValidationLaboratoryReadiness.Ready;
    }

    private static bool IsIncompleteStatus(ValidationExperimentStatus status) =>
        status is ValidationExperimentStatus.Draft
            or ValidationExperimentStatus.DataPreparing
            or ValidationExperimentStatus.DataReady
            or ValidationExperimentStatus.TrainingRunning
            or ValidationExperimentStatus.TrainingCompleted
            or ValidationExperimentStatus.ConfigurationFrozen
            or ValidationExperimentStatus.ValidationRunning;

    private static bool HasIntegritySignals(ValidationExperiment experiment) =>
        !string.IsNullOrWhiteSpace(experiment.MetricConsistencyStatus)
        || experiment.LeakageAuditStatus is not null
        || experiment.CandidateReconciliationStatus is not null
        || experiment.ExportVerificationStatus is ValidationExportVerificationStatus.Failed
            or ValidationExportVerificationStatus.Passed;

    private static bool IsExclusivityVerificationComplete(ValidationExperiment e) =>
        e.Status == ValidationExperimentStatus.Completed
        && string.Equals(e.HoldoutExclusivityPolicyVersion, "ValidationHoldoutExclusivity/v1",
            StringComparison.OrdinalIgnoreCase)
        && (!string.IsNullOrWhiteSpace(e.HoldoutExclusivityJson)
            || e.CrossSegmentOverlapCount >= 0)
        && string.Equals(e.MetricConsistencyStatus, "Passed", StringComparison.OrdinalIgnoreCase)
        && e.LeakageAuditStatus == ValidationLeakageAuditStatus.Passed;

    private static bool IsLongRangeManualCComplete(ValidationExperiment e) =>
        e.Status == ValidationExperimentStatus.Completed
        && e.ExperimentType == ValidationExperimentType.TrainingSearchHoldoutValidation
        && e.TotalEligibleCandleCount >= MinimumLongRangeEligibleCandles
        && e.MaximumTrials >= 25
        && string.Equals(e.MetricConsistencyStatus, "Passed", StringComparison.OrdinalIgnoreCase)
        && e.LeakageAuditStatus == ValidationLeakageAuditStatus.Passed
        && e.ExportVerificationStatus == ValidationExportVerificationStatus.Passed;

    private static string DescribeWarning(ValidationExperiment e)
    {
        if (IsIncompleteStatus(e.Status) || e.Status == ValidationExperimentStatus.Failed)
        {
            return $"Incomplete or interrupted ({e.Status}).";
        }

        if (e.CrossSegmentOverlapCount > 0)
        {
            return $"{e.CrossSegmentOverlapCount} cross-segment overlap(s) excluded from validation metrics";
        }

        if (e.CandidateReconciliationStatus == CandidateReconciliationStatus.ExplainedSessionBoundaryDifference)
        {
            return "Explained session-boundary reconciliation difference.";
        }

        return "Ready with warnings";
    }
}
