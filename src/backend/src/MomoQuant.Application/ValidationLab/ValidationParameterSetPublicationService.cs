using Microsoft.Extensions.Logging;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Audit;
using MomoQuant.Application.Common;
using MomoQuant.Application.Optimization;
using MomoQuant.Application.Optimization.Dtos;
using MomoQuant.Application.Strategies;
using MomoQuant.Application.ValidationLab.Dtos;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Strategies;
using MomoQuant.Domain.ValidationLab;

namespace MomoQuant.Application.ValidationLab;

public static class ValidationParameterSetPublicationCodes
{
    public const string ExperimentNotFound = "VALIDATION_PUBLICATION_EXPERIMENT_NOT_FOUND";
    public const string NotCompleted = "VALIDATION_PUBLICATION_NOT_COMPLETED";
    public const string NotPassed = "VALIDATION_PUBLICATION_NOT_PASSED";
    public const string NotQualificationCapable = "VALIDATION_PUBLICATION_NOT_QUALIFICATION_CAPABLE";
    public const string ExperimentTypeUnsupported = "VALIDATION_PUBLICATION_EXPERIMENT_TYPE_UNSUPPORTED";
    public const string SelectionInvalid = "VALIDATION_PUBLICATION_SELECTION_INVALID";
    public const string TrialInvalid = "VALIDATION_PUBLICATION_TRIAL_INVALID";
    public const string AuditIncomplete = "VALIDATION_PUBLICATION_AUDIT_INCOMPLETE";
    public const string FingerprintMismatch = "VALIDATION_PUBLICATION_FINGERPRINT_MISMATCH";
    public const string StrategyIneligible = "VALIDATION_PUBLICATION_STRATEGY_INELIGIBLE";
    public const string ExistingCanonicalQualification = "VALIDATION_PUBLICATION_EXISTING_CANONICAL_QUALIFICATION";
    public const string ProvenanceConflict = "VALIDATION_PUBLICATION_PROVENANCE_CONFLICT";
}

public sealed class ValidationPublicationPersistenceException : Exception
{
    public ValidationPublicationPersistenceException(string code, string message, Exception? innerException = null)
        : base(message, innerException) => Code = code;

    public string Code { get; }
}

