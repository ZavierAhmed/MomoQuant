using System.Text.Json;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.MarketData;
using MomoQuant.Application.Strategies;
using MomoQuant.Application.ValidationLab;
using MomoQuant.Domain.Enums;

namespace MomoQuant.Application.PaperTrading;

public static class PaperDeploymentQualificationCodes
{
    public const string UseClassInvalid = "PAPER_USE_CLASS_INVALID";
    public const string LiveModeRequired = "PAPER_DEPLOYMENT_LIVE_MODE_REQUIRED";
    public const string SingleScopeRequired = "PAPER_DEPLOYMENT_SINGLE_SCOPE_REQUIRED";
    public const string ParameterSetRequired = "PAPER_DEPLOYMENT_PARAMETER_SET_REQUIRED";
    public const string ParameterSetNotFound = "PAPER_DEPLOYMENT_PARAMETER_SET_NOT_FOUND";
    public const string NotQualified = "PAPER_DEPLOYMENT_NOT_QUALIFIED";
    public const string ProvenanceIncomplete = "PAPER_DEPLOYMENT_PROVENANCE_INCOMPLETE";
    public const string EvidenceVersionUnsupported = "PAPER_DEPLOYMENT_EVIDENCE_VERSION_UNSUPPORTED";
    public const string FingerprintMismatch = "PAPER_DEPLOYMENT_FINGERPRINT_MISMATCH";
    public const string ScopeMismatch = "PAPER_DEPLOYMENT_SCOPE_MISMATCH";
    public const string ExperimentInvalid = "PAPER_DEPLOYMENT_EXPERIMENT_INVALID";
    public const string TrialInvalid = "PAPER_DEPLOYMENT_TRIAL_INVALID";
    public const string AuditIncomplete = "PAPER_DEPLOYMENT_AUDIT_INCOMPLETE";
    public const string VerdictNotPassed = "PAPER_DEPLOYMENT_VERDICT_NOT_PASSED";
    public const string StrategyIneligible = "PAPER_DEPLOYMENT_STRATEGY_INELIGIBLE";
    public const string CanonicalMismatch = "PAPER_DEPLOYMENT_CANONICAL_MISMATCH";
    public const string BindingConflict = "PAPER_DEPLOYMENT_BINDING_CONFLICT";
    public const string RuntimeActivationFailed = "PAPER_RUNTIME_ACTIVATION_FAILED";
}

public sealed record PaperDeploymentStoredBinding(
    long ParameterSetId,
    long StrategyId,
    long SymbolId,
    string Timeframe,
    long SourceExperimentId,
    long SourceTrialId,
    string ParameterFingerprint,
    string EvidenceVersion);

