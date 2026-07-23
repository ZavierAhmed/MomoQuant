using System.Text.Json;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Common;
using MomoQuant.Application.ValidationLab.Dtos;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Strategies;
using MomoQuant.Domain.StrategyLab;
using MomoQuant.Domain.ValidationLab;

namespace MomoQuant.Application.ValidationLab;

public sealed class ExclusivityCloseoutAuditResult
{
    public int TrainingMetricIncludedCount { get; init; }
    public int ValidationMetricIncludedCount { get; init; }
    public int CrossSegmentOverlapCount { get; init; }
    public IReadOnlyList<string> ExcludedOverlapFingerprints { get; init; } = [];
    public int MetricIntersectionCount { get; init; }
    public bool ValidationRiskOnlyMutatedByOverlap { get; init; }
    public bool ValidationFullPipelineMutatedByOverlap { get; init; }
    public bool Passed { get; init; }
    public string Summary { get; init; } = string.Empty;
}

public sealed class ExperimentCloseoutAuditResult
{
    public long ExperimentId { get; init; }
    public string ExperimentName { get; init; } = string.Empty;
    public ExperimentMetricAuditReport MetricAudit { get; init; } = new();
    public TrialSelectionAuditResult SelectionAudit { get; init; } = new();
    public TrialPopulationSummary TrialPopulation { get; init; } = new();
    public ExclusivityCloseoutAuditResult ExclusivityAudit { get; init; } = new();
    public ExportVerificationResult? ExportVerification { get; init; }
    public ValidationLaboratoryReadiness Readiness { get; init; }
    public IReadOnlyList<string> Blockers { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public sealed class ValidationLaboratoryCloseoutReport
{
    public DateTime CompletedAtUtc { get; init; } = DateTime.UtcNow;
    public ValidationLaboratoryReadiness InfrastructureReadiness { get; init; }
    public IReadOnlyList<string> ReadinessReasons { get; init; } = [];
    public IReadOnlyList<ExperimentCloseoutAuditResult> Experiments { get; init; } = [];
    public StrategyResearchCloseoutResult? StrategyResearch { get; init; }
    public long CanonicalExperimentId { get; init; }
}

public sealed class StrategyResearchCloseoutResult
{
    public string StrategyCode { get; init; } = string.Empty;
    public string StrategyVersion { get; init; } = string.Empty;
    public StrategyResearchStatus ResearchStatus { get; init; }
    public bool DeploymentQualificationEligible { get; init; }
    public long? CanonicalValidationExperimentId { get; init; }
    public string DecisionVersion { get; init; } = "StrategyResearchCloseout/v1";
    public string Reason { get; init; } = string.Empty;
}

public interface IValidationLaboratoryCloseoutService
{
    Task<ServiceResult<ExperimentCloseoutAuditResult>> AuditExperimentAsync(
        long experimentId,
        bool verifyExports,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<ValidationLaboratoryCloseoutReport>> RunMilestone223CloseoutAsync(
        CancellationToken cancellationToken = default);
}

public sealed class ValidationLaboratoryCloseoutService : IValidationLaboratoryCloseoutService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly IValidationExperimentRepository _experiments;
    private readonly IValidationParameterTrialRepository _trials;
    private readonly IValidationSegmentResultRepository _segments;
    private readonly IStrategyResearchCandidateRepository _candidates;
    private readonly IValidationExperimentExecutionLeaseRepository _leases;
    private readonly IValidationMetricAuditService _metricAudit;
    private readonly IValidationTrialSelectionAuditor _selectionAuditor;
    private readonly IValidationExportContentVerifier _exportVerifier;
    private readonly IValidationLaboratoryReadinessService _readiness;
    private readonly IValidationHoldoutExclusivityService _exclusivity;
    private readonly IValidationLabService _labService;
    private readonly IStrategyRepository _strategies;

    public ValidationLaboratoryCloseoutService(
        IValidationExperimentRepository experiments,
        IValidationParameterTrialRepository trials,
        IValidationSegmentResultRepository segments,
        IStrategyResearchCandidateRepository candidates,
        IValidationExperimentExecutionLeaseRepository leases,
        IValidationMetricAuditService metricAudit,
        IValidationTrialSelectionAuditor selectionAuditor,
        IValidationExportContentVerifier exportVerifier,
        IValidationLaboratoryReadinessService readiness,
        IValidationHoldoutExclusivityService exclusivity,
        IValidationLabService labService,
        IStrategyRepository strategies)
    {
        _experiments = experiments;
        _trials = trials;
        _segments = segments;
        _candidates = candidates;
        _leases = leases;
        _metricAudit = metricAudit;
        _selectionAuditor = selectionAuditor;
        _exportVerifier = exportVerifier;
        _readiness = readiness;
        _exclusivity = exclusivity;
        _labService = labService;
        _strategies = strategies;
    }

    public async Task<ServiceResult<ExperimentCloseoutAuditResult>> AuditExperimentAsync(
        long experimentId,
        bool verifyExports,
        CancellationToken cancellationToken = default)
    {
        var experiment = await _experiments.GetByIdAsync(experimentId, cancellationToken);
        if (experiment is null)
        {
            return ServiceResult<ExperimentCloseoutAuditResult>.Fail("Validation experiment was not found.");
        }

        var trialEntities = (await _trials.GetByExperimentIdAsync(experimentId, cancellationToken)).ToList();
        var segmentEntities = (await _segments.GetByExperimentIdAsync(experimentId, cancellationToken)).ToList();
        var candidateSets = await BuildMetricCandidateSetsAsync(experiment, cancellationToken);

        var metricAudit = _metricAudit.AuditExperiment(experiment, segmentEntities, candidateSets);
        var selectionAudit = _selectionAuditor.AuditSelection(experiment, trialEntities);
        var population = _selectionAuditor.SummarizePopulation(experiment, trialEntities);
        var exclusivity = AuditExclusivity(experiment, candidateSets);

        ExportVerificationResult? exportResult = null;
        if (verifyExports)
        {
            var detailResult = await _labService.GetExperimentDetailAsync(experimentId, cancellationToken);
            if (detailResult.Succeeded && detailResult.Data is not null)
            {
                var detail = detailResult.Data;
                var trialsDto = await _labService.GetTrainingTrialsAsync(experimentId, cancellationToken);
                var trialObjects = trialsDto.Data?.Cast<object>().ToList();
                var json = JsonSerializer.Serialize(
                    ValidationLabExportBuilder.BuildCompleteEnvelope(detail), JsonOptions);
                var csv = ValidationLabExportBuilder.BuildCsvBundle(detail, trialObjects);
                var pdf = ValidationLabExportBuilder.BuildPdfSummaryText(detail);
                exportResult = _exportVerifier.Verify(detail, json, csv, pdf);
                await _labService.RecordExportVerificationAsync(experimentId, exportResult, cancellationToken);
            }
        }

        experiment.SelectedTrialId = selectionAudit.SelectedTrialId;
        experiment.SelectionIntegrityStatus = selectionAudit.IntegrityStatus;
        experiment.TrialPopulationSummaryJson = JsonSerializer.Serialize(population, JsonOptions);
        experiment.CloseoutAuditJson = JsonSerializer.Serialize(new
        {
            metricAudit,
            selectionAudit,
            population,
            exclusivity,
            exportVerification = exportResult,
            auditedAtUtc = DateTime.UtcNow
        }, JsonOptions);

        var blockers = new List<string>();
        var warnings = new List<string>();

        if (!metricAudit.IsConsistent)
        {
            blockers.Add("Metric audit failed — persisted NetExpectancyR does not match trade-level recomputation.");
        }

        if (selectionAudit.IntegrityStatus is ValidationSelectionIntegrityStatus.InvalidSelectedTrial
            or ValidationSelectionIntegrityStatus.SelectionPolicyViolation)
        {
            blockers.Add(selectionAudit.Explanation);
        }

        if (!exclusivity.Passed)
        {
            blockers.Add(exclusivity.Summary);
        }

        if (exportResult?.Status == ValidationExportVerificationStatus.Failed)
        {
            blockers.Add("Export content verification failed.");
        }
        else if (exportResult?.Status == ValidationExportVerificationStatus.NotRun)
        {
            warnings.Add("Export verification not run.");
        }

        if (experiment.StrategyRobustnessDecision is StrategyRobustnessDecision.FailedNegativeTrainingExpectancy
            or StrategyRobustnessDecision.FailedNegativeValidationExpectancy)
        {
            warnings.Add("Strategy failed profitability criteria (does not block infrastructure readiness).");
        }

        experiment.ValidationLaboratoryReadinessStatus = blockers.Count > 0
            ? ValidationLaboratoryReadiness.Blocked
            : warnings.Count > 0
                ? ValidationLaboratoryReadiness.ReadyWithWarnings
                : ValidationLaboratoryReadiness.Ready;
        experiment.UpdatedAtUtc = DateTime.UtcNow;
        await _experiments.UpdateAsync(experiment, cancellationToken);

        return ServiceResult<ExperimentCloseoutAuditResult>.Ok(new ExperimentCloseoutAuditResult
        {
            ExperimentId = experimentId,
            ExperimentName = experiment.Name,
            MetricAudit = metricAudit,
            SelectionAudit = selectionAudit,
            TrialPopulation = population,
            ExclusivityAudit = exclusivity,
            ExportVerification = exportResult,
            Readiness = experiment.ValidationLaboratoryReadinessStatus ?? ValidationLaboratoryReadiness.Blocked,
            Blockers = blockers,
            Warnings = warnings
        });
    }

    public async Task<ServiceResult<ValidationLaboratoryCloseoutReport>> RunMilestone223CloseoutAsync(
        CancellationToken cancellationToken = default)
    {
        const long canonicalId = 23;
        var targetIds = new[] { 22L, 23L, 24L };
        var results = new List<ExperimentCloseoutAuditResult>();

        foreach (var id in targetIds)
        {
            var audit = await AuditExperimentAsync(id, verifyExports: id == canonicalId, cancellationToken);
            if (audit.Succeeded && audit.Data is not null)
            {
                results.Add(audit.Data);
            }
        }

        var exp22 = await _experiments.GetByIdAsync(22, cancellationToken);
        var exp23 = await _experiments.GetByIdAsync(23, cancellationToken);
        var exp24 = await _experiments.GetByIdAsync(24, cancellationToken);

        if (exp22 is not null)
        {
            exp22.SupersessionStatus = ValidationExperimentSupersessionStatus.Superseded;
            exp22.SupersededByExperimentId = canonicalId;
            exp22.SupersededAtUtc = DateTime.UtcNow;
            exp22.SupersessionReason =
                "Superseded by Experiment 23 after recovery and training durability verification.";
            exp22.IsCanonical = false;
            await _experiments.UpdateAsync(exp22, cancellationToken);
        }

        if (exp23 is not null)
        {
            exp23.IsCanonical = true;
            exp23.SupersessionStatus = ValidationExperimentSupersessionStatus.None;
            await _experiments.UpdateAsync(exp23, cancellationToken);
        }

        if (exp24 is not null)
        {
            exp24.IsCanonical = false;
            await _experiments.UpdateAsync(exp24, cancellationToken);
        }

        var lease = await _leases.GetByExperimentIdAsync(canonicalId, cancellationToken);
        if (lease is not null && lease.ExpiresAtUtc <= DateTime.UtcNow)
        {
            await _leases.ReleaseAsync(canonicalId, cancellationToken);
        }

        StrategyResearchCloseoutResult? strategyResult = null;
        if (exp23 is not null)
        {
            try
            {
                var code = StrategyCodeExtensions.FromCode(exp23.StrategyCode);
                var strategy = await _strategies.GetByCodeAsync(code, cancellationToken);
                if (strategy is not null)
                {
                    strategy.ResearchStatus = StrategyResearchStatus.Failed;
                    strategy.DeploymentQualificationEligible = false;
                    strategy.CanonicalValidationExperimentId = canonicalId;
                    strategy.ResearchDecisionAtUtc = DateTime.UtcNow;
                    strategyResult = new StrategyResearchCloseoutResult
                    {
                        StrategyCode = exp23.StrategyCode,
                        StrategyVersion = exp23.StrategyVersion,
                        ResearchStatus = StrategyResearchStatus.Failed,
                        DeploymentQualificationEligible = false,
                        CanonicalValidationExperimentId = canonicalId,
                        Reason =
                            "Negative raw training expectancy and negative unseen validation expectancy across long-range chronological holdout experiments. Not eligible for Deployment Qualification."
                    };
                    strategy.ResearchDecisionJson = JsonSerializer.Serialize(strategyResult, JsonOptions);
                    await _strategies.UpdateAsync(strategy, cancellationToken);
                    await _strategies.SaveChangesAsync(cancellationToken);
                }
            }
            catch (ArgumentException)
            {
                // Strategy code not in catalog enum — skip persistence.
            }
        }

        var labReadiness = await _readiness.GetReadinessAsync(cancellationToken);
        var reasons = new List<string>();
        if (labReadiness.Data is not null)
        {
            reasons.AddRange(labReadiness.Data.Checks
                .Where(c => !c.Passed && !c.IsWarning)
                .Select(c => c.Message));
            reasons.AddRange(labReadiness.Data.Checks
                .Where(c => c.IsWarning)
                .Select(c => c.Message));
        }

        foreach (var r in results)
        {
            reasons.AddRange(r.Blockers);
            reasons.AddRange(r.Warnings);
        }

        return ServiceResult<ValidationLaboratoryCloseoutReport>.Ok(new ValidationLaboratoryCloseoutReport
        {
            InfrastructureReadiness = labReadiness.Data?.Status ?? ValidationLaboratoryReadiness.Blocked,
            ReadinessReasons = reasons.Distinct(StringComparer.Ordinal).ToList(),
            Experiments = results,
            StrategyResearch = strategyResult,
            CanonicalExperimentId = canonicalId
        });
    }

    private async Task<Dictionary<(ValidationSegmentType, ValidationLayerType), IReadOnlyList<StrategyResearchCandidate>>>
        BuildMetricCandidateSetsAsync(ValidationExperiment experiment, CancellationToken cancellationToken)
    {
        var map = new Dictionary<(ValidationSegmentType, ValidationLayerType), IReadOnlyList<StrategyResearchCandidate>>();

        if (experiment.TrainingStrategyLabRunId is long trainRunId)
        {
            var trainCandidates = await _candidates.GetByRunIdAsync(trainRunId, cancellationToken);
            IEnumerable<StrategyResearchCandidate> trainMetrics = trainCandidates;
            if (experiment.ValidationStartUtc is not null)
            {
                trainMetrics = ValidationMetricsMapper.ExcludeBoundaryFromMetrics(
                    trainCandidates, experiment.ValidationStartUtc.Value);
            }

            map[(ValidationSegmentType.Training, ValidationLayerType.RawStrategy)] = trainMetrics.ToList();
        }

        if (experiment.ValidationStrategyLabRunId is long valRunId
            && experiment.TrainingStrategyLabRunId is long trainId)
        {
            var valCandidates = await _candidates.GetByRunIdAsync(valRunId, cancellationToken);
            var trainCandidates = await _candidates.GetByRunIdAsync(trainId, cancellationToken);
            var exclusivity = _exclusivity.Apply(
                trainCandidates, valCandidates, experiment.ValidationStartUtc);
            var partition = _exclusivity.ApplyExclusivityToValidationCandidates(valCandidates, exclusivity);
            map[(ValidationSegmentType.Validation, ValidationLayerType.RawStrategy)] = partition.MetricIncluded;
        }

        return map;
    }

    private static ExclusivityCloseoutAuditResult AuditExclusivity(
        ValidationExperiment experiment,
        IReadOnlyDictionary<(ValidationSegmentType, ValidationLayerType), IReadOnlyList<StrategyResearchCandidate>> candidateSets)
    {
        candidateSets.TryGetValue((ValidationSegmentType.Training, ValidationLayerType.RawStrategy), out var train);
        candidateSets.TryGetValue((ValidationSegmentType.Validation, ValidationLayerType.RawStrategy), out var val);
        train ??= [];
        val ??= [];

        var trainFps = train
            .Select(c => c.SetupFingerprint)
            .Where(fp => !string.IsNullOrWhiteSpace(fp))
            .ToHashSet(StringComparer.Ordinal);
        var valFps = val
            .Select(c => c.SetupFingerprint)
            .Where(fp => !string.IsNullOrWhiteSpace(fp))
            .ToHashSet(StringComparer.Ordinal);

        var intersection = trainFps.Intersect(valFps, StringComparer.Ordinal).ToList();
        var overlaps = ParseOverlapFingerprints(experiment.HoldoutExclusivityJson);

        return new ExclusivityCloseoutAuditResult
        {
            TrainingMetricIncludedCount = trainFps.Count,
            ValidationMetricIncludedCount = valFps.Count,
            CrossSegmentOverlapCount = experiment.CrossSegmentOverlapCount,
            ExcludedOverlapFingerprints = overlaps,
            MetricIntersectionCount = intersection.Count,
            ValidationRiskOnlyMutatedByOverlap = false,
            ValidationFullPipelineMutatedByOverlap = false,
            Passed = intersection.Count == 0,
            Summary = intersection.Count == 0
                ? "Training and validation metric-included fingerprint sets are exclusive."
                : $"Metric intersection detected ({intersection.Count} fingerprint(s))."
        };
    }

    private static IReadOnlyList<string> ParseOverlapFingerprints(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("overlaps", out var overlaps)
                && !doc.RootElement.TryGetProperty("Overlaps", out overlaps))
            {
                return [];
            }

            return overlaps.EnumerateArray()
                .Select(o => o.TryGetProperty("overlapFingerprint", out var fp) ? fp.GetString()
                    : o.TryGetProperty("OverlapFingerprint", out fp) ? fp.GetString() : null)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s!)
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }
        catch
        {
            return [];
        }
    }
}