public interface IValidationParameterSetPublicationService
{
    Task<ServiceResult<StrategyParameterSetDto>> PublishAsync(
        long experimentId,
        PublishValidationParameterSetRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The only B1C6B production writer for DeploymentQualified parameter sets.
/// All source entities are locked and durably reloaded before evidence is evaluated.
/// </summary>
public sealed class ValidationParameterSetPublicationService : IValidationParameterSetPublicationService
{
    public const string EvidenceVersion = "ValidationLabPublication/v1";
    public const string AuditAction = RequiredAuditActions.ParameterSetDeploymentQualified;

    private readonly IValidationParameterSetPublicationStore _store;
    private readonly IValidationParameterFingerprintService _fingerprints;
    private readonly IValidationAuthoritativeAuditQualificationEvaluator _auditEvaluator;
    private readonly IValidationVerdictService _verdicts;
    private readonly ICurrentUserService _currentUser;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ValidationParameterSetPublicationService> _logger;
    private readonly IRequiredAuditWriter? _requiredAuditWriter;

    public ValidationParameterSetPublicationService(
        IValidationParameterSetPublicationStore store,
        IValidationParameterFingerprintService fingerprints,
        IValidationAuthoritativeAuditQualificationEvaluator auditEvaluator,
        IValidationVerdictService verdicts,
        ICurrentUserService currentUser,
        TimeProvider timeProvider,
        ILogger<ValidationParameterSetPublicationService> logger,
        IRequiredAuditWriter? requiredAuditWriter = null)
    {
        _store = store;
        _fingerprints = fingerprints;
        _auditEvaluator = auditEvaluator;
        _verdicts = verdicts;
        _currentUser = currentUser;
        _timeProvider = timeProvider;
        _logger = logger;
        _requiredAuditWriter = requiredAuditWriter;
    }

    public async Task<ServiceResult<StrategyParameterSetDto>> PublishAsync(
        long experimentId,
        PublishValidationParameterSetRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.DisplayName?.Length > 200)
        {
            return Fail(ValidationParameterSetPublicationCodes.SelectionInvalid,
                "The publication display name is too long.");
        }

        try
        {
            return await _store.ExecuteInTransactionAsync(
                transactionToken => PublishLockedAsync(experimentId, request, transactionToken),
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (AuditEvidenceException ex)
        {
            _logger.LogError(
                "Required publication audit evidence failed for experiment {ExperimentId} with code {Code}.",
                experimentId,
                ex.Code);
            return Fail(ex.Code, "Required publication audit evidence could not be committed.");
        }
        catch (ValidationPublicationPersistenceException ex)
        {
            _logger.LogWarning(ex, "Validation Lab publication persistence conflict for experiment {ExperimentId}.", experimentId);
            return Fail(ex.Code, "The publication conflicted with durable qualification provenance.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Validation Lab publication failed for experiment {ExperimentId}.", experimentId);
            return Fail(ValidationParameterSetPublicationCodes.ProvenanceConflict,
                "The publication could not be committed safely.");
        }
    }

    private async Task<ServiceResult<StrategyParameterSetDto>> PublishLockedAsync(
        long experimentId,
        PublishValidationParameterSetRequest request,
        CancellationToken cancellationToken)
    {
        var experiment = await _store.LockExperimentAsync(experimentId, cancellationToken).ConfigureAwait(false);
        if (experiment is null)
        {
            return Fail(ValidationParameterSetPublicationCodes.ExperimentNotFound,
                "The Validation Lab experiment was not found.");
        }

        if (experiment.SelectedTrialId is null)
        {
            return Fail(ValidationParameterSetPublicationCodes.SelectionInvalid,
                "The experiment has no durable selected trial.");
        }

        var trial = await _store.LockTrialAsync(experiment.SelectedTrialId.Value, cancellationToken).ConfigureAwait(false);
        if (trial is null)
        {
            return Fail(ValidationParameterSetPublicationCodes.TrialInvalid,
                "The selected Validation Lab trial was not found.");
        }

        var strategy = await _store.LockStrategyByCodeAsync(experiment.StrategyCode, cancellationToken).ConfigureAwait(false);
        var existing = await _store.LockPublicationByExperimentAsync(experiment.Id, cancellationToken).ConfigureAwait(false);
        var qualifiedForStrategy = await _store
            .LockQualifiedPublicationsByStrategyAsync(experiment.StrategyCode, cancellationToken)
            .ConfigureAwait(false);
        var canonicalExperiments = await _store
            .ListCanonicalExperimentsAsync(experiment.StrategyCode, cancellationToken)
            .ConfigureAwait(false);

        var experimentFailure = ValidateExperiment(experiment);
        if (experimentFailure is not null)
        {
            return experimentFailure;
        }

        var trialFailure = ValidateTrial(experiment, trial);
        if (trialFailure is not null)
        {
            return trialFailure;
        }

        ParameterFingerprintResult frozenCanonical;
        ParameterFingerprintResult selectedCanonical;
        try
        {
            frozenCanonical = _fingerprints.ComputeCanonicalFromSnapshotJson(
                experiment.FrozenStrategyParameterSnapshotJson!);
            selectedCanonical = _fingerprints.ComputeCanonicalFromSnapshotJson(trial.ParameterSnapshotJson);
        }
        catch
        {
            return Fail(ValidationParameterSetPublicationCodes.FingerprintMismatch,
                "The frozen parameter evidence is invalid.");
        }

        var fingerprintFailure = ValidateFingerprintBinding(experiment, trial, frozenCanonical, selectedCanonical);
        if (fingerprintFailure is not null)
        {
            return fingerprintFailure;
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
            return Fail(ValidationParameterSetPublicationCodes.AuditIncomplete,
                "Authoritative audit evidence is incomplete or not verifier-confirmed.");
        }

        var persistedRules = ValidationVerdictService.DeserializeRules(experiment.QualificationRuleResultsJson);
        if (persistedRules is null)
        {
            return Fail(ValidationParameterSetPublicationCodes.NotPassed,
                "Structured qualification-rule evidence is missing or invalid.");
        }

        var recalculatedVerdict = _verdicts.Recalculate(persistedRules);
        if (experiment.StrategyRobustnessDecision != StrategyRobustnessDecision.Passed
            || recalculatedVerdict.Decision != StrategyRobustnessDecision.Passed
            || recalculatedVerdict.Decision != experiment.StrategyRobustnessDecision)
        {
            return Fail(ValidationParameterSetPublicationCodes.NotPassed,
                "The durable Validation Lab verdict is not an exact pass.");
        }

        var strategyFailure = ValidateStrategy(experiment, strategy);
        if (strategyFailure is not null)
        {
            return strategyFailure;
        }

        if (existing is not null)
        {
            return ExistingPublicationResult(existing, experiment, trial, strategy!, frozenCanonical.ShortDisplayHash);
        }

        if (qualifiedForStrategy.Any(item => item.QualificationSourceExperimentId != experiment.Id)
            || strategy!.CanonicalValidationExperimentId is long canonicalId && canonicalId != experiment.Id
            || canonicalExperiments.Any(item => item.Id != experiment.Id))
        {
            return Fail(ValidationParameterSetPublicationCodes.ExistingCanonicalQualification,
                "A different canonical deployment-qualified publication already exists for this strategy.");
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var parameterSet = new StrategyParameterSet
        {
            Name = ResolveName(request.DisplayName, experiment.Name),
            StrategyCode = experiment.StrategyCode,
            SymbolId = experiment.SymbolId,
            Timeframe = experiment.Timeframe,
            ParametersJson = experiment.FrozenStrategyParameterSnapshotJson!,
            Source = StrategyParameterSetSource.ValidationLab,
            IsApproved = true,
            QualificationStatus = ParameterSetQualificationStatus.DeploymentQualified,
            QualificationSourceExperimentId = experiment.Id,
            QualificationSourceTrialId = trial.Id,
            QualificationParameterFingerprint = frozenCanonical.ShortDisplayHash,
            QualificationEvidenceVersion = EvidenceVersion,
            QualifiedAtUtc = now,
            ApprovedAtUtc = now,
            IsDefaultForStrategy = false,
            IsDefaultForSymbolTimeframe = false,
            CreatedAtUtc = now
        };

        experiment.IsCanonical = true;
        experiment.UpdatedAtUtc = now;
        strategy.CanonicalValidationExperimentId = experiment.Id;
        strategy.DeploymentQualificationEligible = true;
        strategy.UpdatedAtUtc = now;

        _store.AddParameterSet(parameterSet);
        await _store.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        if (_requiredAuditWriter is null)
        {
            throw new AuditEvidenceException(
                AuditEvidenceCodes.Unavailable,
                "Required publication audit evidence is unavailable.");
        }

        _requiredAuditWriter.AttachRequired(
            new RequiredAuditRequest(
                AuditAction,
                nameof(StrategyParameterSet),
                parameterSet.Id,
                _currentUser.UserId,
                null,
                LogSeverity.Info,
                new ParameterSetPublicationAuditMetadata(
                    parameterSet.Id,
                    parameterSet.StrategyCode,
                    experiment.Id,
                    trial.Id,
                    parameterSet.QualificationParameterFingerprint!,
                    parameterSet.QualificationEvidenceVersion!,
                    parameterSet.QualifiedAtUtc!.Value),
                now),
            cancellationToken);
        await _store.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return ServiceResult<StrategyParameterSetDto>.Ok(StrategyParameterSetService.Map(parameterSet));
    }

    private static ServiceResult<StrategyParameterSetDto>? ValidateExperiment(ValidationExperiment experiment)
    {
        if (experiment.ExperimentType != ValidationExperimentType.TrainingSearchHoldoutValidation)
        {
            return Fail(ValidationParameterSetPublicationCodes.ExperimentTypeUnsupported,
                "This Validation Lab experiment type cannot publish deployment qualification.");
        }

        if (experiment.Status != ValidationExperimentStatus.Completed)
        {
            return Fail(ValidationParameterSetPublicationCodes.NotCompleted,
                "The Validation Lab experiment is not completed.");
        }

        if (experiment.ValidationRevealStatus != ValidationRevealStatus.Revealed
            || experiment.StrategyRobustnessDecision != StrategyRobustnessDecision.Passed)
        {
            return Fail(ValidationParameterSetPublicationCodes.NotPassed,
                "The Validation Lab experiment does not have a revealed exact-pass verdict.");
        }

        if (!experiment.IsQualificationCapable)
        {
            return Fail(ValidationParameterSetPublicationCodes.NotQualificationCapable,
                "The Validation Lab experiment is not qualification-capable.");
        }

        if (experiment.SupersessionStatus != ValidationExperimentSupersessionStatus.None
            || experiment.FrozenSnapshotValidationStatus != FrozenSnapshotValidationStatus.Valid
            || experiment.SelectionIntegrityStatus != ValidationSelectionIntegrityStatus.Passed
            || experiment.TrialSegmentReconciliationStatus != ValidationTrialSegmentReconciliationStatus.Matched
            || experiment.SelectedTrialId is null
            || string.IsNullOrWhiteSpace(experiment.FrozenStrategyParameterSnapshotJson)
            || string.IsNullOrWhiteSpace(experiment.FrozenParameterFingerprint)
            || string.IsNullOrWhiteSpace(experiment.SelectedTrialParameterFingerprint))
        {
            return Fail(ValidationParameterSetPublicationCodes.SelectionInvalid,
                "The experiment selection or frozen-snapshot evidence is invalid.");
        }

        return null;
    }

    private static ServiceResult<StrategyParameterSetDto>? ValidateTrial(
        ValidationExperiment experiment,
        ValidationParameterTrial trial)
    {
        if (trial.ValidationExperimentId != experiment.Id
            || trial.Id != experiment.SelectedTrialId
            || trial.Status != ValidationTrialStatus.Completed
            || !string.Equals(trial.GuardrailDecision, "Passed", StringComparison.Ordinal)
            || trial.TrialRankEligibility != ValidationTrialRankEligibility.Eligible)
        {
            return Fail(ValidationParameterSetPublicationCodes.TrialInvalid,
                "The selected Validation Lab trial is not terminal, eligible, and identity-bound.");
        }

        if (trial.AuditCompletionStatus != ValidationAuditCompletionStatus.Complete
            || trial.AuthoritativeAuditExecutionId is null)
        {
            return Fail(ValidationParameterSetPublicationCodes.AuditIncomplete,
                "The selected trial has no complete authoritative audit execution.");
        }

        return null;
    }

    private static ServiceResult<StrategyParameterSetDto>? ValidateFingerprintBinding(
        ValidationExperiment experiment,
        ValidationParameterTrial trial,
        ParameterFingerprintResult frozenCanonical,
        ParameterFingerprintResult selectedCanonical)
    {
        if (frozenCanonical.ValidationStatus != FrozenSnapshotValidationStatus.Valid
            || selectedCanonical.ValidationStatus != FrozenSnapshotValidationStatus.Valid
            || !string.Equals(trial.ParameterFingerprint, experiment.SelectedTrialParameterFingerprint, StringComparison.Ordinal)
            || !string.Equals(trial.ParameterFingerprint, experiment.FrozenParameterFingerprint, StringComparison.Ordinal)
            || !string.Equals(trial.ParameterFingerprint, frozenCanonical.ShortDisplayHash, StringComparison.Ordinal)
            || !string.Equals(frozenCanonical.CanonicalSnapshot, selectedCanonical.CanonicalSnapshot, StringComparison.Ordinal))
        {
            return Fail(ValidationParameterSetPublicationCodes.FingerprintMismatch,
                "The selected and frozen parameter fingerprints do not match recomputed canonical content.");
        }

        return null;
    }

    private static ServiceResult<StrategyParameterSetDto>? ValidateStrategy(
        ValidationExperiment experiment,
        Strategy? strategy)
    {
        if (strategy is null
            || !CanonicalStrategyPortfolio.TryParseCanonical(experiment.StrategyCode, out var expectedCode)
            || strategy.Code != expectedCode
            || !string.Equals(strategy.Code.ToCode(), experiment.StrategyCode, StringComparison.Ordinal)
            || !string.Equals(strategy.Version, experiment.StrategyVersion, StringComparison.Ordinal)
            || !strategy.IsEnabled
            || !CanonicalStrategyPortfolio.CanExecute(strategy.Code))
        {
            return Fail(ValidationParameterSetPublicationCodes.StrategyIneligible,
                "The experiment strategy is not an enabled canonical strategy with matching code and version.");
        }

        return null;
    }

    private static ServiceResult<StrategyParameterSetDto> ExistingPublicationResult(
        StrategyParameterSet existing,
        ValidationExperiment experiment,
        ValidationParameterTrial trial,
        Strategy strategy,
        string recomputedFingerprint)
    {
        var isExact = existing.Source == StrategyParameterSetSource.ValidationLab
            && existing.IsApproved
            && existing.QualificationStatus == ParameterSetQualificationStatus.DeploymentQualified
            && existing.QualificationSourceExperimentId == experiment.Id
            && existing.QualificationSourceTrialId == trial.Id
            && string.Equals(existing.QualificationParameterFingerprint, recomputedFingerprint, StringComparison.Ordinal)
            && string.Equals(existing.QualificationEvidenceVersion, EvidenceVersion, StringComparison.Ordinal)
            && existing.QualifiedAtUtc is not null
            && existing.ApprovedAtUtc is not null
            && !existing.IsDefaultForStrategy
            && !existing.IsDefaultForSymbolTimeframe
            && string.Equals(existing.StrategyCode, experiment.StrategyCode, StringComparison.Ordinal)
            && existing.SymbolId == experiment.SymbolId
            && string.Equals(existing.Timeframe, experiment.Timeframe, StringComparison.Ordinal)
            && string.Equals(existing.ParametersJson, experiment.FrozenStrategyParameterSnapshotJson, StringComparison.Ordinal)
            && experiment.IsCanonical
            && strategy.CanonicalValidationExperimentId == experiment.Id;

        return isExact
            ? ServiceResult<StrategyParameterSetDto>.Ok(StrategyParameterSetService.Map(existing))
            : Fail(ValidationParameterSetPublicationCodes.ProvenanceConflict,
                "The existing publication does not match durable Validation Lab provenance.");
    }

    private static string ResolveName(string? requested, string experimentName)
    {
        var resolved = string.IsNullOrWhiteSpace(requested)
            ? $"{experimentName} — qualified"
            : requested.Trim();
        return resolved.Length <= 200 ? resolved : resolved[..200];
    }

    private static ServiceResult<StrategyParameterSetDto> Fail(string code, string message) =>
        ServiceResult<StrategyParameterSetDto>.Fail(message, code);
}