public sealed class PaperDeploymentQualificationResult
{
    public bool Succeeded { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public long ParameterSetId { get; init; }
    public long StrategyId { get; init; }
    public long SymbolId { get; init; }
    public required string Timeframe { get; init; }
    public long SourceExperimentId { get; init; }
    public long SourceTrialId { get; init; }
    public required string ParameterFingerprint { get; init; }
    public required string EvidenceVersion { get; init; }
    public DateTime VerifiedAtUtc { get; init; }
    public required IReadOnlyDictionary<string, string> FrozenParameters { get; init; }

    public static PaperDeploymentQualificationResult Fail(string code, string message) => new()
    {
        Succeeded = false,
        ErrorCode = code,
        ErrorMessage = message,
        Timeframe = string.Empty,
        ParameterFingerprint = string.Empty,
        EvidenceVersion = string.Empty,
        FrozenParameters = new Dictionary<string, string>()
    };
}

public interface IPaperDeploymentQualificationVerifier
{
    Task<PaperDeploymentQualificationResult> VerifyAsync(
        long parameterSetId,
        long strategyId,
        long symbolId,
        string timeframe,
        PaperDeploymentStoredBinding? storedBinding = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Reloads and recomputes every durable Validation Lab publication gate used by deployment simulation.
/// It is intentionally read-only and never repairs or republishes qualification evidence.
/// </summary>
public sealed class PaperDeploymentQualificationVerifier : IPaperDeploymentQualificationVerifier
{
    private readonly IStrategyParameterSetRepository _parameterSets;
    private readonly IValidationExperimentRepository _experiments;
    private readonly IValidationParameterTrialRepository _trials;
    private readonly IStrategyRepository _strategies;
    private readonly IValidationParameterFingerprintService _fingerprints;
    private readonly IValidationAuthoritativeAuditQualificationEvaluator _auditEvaluator;
    private readonly IValidationVerdictService _verdicts;
    private readonly TimeProvider _timeProvider;

    public PaperDeploymentQualificationVerifier(
        IStrategyParameterSetRepository parameterSets,
        IValidationExperimentRepository experiments,
        IValidationParameterTrialRepository trials,
        IStrategyRepository strategies,
        IValidationParameterFingerprintService fingerprints,
        IValidationAuthoritativeAuditQualificationEvaluator auditEvaluator,
        IValidationVerdictService verdicts,
        TimeProvider timeProvider)
    {
        _parameterSets = parameterSets;
        _experiments = experiments;
        _trials = trials;
        _strategies = strategies;
        _fingerprints = fingerprints;
        _auditEvaluator = auditEvaluator;
        _verdicts = verdicts;
        _timeProvider = timeProvider;
    }

    public async Task<PaperDeploymentQualificationResult> VerifyAsync(
        long parameterSetId,
        long strategyId,
        long symbolId,
        string timeframe,
        PaperDeploymentStoredBinding? storedBinding = null,
        CancellationToken cancellationToken = default)
    {
        var parameterSet = await _parameterSets.GetByIdAsync(parameterSetId, cancellationToken).ConfigureAwait(false);
        if (parameterSet is null)
        {
            return Fail(PaperDeploymentQualificationCodes.ParameterSetNotFound,
                "The deployment-qualified parameter set was not found.");
        }

        if (parameterSet.Source != StrategyParameterSetSource.ValidationLab
            || !parameterSet.IsApproved
            || parameterSet.QualificationStatus != ParameterSetQualificationStatus.DeploymentQualified
            || parameterSet.IsDefaultForStrategy
            || parameterSet.IsDefaultForSymbolTimeframe)
        {
            return Fail(PaperDeploymentQualificationCodes.NotQualified,
                "The selected parameter set is not an exact Validation Lab deployment-qualified publication.");
        }

        if (parameterSet.QualificationSourceExperimentId is null
            || parameterSet.QualificationSourceTrialId is null
            || string.IsNullOrWhiteSpace(parameterSet.QualificationParameterFingerprint)
            || parameterSet.QualifiedAtUtc is null)
        {
            return Fail(PaperDeploymentQualificationCodes.ProvenanceIncomplete,
                "The selected parameter set has incomplete deployment-qualification provenance.");
        }

        if (!string.Equals(
                parameterSet.QualificationEvidenceVersion,
                ValidationParameterSetPublicationService.EvidenceVersion,
                StringComparison.Ordinal))
        {
            return Fail(PaperDeploymentQualificationCodes.EvidenceVersionUnsupported,
                "The selected parameter set uses an unsupported qualification-evidence version.");
        }

        if (!TryCanonicalTimeframe(timeframe, out var requestedTimeframe)
            || parameterSet.SymbolId != symbolId
            || !TryCanonicalTimeframe(parameterSet.Timeframe, out var parameterTimeframe)
            || parameterTimeframe != requestedTimeframe)
        {
            return Fail(PaperDeploymentQualificationCodes.ScopeMismatch,
                "The selected parameter set does not match the requested symbol and timeframe.");
        }

        if (storedBinding is not null
            && (storedBinding.ParameterSetId != parameterSetId
                || storedBinding.StrategyId != strategyId
                || storedBinding.SymbolId != symbolId
                || !TryCanonicalTimeframe(storedBinding.Timeframe, out var boundTimeframe)
                || boundTimeframe != requestedTimeframe
                || storedBinding.SourceExperimentId != parameterSet.QualificationSourceExperimentId
                || storedBinding.SourceTrialId != parameterSet.QualificationSourceTrialId))
        {
            return Fail(PaperDeploymentQualificationCodes.BindingConflict,
                "The durable deployment-simulation binding conflicts with current qualification provenance.");
        }

        if (storedBinding is not null
            && !string.Equals(storedBinding.EvidenceVersion,
                ValidationParameterSetPublicationService.EvidenceVersion,
                StringComparison.Ordinal))
        {
            return Fail(PaperDeploymentQualificationCodes.EvidenceVersionUnsupported,
                "The durable deployment-simulation binding uses an unsupported evidence version.");
        }

        var experiment = await _experiments
            .GetByIdAsync(parameterSet.QualificationSourceExperimentId.Value, cancellationToken)
            .ConfigureAwait(false);
        if (experiment is null)
        {
            return Fail(PaperDeploymentQualificationCodes.ProvenanceIncomplete,
                "The source Validation Lab experiment was not found.");
        }

        var experimentFailure = ValidateExperiment(experiment);
        if (experimentFailure is not null)
        {
            return experimentFailure;
        }

        if (experiment.SymbolId != symbolId
            || !TryCanonicalTimeframe(experiment.Timeframe, out var experimentTimeframe)
            || experimentTimeframe != requestedTimeframe)
        {
            return Fail(PaperDeploymentQualificationCodes.ScopeMismatch,
                "The Validation Lab publication scope does not match the durable paper scope.");
        }

        if (experiment.SelectedTrialId != parameterSet.QualificationSourceTrialId)
        {
            return Fail(PaperDeploymentQualificationCodes.TrialInvalid,
                "The selected trial no longer matches the published qualification provenance.");
        }

        var trial = (await _trials.GetByExperimentIdAsync(experiment.Id, cancellationToken).ConfigureAwait(false))
            .SingleOrDefault(item => item.Id == parameterSet.QualificationSourceTrialId.Value);
        if (trial is null
            || trial.ValidationExperimentId != experiment.Id
            || trial.Id != experiment.SelectedTrialId
            || trial.Status != ValidationTrialStatus.Completed
            || !string.Equals(trial.GuardrailDecision, "Passed", StringComparison.Ordinal)
            || trial.TrialRankEligibility != ValidationTrialRankEligibility.Eligible)
        {
            return Fail(PaperDeploymentQualificationCodes.TrialInvalid,
                "The source Validation Lab trial is not completed, eligible, and identity-bound.");
        }

        if (trial.AuditCompletionStatus != ValidationAuditCompletionStatus.Complete
            || trial.AuthoritativeAuditExecutionId is null)
        {
            return Fail(PaperDeploymentQualificationCodes.AuditIncomplete,
                "The source trial has incomplete authoritative audit evidence.");
        }

        var fingerprintResult = ValidateFingerprints(parameterSet, experiment, trial, storedBinding);
        if (fingerprintResult is not null)
        {
            return fingerprintResult;
        }

        var rules = ValidationVerdictService.DeserializeRules(experiment.QualificationRuleResultsJson);
        if (rules is null)
        {
            return Fail(PaperDeploymentQualificationCodes.VerdictNotPassed,
                "Structured qualification-rule evidence is missing or invalid.");
        }

        var recalculated = _verdicts.Recalculate(rules);
        if (experiment.StrategyRobustnessDecision != StrategyRobustnessDecision.Passed
            || recalculated.Decision != StrategyRobustnessDecision.Passed
            || recalculated.Decision != experiment.StrategyRobustnessDecision)
        {
            return Fail(PaperDeploymentQualificationCodes.VerdictNotPassed,
                "The stored and recalculated Validation Lab verdicts are not both exact passes.");
        }

        var strategy = await _strategies.GetByIdAsync(strategyId, cancellationToken).ConfigureAwait(false);
        if (strategy is null
            || !strategy.IsEnabled
            || !strategy.DeploymentQualificationEligible
            || !CanonicalStrategyPortfolio.CanExecute(strategy.Code))
        {
            return Fail(PaperDeploymentQualificationCodes.StrategyIneligible,
                "The selected strategy is not enabled and deployment-qualification eligible.");
        }

        if (!CanonicalStrategyPortfolio.TryParseCanonical(experiment.StrategyCode, out var experimentCode)
            || strategy.Code != experimentCode
            || !string.Equals(strategy.Code.ToCode(), parameterSet.StrategyCode, StringComparison.Ordinal)
            || !string.Equals(strategy.Code.ToCode(), experiment.StrategyCode, StringComparison.Ordinal)
            || !string.Equals(strategy.Version, experiment.StrategyVersion, StringComparison.Ordinal))
        {
            return Fail(PaperDeploymentQualificationCodes.ScopeMismatch,
                "The selected strategy code or version does not match the qualified publication.");
        }

        if (!experiment.IsCanonical || strategy.CanonicalValidationExperimentId != experiment.Id)
        {
            return Fail(PaperDeploymentQualificationCodes.CanonicalMismatch,
                "The selected strategy and Validation Lab experiment are not the canonical deployment qualification.");
        }

        var audit = await _auditEvaluator.EvaluateTrialAsync(experiment, trial, cancellationToken).ConfigureAwait(false);
        if (!audit.IsApplicable
            || !audit.IsQualificationEligible
            || audit.CompletenessCode != ValidationAuditCompletenessCode.Complete
            || audit.AuthoritativeStatus != ValidationAuditExecutionStatus.Completed
            || audit.TrialId != trial.Id
            || audit.AuditExecutionId != trial.AuthoritativeAuditExecutionId
            || audit.ScopeExecutionId is null
            || audit.Completeness is not { IsComplete: true, IsAuthoritative: true })
        {
            return Fail(PaperDeploymentQualificationCodes.AuditIncomplete,
                "Authoritative audit evidence is incomplete or not verifier-confirmed.");
        }

        var canonical = _fingerprints.ComputeCanonicalFromSnapshotJson(parameterSet.ParametersJson);
        var frozenParameters = JsonSerializer.Deserialize<Dictionary<string, string>>(parameterSet.ParametersJson)
            ?? new Dictionary<string, string>();

        return new PaperDeploymentQualificationResult
        {
            Succeeded = true,
            ParameterSetId = parameterSet.Id,
            StrategyId = strategy.Id,
            SymbolId = symbolId,
            Timeframe = TimeframeParser.ToApiString(requestedTimeframe),
            SourceExperimentId = experiment.Id,
            SourceTrialId = trial.Id,
            ParameterFingerprint = canonical.ShortDisplayHash,
            EvidenceVersion = ValidationParameterSetPublicationService.EvidenceVersion,
            VerifiedAtUtc = _timeProvider.GetUtcNow().UtcDateTime,
            FrozenParameters = new Dictionary<string, string>(frozenParameters, StringComparer.OrdinalIgnoreCase)
        };
    }

    private PaperDeploymentQualificationResult? ValidateFingerprints(
        Domain.Strategies.StrategyParameterSet parameterSet,
        Domain.ValidationLab.ValidationExperiment experiment,
        Domain.ValidationLab.ValidationParameterTrial trial,
        PaperDeploymentStoredBinding? storedBinding)
    {
        try
        {
            var parameterCanonical = _fingerprints.ComputeCanonicalFromSnapshotJson(parameterSet.ParametersJson);
            var frozenCanonical = _fingerprints.ComputeCanonicalFromSnapshotJson(
                experiment.FrozenStrategyParameterSnapshotJson!);
            var selectedCanonical = _fingerprints.ComputeCanonicalFromSnapshotJson(trial.ParameterSnapshotJson);
            var experimentSelectedCanonical = _fingerprints.ComputeCanonicalFromSnapshotJson(
                experiment.SelectedTrialParameterSnapshotJson!);

            if (parameterCanonical.ValidationStatus != FrozenSnapshotValidationStatus.Valid
                || frozenCanonical.ValidationStatus != FrozenSnapshotValidationStatus.Valid
                || selectedCanonical.ValidationStatus != FrozenSnapshotValidationStatus.Valid
                || experimentSelectedCanonical.ValidationStatus != FrozenSnapshotValidationStatus.Valid
                || !string.Equals(parameterSet.ParametersJson,
                    experiment.FrozenStrategyParameterSnapshotJson,
                    StringComparison.Ordinal)
                || !string.Equals(parameterCanonical.CanonicalSnapshot, frozenCanonical.CanonicalSnapshot, StringComparison.Ordinal)
                || !string.Equals(parameterCanonical.CanonicalSnapshot, selectedCanonical.CanonicalSnapshot, StringComparison.Ordinal)
                || !string.Equals(parameterCanonical.CanonicalSnapshot, experimentSelectedCanonical.CanonicalSnapshot, StringComparison.Ordinal)
                || !string.Equals(parameterCanonical.ShortDisplayHash, parameterSet.QualificationParameterFingerprint, StringComparison.Ordinal)
                || !string.Equals(parameterCanonical.ShortDisplayHash, experiment.FrozenParameterFingerprint, StringComparison.Ordinal)
                || !string.Equals(parameterCanonical.ShortDisplayHash, experiment.SelectedTrialParameterFingerprint, StringComparison.Ordinal)
                || !string.Equals(parameterCanonical.ShortDisplayHash, trial.ParameterFingerprint, StringComparison.Ordinal)
                || storedBinding is not null
                    && !string.Equals(parameterCanonical.ShortDisplayHash, storedBinding.ParameterFingerprint, StringComparison.Ordinal))
            {
                return Fail(PaperDeploymentQualificationCodes.FingerprintMismatch,
                    "The published, frozen, selected-trial, or durable-binding fingerprints do not match.");
            }
        }
        catch (JsonException)
        {
            return Fail(PaperDeploymentQualificationCodes.FingerprintMismatch,
                "Qualified parameter evidence is not valid canonical JSON.");
        }

        return null;
    }

    private static PaperDeploymentQualificationResult? ValidateExperiment(
        Domain.ValidationLab.ValidationExperiment experiment)
    {
        if (experiment.ExperimentType != ValidationExperimentType.TrainingSearchHoldoutValidation
            || experiment.Status != ValidationExperimentStatus.Completed
            || experiment.ValidationRevealStatus != ValidationRevealStatus.Revealed
            || !experiment.IsQualificationCapable
            || experiment.SupersessionStatus != ValidationExperimentSupersessionStatus.None
            || experiment.FrozenSnapshotValidationStatus != FrozenSnapshotValidationStatus.Valid
            || experiment.SelectionIntegrityStatus != ValidationSelectionIntegrityStatus.Passed
            || experiment.TrialSegmentReconciliationStatus != ValidationTrialSegmentReconciliationStatus.Matched
            || experiment.SelectedTrialId is null
            || string.IsNullOrWhiteSpace(experiment.FrozenStrategyParameterSnapshotJson)
            || string.IsNullOrWhiteSpace(experiment.SelectedTrialParameterSnapshotJson)
            || string.IsNullOrWhiteSpace(experiment.FrozenParameterFingerprint)
            || string.IsNullOrWhiteSpace(experiment.SelectedTrialParameterFingerprint))
        {
            return Fail(PaperDeploymentQualificationCodes.ExperimentInvalid,
                "The source Validation Lab experiment no longer satisfies publication gates.");
        }

        return null;
    }

    private static bool TryCanonicalTimeframe(string? value, out Timeframe timeframe) =>
        TimeframeParser.TryParse(value, out timeframe);

    private static PaperDeploymentQualificationResult Fail(string code, string message) =>
        PaperDeploymentQualificationResult.Fail(code, message);
}
