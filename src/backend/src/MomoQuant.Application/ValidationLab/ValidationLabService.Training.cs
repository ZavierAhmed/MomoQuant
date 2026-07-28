using System.Runtime.ExceptionServices;
using System.Text.Json;
using MomoQuant.Application.Common;
using MomoQuant.Application.Strategies;
using MomoQuant.Application.StrategyLab;
using MomoQuant.Application.ValidationLab.Dtos;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.StrategyLab;
using MomoQuant.Domain.ValidationLab;

namespace MomoQuant.Application.ValidationLab;

public sealed partial class ValidationLabService
{
    private static readonly TimeSpan TrainingLeaseTtl = TimeSpan.FromMinutes(30);

    public async Task<ServiceResult<ValidationTrialRecoveryReport>> RecoverTrialsAsync(
        long id,
        CancellationToken cancellationToken = default)
    {
        var experiment = await _experiments.GetByIdAsync(id, cancellationToken);
        if (experiment is null)
        {
            return ServiceResult<ValidationTrialRecoveryReport>.Fail("Validation experiment was not found.");
        }

        var preflight = await _trainingPreflight.CheckAsync(experiment, requireNoActiveLease: true, cancellationToken);
        if (!preflight.Passed)
        {
            return ServiceResult<ValidationTrialRecoveryReport>.Fail(string.Join("; ", preflight.Failures));
        }

        var draft = ParseDraft(experiment.DraftConfigurationJson);
        var profile = ToQualificationProfile(draft.QualificationProfile, experiment.PrimaryQualificationLayer);
        var combos = BuildTrainingCombinations(experiment, draft);
        var report = await _trialRecovery.RecoverFromStrategyLabRunsAsync(
            experiment, combos, profile, cancellationToken);

        var trials = await _trials.GetByExperimentIdAsync(id, cancellationToken);
        var progress = ValidationTrainingProgressCalculator.Calculate(experiment, trials, combos.Count);
        experiment.PercentComplete = progress.ProgressPercent;
        experiment.UpdatedAtUtc = DateTime.UtcNow;
        await _experiments.UpdateAsync(experiment, cancellationToken);

        return ServiceResult<ValidationTrialRecoveryReport>.Ok(report);
    }

    public async Task<ServiceResult<ValidationTrainingProgressDto>> GetTrainingProgressAsync(
        long id,
        CancellationToken cancellationToken = default)
    {
        var experiment = await _experiments.GetByIdAsync(id, cancellationToken);
        if (experiment is null)
        {
            return ServiceResult<ValidationTrainingProgressDto>.Fail("Validation experiment was not found.");
        }

        var draft = ParseDraft(experiment.DraftConfigurationJson);
        var combos = BuildTrainingCombinations(experiment, draft);
        var trials = await _trials.GetByExperimentIdAsync(id, cancellationToken);
        var progress = ValidationTrainingProgressCalculator.Calculate(experiment, trials, combos.Count);
        return ServiceResult<ValidationTrainingProgressDto>.Ok(progress);
    }

    private async Task<ServiceResult<ValidationExperimentDto>> ExecuteDurableTrainingAsync(
        long id,
        bool isResume,
        CancellationToken cancellationToken)
    {
        var experiment = await _experiments.GetByIdAsync(id, cancellationToken);
        if (experiment is null)
        {
            return ServiceResult<ValidationExperimentDto>.Fail("Validation experiment was not found.");
        }

        if (isResume)
        {
            if (!ValidationLifecycleGate.CanResumeTraining(experiment.Status))
            {
                return ServiceResult<ValidationExperimentDto>.Fail(
                    $"Resume requires Failed, TrainingInterrupted, or TrainingPaused (current: {experiment.Status}).");
            }
        }
        else if (!ValidationLifecycleGate.CanRunTraining(experiment.Status))
        {
            return ServiceResult<ValidationExperimentDto>.Fail(
                $"Training requires DataReady status (current: {experiment.Status}).");
        }

        if (experiment.TrainingStartUtc is null || experiment.TrainingEndUtc is null)
        {
            return ServiceResult<ValidationExperimentDto>.Fail("Training date range is missing. Prepare data first.");
        }

        var preflight = await _trainingPreflight.CheckAsync(experiment, requireNoActiveLease: true, cancellationToken);
        if (!preflight.Passed)
        {
            return ServiceResult<ValidationExperimentDto>.Fail(string.Join("; ", preflight.Failures));
        }

        var leaseOwner = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";
        var (acquired, conflict) = await _trainingLease.TryAcquireAsync(
            experiment.Id, leaseOwner, TrainingLeaseTtl, cancellationToken);
        if (!acquired)
        {
            return ServiceResult<ValidationExperimentDto>.Fail(conflict ?? "Training lease conflict.");
        }

        // Every post-acquisition operation is inside cleanup-safe orchestration.
        cancellationToken = CancellationToken.None;

        try
        {
            experiment.Status = isResume
                ? ValidationExperimentStatus.TrainingResumed
                : ValidationExperimentStatus.TrainingRunning;
            experiment.CurrentStage = isResume ? "ResumeTraining" : "Training";
            experiment.ErrorMessage = null;
            experiment.ValidationRevealStatus = ValidationRevealStatus.Hidden;
            experiment.UpdatedAtUtc = DateTime.UtcNow;
            await _experiments.UpdateAsync(experiment, cancellationToken);

            var requirementsResult = await _executionRequirementsResolver.ResolveAsync(
                new ResolveStrategyExecutionRequirementsRequest
                {
                    StrategyCode = experiment.StrategyCode,
                    StrategyVersion = experiment.StrategyVersion
                },
                cancellationToken);
            if (!requirementsResult.Succeeded || requirementsResult.Data is null)
            {
                experiment.Status = ValidationExperimentStatus.Failed;
                experiment.ErrorMessage = requirementsResult.ErrorMessage ?? "Failed to resolve strategy execution requirements.";
                experiment.PrimaryFailureReason = "STRATEGY_REQUIREMENTS_UNRESOLVED";
                experiment.CurrentStage = "Training";
                experiment.UpdatedAtUtc = DateTime.UtcNow;
                await _experiments.UpdateAsync(experiment, cancellationToken);
                return await ApplyCleanupOutcomeToResultAsync(
                    experiment,
                    leaseOwner,
                    ServiceResult<ValidationExperimentDto>.Fail(experiment.ErrorMessage),
                    cancellationToken);
            }

            var requirements = requirementsResult.Data;
            var trainingEndExclusive = ToExclusiveUtc(experiment.TrainingEndUtc.Value, experiment.Timeframe);
            var scopeRequest = ValidationTrainingCandleScopeRequest.FromExperiment(
                experiment,
                requirements,
                trainingEndExclusive);

            experiment.WarmupSnapshotJson = JsonSerializer.Serialize(new
            {
                requiredWarmupCandleCount = requirements.RequiredWarmupCandleCount,
                requirementsVersion = requirements.RequirementsVersion,
                strategyId = requirements.StrategyId,
                strategyCode = requirements.StrategyCode,
                strategyVersion = requirements.StrategyVersion
            }, JsonOptions);
            experiment.UpdatedAtUtc = DateTime.UtcNow;
            await _experiments.UpdateAsync(experiment, cancellationToken);

            ServiceResult<ValidationExperimentDto>? result = null;
            try
            {
                // Bootstrap once: create a temporary scope for warmup fingerprint only (no trial audit).
                var bootstrapResult = await _trainingScopeExecution.ExecuteWithScopeAsync(experiment, scopeRequest, async bootstrapScope =>
                {
                    experiment.WarmupSnapshotJson = JsonSerializer.Serialize(new
                    {
                        requiredWarmupCandleCount = bootstrapScope.Partition.RequiredWarmupCandleCount,
                        availableWarmupCandleCount = bootstrapScope.Partition.AvailableWarmupCandleCount,
                        warmupStatus = bootstrapScope.Partition.WarmupStatus.ToString(),
                        requirementsVersion = bootstrapScope.Partition.RequirementsVersion,
                        strategyId = requirements.StrategyId,
                        strategyCode = requirements.StrategyCode,
                        strategyVersion = requirements.StrategyVersion,
                        warmupContentFingerprint = bootstrapScope.Partition.WarmupContentFingerprint
                    }, JsonOptions);
                    experiment.UpdatedAtUtc = DateTime.UtcNow;
                    await _experiments.UpdateAsync(experiment, cancellationToken);
                }, cancellationToken);
                bootstrapResult.ThrowIfFailed();

                var draft = ParseDraft(experiment.DraftConfigurationJson);
                var profile = ToQualificationProfile(draft.QualificationProfile, experiment.PrimaryQualificationLayer);
                var combos = BuildTrainingCombinations(experiment, draft);

                if (isResume)
                {
                    await _trialRecovery.RecoverFromStrategyLabRunsAsync(
                        experiment, combos, profile, cancellationToken);
                }

                await EnsureTrialRowsAsync(experiment, combos, cancellationToken);
                await MarkInterruptedRunningTrialsAsync(experiment.Id, cancellationToken);

                for (var i = 0; i < combos.Count; i++)
                {
                    var combo = combos[i];
                    var trialNumber = i + 1;
                    var fingerprint = ParameterFingerprint(combo);
                    var trial = await _trials.GetByExperimentAndFingerprintAsync(experiment.Id, fingerprint, cancellationToken)
                        ?? throw new InvalidOperationException($"Trial row missing for fingerprint {fingerprint}.");

                    if (trial.Status == ValidationTrialStatus.GuardrailRejected)
                    {
                        await UpdateExperimentProgressAsync(experiment, combos.Count, cancellationToken);
                        var guardrailHeartbeat = await HeartbeatOrFailAsync(
                            experiment, leaseOwner, cancellationToken);
                        if (guardrailHeartbeat is not null)
                        {
                            result = guardrailHeartbeat;
                            break;
                        }

                        continue;
                    }

                    var revalidateCompletedTrial = isResume && trial.Status == ValidationTrialStatus.Completed;

                    if (!revalidateCompletedTrial && trial.Status == ValidationTrialStatus.Completed)
                    {
                        await UpdateExperimentProgressAsync(experiment, combos.Count, cancellationToken);
                        var completedHeartbeat = await HeartbeatOrFailAsync(
                            experiment, leaseOwner, cancellationToken);
                        if (completedHeartbeat is not null)
                        {
                            result = completedHeartbeat;
                            break;
                        }

                        continue;
                    }

                    if (trial.Status == ValidationTrialStatus.Failed && isResume)
                    {
                        // Explicit resume retries failed trials.
                    }
                    else if (trial.Status is ValidationTrialStatus.Failed
                             or ValidationTrialStatus.LeakageFailed)
                    {
                        continue;
                    }

                    if (!revalidateCompletedTrial)
                    {
                        trial.Status = ValidationTrialStatus.Running;
                        trial.StartedAtUtc = DateTime.UtcNow;
                        trial.ErrorMessage = null;
                        await ValidationTrainingDbRetry.ExecuteAsync(() => _trials.UpdateAsync(trial, cancellationToken));
                    }
                    else
                    {
                        trial.ErrorMessage = null;
                        await ValidationTrainingDbRetry.ExecuteAsync(() => _trials.UpdateAsync(trial, cancellationToken));
                    }

                    // Recover / supersede incomplete authoritative audit before access.
                    // Explicit AuditDurability boundary — must not fall into TrialBody handling.
                    AuthoritativeAuditExecutionEnsureResult ensureResult;
                    try
                    {
                        ensureResult = await EnsureAuthoritativeAuditExecutionAsync(
                            experiment, trial, leaseOwner, isResume, cancellationToken);
                    }
                    catch (ValidationAuditCompletenessVerificationException ensureCompletenessEx)
                    {
                        result = await ApplyCleanupOutcomeToResultAsync(
                            experiment,
                            leaseOwner,
                            await FailFinalizationThroughAggregateAsync(
                                experiment,
                                trial,
                                ensureCompletenessEx,
                                ValidationTrainingFailurePhase.CompletenessVerification,
                                leaseOwner,
                                cancellationToken),
                            cancellationToken);
                        break;
                    }
                    catch (Exception ensureEx)
                    {
                        result = await ApplyCleanupOutcomeToResultAsync(
                            experiment,
                            leaseOwner,
                            await FailFinalizationThroughAggregateAsync(
                                experiment,
                                trial,
                                ensureEx,
                                ValidationTrainingFailurePhase.AuditFinalization,
                                leaseOwner,
                                cancellationToken),
                            cancellationToken);
                        break;
                    }

                    var auditExecution = ensureResult.Execution;

                    if (ensureResult.FailClosed)
                    {
                        ServiceResult<ValidationExperimentDto>? revalidationFailure;
                        try
                        {
                            revalidationFailure = await ApplyCompletedTrialAuditRevalidationFailureAsync(
                                experiment,
                                trial,
                                ensureResult.CompletenessCode,
                                $"Completed audit execution failed verifier revalidation: {ensureResult.CompletenessCode?.ToString() ?? "FailClosed"}.",
                                leaseOwner,
                                cancellationToken);
                        }
                        catch (ValidationAuditCompletenessVerificationException revalidationCompletenessEx)
                        {
                            revalidationFailure = await FailFinalizationThroughAggregateAsync(
                                experiment,
                                trial,
                                revalidationCompletenessEx,
                                ValidationTrainingFailurePhase.CompletenessVerification,
                                leaseOwner,
                                cancellationToken);
                        }
                        catch (Exception revalidationEx)
                        {
                            revalidationFailure = await FailFinalizationThroughAggregateAsync(
                                experiment,
                                trial,
                                revalidationEx,
                                ValidationTrainingFailurePhase.CompletenessVerification,
                                leaseOwner,
                                cancellationToken);
                        }

                        result = await ApplyCleanupOutcomeToResultAsync(
                            experiment,
                            leaseOwner,
                            revalidationFailure
                            ?? ServiceResult<ValidationExperimentDto>.Fail(
                                ValidationTrainingFailureHandler.UserSafeAuditPersistenceMessage,
                                ValidationTrainingFailureCodes.ValidationAccessAuditPersistenceFailed),
                            cancellationToken);
                        break;
                    }

                    if (ensureResult.VerifiedFinalizationOnly || ensureResult.FinalizationOnly)
                    {
                        ServiceResult<ValidationExperimentDto>? finalizationOnlyFailure;
                        try
                        {
                            finalizationOnlyFailure = await FinalizeTrialAuditWithVerifierAsync(
                                experiment,
                                trial,
                                combo,
                                fingerprint,
                                auditExecution,
                                leaseOwner,
                                cancellationToken);
                        }
                        catch (ValidationAuditCompletenessVerificationException finalizationCompletenessEx)
                        {
                            finalizationOnlyFailure = await FailFinalizationThroughAggregateAsync(
                                experiment,
                                trial,
                                finalizationCompletenessEx,
                                ValidationTrainingFailurePhase.CompletenessVerification,
                                leaseOwner,
                                cancellationToken);
                        }
                        catch (Exception finalizationEx)
                        {
                            finalizationOnlyFailure = await FailFinalizationThroughAggregateAsync(
                                experiment,
                                trial,
                                finalizationEx,
                                ValidationTrainingFailurePhase.AuditFinalization,
                                leaseOwner,
                                cancellationToken);
                        }

                        if (finalizationOnlyFailure is not null)
                        {
                            result = await ApplyCleanupOutcomeToResultAsync(
                                experiment,
                                leaseOwner,
                                finalizationOnlyFailure,
                                cancellationToken);
                            break;
                        }

                        await UpdateExperimentProgressAsync(experiment, combos.Count, cancellationToken);
                        var finalizationOkHeartbeat = await HeartbeatOrFailAsync(
                            experiment, leaseOwner, cancellationToken);
                        if (finalizationOkHeartbeat is not null)
                        {
                            result = finalizationOkHeartbeat;
                            break;
                        }

                        continue;
                    }

                    if (auditExecution.Status == ValidationAuditExecutionStatus.Completed)
                    {
                        ServiceResult<ValidationExperimentDto>? completedReentryFailure;
                        try
                        {
                            completedReentryFailure = await ApplyCompletedTrialAuditRevalidationFailureAsync(
                                experiment,
                                trial,
                                null,
                                "Completed audit execution cannot re-enter StrategyLab training scope.",
                                leaseOwner,
                                cancellationToken);
                        }
                        catch (ValidationAuditCompletenessVerificationException completedCompletenessEx)
                        {
                            completedReentryFailure = await FailFinalizationThroughAggregateAsync(
                                experiment,
                                trial,
                                completedCompletenessEx,
                                ValidationTrainingFailurePhase.CompletenessVerification,
                                leaseOwner,
                                cancellationToken);
                        }
                        catch (Exception completedEx)
                        {
                            completedReentryFailure = await FailFinalizationThroughAggregateAsync(
                                experiment,
                                trial,
                                completedEx,
                                ValidationTrainingFailurePhase.CompletenessVerification,
                                leaseOwner,
                                cancellationToken);
                        }

                        result = await ApplyCleanupOutcomeToResultAsync(
                            experiment,
                            leaseOwner,
                            completedReentryFailure
                            ?? ServiceResult<ValidationExperimentDto>.Fail(
                                ValidationTrainingFailureHandler.UserSafeAuditPersistenceMessage,
                                ValidationTrainingFailureCodes.ValidationAccessAuditPersistenceFailed),
                            cancellationToken);
                        break;
                    }

                    var trialScopeRequest = new ValidationTrainingCandleScopeRequest
                    {
                        ValidationExperimentId = scopeRequest.ValidationExperimentId,
                        SymbolId = scopeRequest.SymbolId,
                        SymbolName = scopeRequest.SymbolName,
                        Timeframe = scopeRequest.Timeframe,
                        TrainingEvaluationStartUtc = scopeRequest.TrainingEvaluationStartUtc,
                        TrainingEvaluationEndExclusiveUtc = scopeRequest.TrainingEvaluationEndExclusiveUtc,
                        ValidationBoundaryUtc = scopeRequest.ValidationBoundaryUtc,
                        RequiredWarmupCandleCount = scopeRequest.RequiredWarmupCandleCount,
                        RequirementsVersion = scopeRequest.RequirementsVersion,
                        StrategyId = scopeRequest.StrategyId,
                        StrategyCode = scopeRequest.StrategyCode,
                        StrategyVersion = scopeRequest.StrategyVersion,
                        BoundScopeExecutionId = auditExecution.ScopeExecutionId,
                        BoundAuditExecutionId = auditExecution.AuditExecutionId,
                        BoundExecutionToken = auditExecution.ExecutionToken,
                        BoundAttemptNumber = auditExecution.AttemptNumber
                    };

                    try
                    {
                        var optimizerFp = _parameterFingerprint.ComputeFingerprint(draft.Parameters);
                        var scopeResult = await _trainingScopeExecution.ExecuteWithScopeAsync(
                            experiment,
                            trialScopeRequest,
                            async trainingScope =>
                            {
                                var trialResult = await _trainingScopeExecution.ExecuteTrialAsync(
                                    trainingScope,
                                    trialNumber,
                                    trial.Id,
                                    async () =>
                                    {
                                        var run = await CreateLabRunAsync(
                                            experiment,
                                            combo,
                                            draft,
                                            experiment.TrainingStartUtc.Value,
                                            ToExclusiveUtc(experiment.TrainingEndUtc.Value, experiment.Timeframe),
                                            $"VL-Train-{experiment.Id}-T{trialNumber}",
                                            cancellationToken);

                                        trial.StrategyLabRunId = run.Id;
                                        await ValidationTrainingDbRetry.ExecuteAsync(() => _trials.UpdateAsync(trial, cancellationToken));

                                        var trainingBoundary = DateTime.SpecifyKind(
                                            experiment.ValidationStartUtc!.Value,
                                            DateTimeKind.Utc);
                                        var executionContext = StrategyLabExecutionContext.ForValidationTraining(
                                            validationExperimentId: experiment.Id,
                                            validationTrialId: trial.Id,
                                            validationTrialNumber: trialNumber,
                                            trainingBoundaryUtc: trainingBoundary,
                                            candleDataSource: new ValidationTrainingStrategyLabCandleDataSource(
                                                trainingScope,
                                                "ValidationLab.Training"),
                                            callerComponent: "ValidationLab.Training");

                                        await _labRunner.ExecuteAsync(run.Id, executionContext, cancellationToken);
                                        run = await _labRuns.GetByIdAsync(run.Id, cancellationToken) ?? run;

                                        if (run.Status != StrategyLabRunStatus.Completed)
                                        {
                                            trial.Status = ValidationTrialStatus.Failed;
                                            trial.ErrorMessage = run.ErrorMessage
                                                ?? $"Strategy lab run {run.Id} ended with status {run.Status}.";
                                            trial.CompletedAtUtc = DateTime.UtcNow;
                                            await ValidationTrainingDbRetry.ExecuteAsync(() => _trials.UpdateAsync(trial, cancellationToken));
                                            return;
                                        }

                                        await PopulateTrialMetricsAsync(
                                            experiment, trial, combo, run, profile, cancellationToken);
                                        await ValidationTrainingDbRetry.ExecuteAsync(() => _trials.UpdateAsync(trial, cancellationToken));
                                    },
                                    cancellationToken);

                                if (!trialResult.IsSuccess)
                                {
                                    var handled = await HandleScopeExecutionFailureAsync(
                                        experiment,
                                        trial,
                                        trainingScope,
                                        trialResult,
                                        optimizerFp,
                                        leaseOwner,
                                        cancellationToken);
                                    if (handled is not null)
                                    {
                                        result = handled;
                                    }

                                    return;
                                }

                                var finalExpected = trainingScope.AccessLog.Count == 0
                                    ? auditExecution.LastConfirmedSequence
                                    : trainingScope.AccessLog.Max(r => r.ScopeSequenceNumber);

                                auditExecution = await _auditExecutions.GetByAuditExecutionIdAsync(
                                    auditExecution.AuditExecutionId, cancellationToken)
                                    ?? auditExecution;
                                if (finalExpected < auditExecution.LastConfirmedSequence)
                                {
                                    finalExpected = auditExecution.LastConfirmedSequence;
                                }

                                try
                                {
                                    var completion = await _auditFinalizer.CompleteAsync(
                                        auditExecution.AuditExecutionId,
                                        finalExpected,
                                        cancellationToken);

                                    trial = await _trials.GetByExperimentAndFingerprintAsync(
                                        experiment.Id, fingerprint, cancellationToken) ?? trial;
                                    auditExecution = await _auditExecutions.GetByAuditExecutionIdAsync(
                                        auditExecution.AuditExecutionId, cancellationToken) ?? auditExecution;

                                    var metricsPassed = string.Equals(
                                        trial.GuardrailDecision,
                                        "Passed",
                                        StringComparison.OrdinalIgnoreCase)
                                        && trial.Status != ValidationTrialStatus.GuardrailRejected
                                        && trial.Status != ValidationTrialStatus.Failed;

                                    if (!metricsPassed)
                                    {
                                        return;
                                    }

                                    if (!completion.IsComplete)
                                    {
                                        var auditFailure = new ValidationAuditExecutionException(
                                            completion.FailureCode ?? completion.CompletionCode.ToString(),
                                            $"Audit finalization failed: {completion.FailureCode ?? completion.CompletionCode.ToString()}.");
                                        var finalizationPhase = IsCompletenessEvidenceCode(completion.CompletionCode)
                                            ? ValidationTrainingFailurePhase.CompletenessVerification
                                            : ValidationTrainingFailurePhase.AuditFinalization;
                                        var observedFailures = new ValidationTrainingFailureAggregate();
                                        observedFailures.Observe(auditFailure, finalizationPhase);
                                        var handled = await _trainingFailureHandler.HandleAuditPersistenceFailureAsync(
                                            experiment,
                                            trial,
                                            auditFailure,
                                            leaseOwner: leaseOwner,
                                            observedFailures: observedFailures,
                                            cancellationToken: cancellationToken);
                                        result = ServiceResult<ValidationExperimentDto>.Fail(
                                            handled.UserSafeErrorMessage,
                                            handled.ErrorCode);
                                        return;
                                    }
                                }
                                catch (ValidationAuditCompletenessVerificationException ex)
                                {
                                    var observedFailures = new ValidationTrainingFailureAggregate();
                                    observedFailures.Observe(ex, ValidationTrainingFailurePhase.CompletenessVerification);
                                    var handled = await _trainingFailureHandler.HandleAuditPersistenceFailureAsync(
                                        experiment,
                                        trial,
                                        ex,
                                        leaseOwner: leaseOwner,
                                        observedFailures: observedFailures,
                                        cancellationToken: cancellationToken);
                                    result = ServiceResult<ValidationExperimentDto>.Fail(
                                        handled.UserSafeErrorMessage,
                                        handled.ErrorCode);
                                    return;
                                }
                                catch (Exception ex)
                                {
                                    var observedFailures = new ValidationTrainingFailureAggregate();
                                    observedFailures.Observe(ex, ValidationTrainingFailurePhase.AuditFinalization);
                                    var handled = await _trainingFailureHandler.HandleAuditPersistenceFailureAsync(
                                        experiment,
                                        trial,
                                        ex,
                                        leaseOwner: leaseOwner,
                                        observedFailures: observedFailures,
                                        cancellationToken: cancellationToken);
                                    result = ServiceResult<ValidationExperimentDto>.Fail(
                                        handled.UserSafeErrorMessage,
                                        handled.ErrorCode);
                                    return;
                                }

                                if (result is not null
                                    || !string.Equals(
                                        trial.GuardrailDecision,
                                        "Passed",
                                        StringComparison.OrdinalIgnoreCase)
                                    || trial.Status == ValidationTrialStatus.GuardrailRejected
                                    || trial.Status == ValidationTrialStatus.Failed)
                                {
                                    return;
                                }

                                var batches = await _auditBatches.GetByAuditExecutionIdAsync(
                                    auditExecution.AuditExecutionId, cancellationToken);
                                var accessRows = (await _candleAccessAudits.GetByExperimentIdAsync(
                                    experiment.Id, cancellationToken))
                                    .Where(r => r.ScopeExecutionId == auditExecution.ScopeExecutionId)
                                    .ToList();

                                ValidationAuditCompletenessResult completeness;
                                try
                                {
                                    completeness = _auditCompletenessVerifier.Verify(
                                        trial, auditExecution, batches, accessRows);
                                }
                                catch (Exception verifyEx)
                                {
                                    var observedFailures = new ValidationTrainingFailureAggregate();
                                    observedFailures.Observe(
                                        verifyEx,
                                        ValidationTrainingFailurePhase.CompletenessVerification);
                                    var handled = await _trainingFailureHandler.HandleAuditPersistenceFailureAsync(
                                        experiment,
                                        trial,
                                        verifyEx,
                                        leaseOwner: leaseOwner,
                                        observedFailures: observedFailures,
                                        cancellationToken: cancellationToken);
                                    result = ServiceResult<ValidationExperimentDto>.Fail(
                                        handled.UserSafeErrorMessage,
                                        handled.ErrorCode);
                                    return;
                                }

                                if (_trialAuditCompletionGate.CanMarkTrialCompleted(
                                        trial, auditExecution, completeness))
                                {
                                    _trialAuditCompletionGate.ApplyCompletedStatus(
                                        trial, auditExecution, completeness);
                                    await ValidationTrainingDbRetry.ExecuteAsync(
                                        () => _trials.UpdateAsync(trial, cancellationToken));
                                }
                                else
                                {
                                    var completenessFailure = new ValidationAuditExecutionException(
                                        completeness.CompletionCode.ToString(),
                                        $"Audit completeness verification failed: {completeness.CompletionCode}.");
                                    var observedFailures = new ValidationTrainingFailureAggregate();
                                    observedFailures.Observe(
                                        completenessFailure,
                                        ValidationTrainingFailurePhase.CompletenessVerification);
                                    var handled = await _trainingFailureHandler.HandleAuditPersistenceFailureAsync(
                                        experiment,
                                        trial,
                                        completenessFailure,
                                        leaseOwner: leaseOwner,
                                        observedFailures: observedFailures,
                                        cancellationToken: cancellationToken);
                                    result = ServiceResult<ValidationExperimentDto>.Fail(
                                        handled.UserSafeErrorMessage,
                                        handled.ErrorCode);
                                }
                            },
                            cancellationToken);

                        if (!scopeResult.IsSuccess)
                        {
                            if (result is null)
                            {
                                var outerFailure = await HandleOuterScopeExecutionFailureAsync(
                                    experiment,
                                    trial,
                                    scopeResult,
                                    leaseOwner,
                                    cancellationToken);
                                if (outerFailure is not null)
                                {
                                    result = outerFailure;
                                }
                            }
                            else if (scopeResult.DisposalException is not null)
                            {
                                await PersistScopeDisposalFailureAsync(
                                    experiment,
                                    trial,
                                    scopeResult.DisposalException,
                                    cancellationToken);
                            }
                        }

                        if (result is not null)
                        {
                            result = await ApplyCleanupOutcomeToResultAsync(
                                experiment,
                                leaseOwner,
                                result,
                                cancellationToken);
                            break;
                        }
                    }
                    catch (ValidationAccessEvidencePersistenceException ex)
                    {
                        var handled = await _trainingFailureHandler.HandleAuditPersistenceFailureAsync(
                            experiment,
                            trial,
                            ex,
                            leaseOwner: leaseOwner,
                            cancellationToken: cancellationToken);
                        result = await ApplyCleanupOutcomeToResultAsync(
                            experiment,
                            leaseOwner,
                            ServiceResult<ValidationExperimentDto>.Fail(
                                handled.UserSafeErrorMessage,
                                handled.ErrorCode),
                            cancellationToken);
                        break;
                    }
                    catch (ValidationAuditExecutionIdentityMismatchException ex)
                    {
                        var observedFailures = new ValidationTrainingFailureAggregate();
                        observedFailures.Observe(ex, ValidationTrainingFailurePhase.AuditFinalization);
                        var handled = await _trainingFailureHandler.HandleAuditPersistenceFailureAsync(
                            experiment,
                            trial,
                            ex,
                            leaseOwner: leaseOwner,
                            observedFailures: observedFailures,
                            cancellationToken: cancellationToken);
                        result = await ApplyCleanupOutcomeToResultAsync(
                            experiment,
                            leaseOwner,
                            ServiceResult<ValidationExperimentDto>.Fail(
                                handled.UserSafeErrorMessage,
                                handled.ErrorCode),
                            cancellationToken);
                        break;
                    }
                    catch (Exception ex) when (ValidationTrainingDbRetry.IsTransient(ex))
                    {
                        var priorAggregate = ValidationTrainingFailurePersistence.MergeExisting(experiment.FailureReasonsJson);
                        var priorPrimary = priorAggregate.PrimaryFailure;
                        var incoming = new ValidationTrainingFailureAggregate();
                        incoming.Observe(
                            ex,
                            ValidationTrainingFailurePhase.TrialBody,
                            "Training was interrupted by a transient infrastructure error.");
                        var incomingPrimary = incoming.PrimaryFailure!;

                        trial.Status = ValidationTrialStatus.Interrupted;
                        trial.ErrorMessage = incomingPrimary.UserSafeMessage;
                        trial.CompletedAtUtc = DateTime.UtcNow;
                        await ValidationTrainingDbRetry.ExecuteAsync(() => _trials.UpdateAsync(trial, cancellationToken));

                        var aggregate = ValidationTrainingFailurePersistence.MergeExisting(experiment.FailureReasonsJson);
                        aggregate.Observe(
                            ex,
                            ValidationTrainingFailurePhase.TrialBody,
                            incomingPrimary.UserSafeMessage);
                        var keepPriorPrimary = HasHigherPrecedenceFailure(priorPrimary, incomingPrimary);
                        if (!keepPriorPrimary)
                        {
                            experiment.Status = ValidationExperimentStatus.TrainingInterrupted;
                            experiment.ErrorMessage = aggregate.PrimaryFailure?.UserSafeMessage;
                            experiment.CurrentStage = "TrainingInterrupted";
                        }

                        ValidationTrainingFailurePersistence.ApplyToExperiment(experiment, aggregate);
                        await UpdateExperimentProgressAsync(experiment, combos.Count, cancellationToken);
                        result = await ApplyCleanupOutcomeToResultAsync(
                            experiment,
                            leaseOwner,
                            ServiceResult<ValidationExperimentDto>.Fail(
                                aggregate.PrimaryFailure?.UserSafeMessage ?? incomingPrimary.UserSafeMessage,
                                aggregate.PrimaryFailure?.Code ?? incomingPrimary.Code),
                            cancellationToken);
                        break;
                    }
                    catch (Exception ex)
                    {
                        var trialAggregate = new ValidationTrainingFailureAggregate();
                        trialAggregate.Observe(ex, ValidationTrainingFailurePhase.TrialBody);
                        trial.Status = ValidationTrialStatus.Failed;
                        trial.ErrorMessage = trialAggregate.PrimaryFailure?.UserSafeMessage
                            ?? "Validation training trial execution failed.";
                        trial.CompletedAtUtc = DateTime.UtcNow;
                        await ValidationTrainingDbRetry.ExecuteAsync(() => _trials.UpdateAsync(trial, cancellationToken));
                    }

                    await UpdateExperimentProgressAsync(experiment, combos.Count, cancellationToken);
                    var trialHeartbeat = await HeartbeatOrFailAsync(
                        experiment, leaseOwner, cancellationToken);
                    if (trialHeartbeat is not null)
                    {
                        result = trialHeartbeat;
                        break;
                    }

                    if (result is not null)
                    {
                        result = await ApplyCleanupOutcomeToResultAsync(
                            experiment, leaseOwner, result, cancellationToken);
                        break;
                    }
                }

                result ??= await FinalizeTrainingAsync(experiment, draft, combos.Count, cancellationToken, leaseOwner);
            }
            catch (ValidationTrainingInsufficientWarmupException ex)
            {
                experiment.Status = ValidationExperimentStatus.Failed;
                experiment.ErrorMessage =
                    $"Insufficient warm-up candles for training (available={ex.AvailableWarmupCandleCount}, required={ex.RequiredWarmupCandleCount}).";
                experiment.CurrentStage = "InsufficientWarmup";
                experiment.UpdatedAtUtc = DateTime.UtcNow;
                var warmupAggregate = new ValidationTrainingFailureAggregate();
                warmupAggregate.Observe(ex, ValidationTrainingFailurePhase.TrialBody);
                ValidationTrainingFailurePersistence.ApplyToExperiment(experiment, warmupAggregate);
                experiment.WarmupSnapshotJson = JsonSerializer.Serialize(new
                {
                    requiredWarmupCandleCount = ex.RequiredWarmupCandleCount,
                    availableWarmupCandleCount = ex.AvailableWarmupCandleCount,
                    warmupStatus = ex.WarmupStatus.ToString(),
                    requirementsVersion = requirements.RequirementsVersion
                }, JsonOptions);
                await _experiments.UpdateAsync(experiment, cancellationToken);
                return await ApplyCleanupOutcomeToResultAsync(
                    experiment,
                    leaseOwner,
                    ServiceResult<ValidationExperimentDto>.Fail(
                        experiment.ErrorMessage,
                        ValidationTrainingFailureCodes.InsufficientWarmup),
                    cancellationToken);
            }

            return result ?? ServiceResult<ValidationExperimentDto>.Fail("Training ended without a result.");
        }
        catch (Exception ex) when (ValidationTrainingDbRetry.IsTransient(ex))
        {
            var priorAggregate = ValidationTrainingFailurePersistence.MergeExisting(experiment.FailureReasonsJson);
            var priorPrimary = priorAggregate.PrimaryFailure;
            var incoming = new ValidationTrainingFailureAggregate();
            incoming.Observe(
                ex,
                ValidationTrainingFailurePhase.TrialBody,
                "Training was interrupted by a transient infrastructure error.");
            var incomingPrimary = incoming.PrimaryFailure!;

            var aggregate = ValidationTrainingFailurePersistence.MergeExisting(experiment.FailureReasonsJson);
            aggregate.Observe(ex, ValidationTrainingFailurePhase.TrialBody, incomingPrimary.UserSafeMessage);
            var keepPriorPrimary = HasHigherPrecedenceFailure(priorPrimary, incomingPrimary);
            if (!keepPriorPrimary)
            {
                experiment.Status = ValidationExperimentStatus.TrainingInterrupted;
                experiment.ErrorMessage = aggregate.PrimaryFailure?.UserSafeMessage;
                experiment.CurrentStage = "TrainingInterrupted";
            }

            return await PersistAggregateSafelyAndReleaseAsync(
                experiment,
                leaseOwner,
                aggregate,
                incomingPrimary.UserSafeMessage,
                cancellationToken);
        }
        catch (Exception ex)
        {
            var priorAggregate = ValidationTrainingFailurePersistence.MergeExisting(experiment.FailureReasonsJson);
            var priorPrimary = priorAggregate.PrimaryFailure;
            var incoming = new ValidationTrainingFailureAggregate();
            incoming.Observe(ex, ValidationTrainingFailurePhase.ExperimentStatusPersistence);
            var incomingPrimary = incoming.PrimaryFailure!;

            var aggregate = ValidationTrainingFailurePersistence.MergeExisting(experiment.FailureReasonsJson);
            aggregate.Observe(ex, ValidationTrainingFailurePhase.ExperimentStatusPersistence);
            var keepPriorPrimary = HasHigherPrecedenceFailure(priorPrimary, incomingPrimary);
            if (!keepPriorPrimary)
            {
                experiment.Status = ValidationExperimentStatus.Failed;
                experiment.ErrorMessage = aggregate.PrimaryFailure?.UserSafeMessage;
                experiment.CurrentStage = "Training";
            }

            return await PersistAggregateSafelyAndReleaseAsync(
                experiment,
                leaseOwner,
                aggregate,
                "Validation training failed.",
                cancellationToken);
        }
    }

    /// <summary>
    /// Bounded failure persistence then exactly one authoritative lease cleanup.
    /// Persistence exceptions are observed as ExperimentStatusPersistence and never bypass release.
    /// </summary>
    private async Task<ServiceResult<ValidationExperimentDto>> PersistAggregateSafelyAndReleaseAsync(
        ValidationExperiment experiment,
        string leaseOwner,
        ValidationTrainingFailureAggregate aggregate,
        string fallbackUserSafeMessage,
        CancellationToken cancellationToken)
    {
        ValidationTrainingFailurePersistence.ApplyToExperiment(experiment, aggregate);
        experiment.UpdatedAtUtc = DateTime.UtcNow;
        try
        {
            await ValidationTrainingDbRetry.ExecuteAsync(
                    () => _experiments.UpdateAsync(experiment, cancellationToken),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception persistEx)
        {
            aggregate.Observe(persistEx, ValidationTrainingFailurePhase.ExperimentStatusPersistence);
            var primary = aggregate.PrimaryFailure;
            if (primary is not null)
            {
                experiment.ErrorMessage = primary.UserSafeMessage;
                experiment.PrimaryFailureReason = primary.Code;
            }

            experiment.IsQualificationCapable = false;
            ValidationTrainingFailurePersistence.ApplyToExperiment(experiment, aggregate);
            experiment.UpdatedAtUtc = DateTime.UtcNow;
            try
            {
                await ValidationTrainingDbRetry.ExecuteAsync(
                        () => _experiments.UpdateAsync(experiment, cancellationToken),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch
            {
                // Bounded best-effort — retain in-memory aggregate; never leak raw persistence details.
            }
        }

        var finalPrimary = aggregate.PrimaryFailure;
        return await ApplyCleanupOutcomeToResultAsync(
            experiment,
            leaseOwner,
            ServiceResult<ValidationExperimentDto>.Fail(
                finalPrimary?.UserSafeMessage ?? fallbackUserSafeMessage,
                finalPrimary?.Code),
            cancellationToken);
    }

    private async Task<ServiceResult<ValidationExperimentDto>?> HeartbeatOrFailAsync(
        ValidationExperiment experiment,
        string leaseOwner,
        CancellationToken cancellationToken)
    {
        var heartbeat = await TryHeartbeatTrainingLeaseAsync(experiment, leaseOwner, cancellationToken);
        if (heartbeat.Succeeded)
        {
            return null;
        }

        return await ApplyCleanupOutcomeToResultAsync(
            experiment,
            leaseOwner,
            ServiceResult<ValidationExperimentDto>.Fail(
                heartbeat.UserSafeErrorMessage ?? ValidationTrainingFailureHandler.UserSafeCleanupMessage,
                heartbeat.ErrorCode ?? ValidationTrainingFailureCodes.TrainingCleanupFailed),
            cancellationToken);
    }

    private async Task<ServiceResult<ValidationExperimentDto>?> HandleScopeExecutionFailureAsync(
        ValidationExperiment experiment,
        ValidationParameterTrial trial,
        IValidationTrainingCandleScope scope,
        ValidationTrainingScopeExecutionResult executionResult,
        string optimizerFp,
        string leaseOwner,
        CancellationToken cancellationToken)
    {
        var aggregate = executionResult.ToFailureAggregate();
        if (aggregate.HasBoundaryFailure)
        {
            var exception = ResolveHandlerException(
                aggregate,
                executionResult.BodyException,
                executionResult.FlushException);
            var handled = await _trainingFailureHandler.HandleBoundaryFailureAsync(
                experiment,
                trial,
                scope,
                exception,
                optimizerInputFingerprint: optimizerFp,
                leaseOwner: leaseOwner,
                observedFailures: aggregate,
                scopeFlushAlreadyAttempted: executionResult.FlushAttempted,
                cancellationToken);
            return ServiceResult<ValidationExperimentDto>.Fail(
                handled.UserSafeErrorMessage,
                handled.ErrorCode);
        }

        if (aggregate.HasAuditDurabilityFailure)
        {
            var exception = ResolveHandlerException(
                aggregate,
                executionResult.BodyException,
                executionResult.FlushException);
            var handled = await _trainingFailureHandler.HandleAuditPersistenceFailureAsync(
                experiment,
                trial,
                exception,
                leaseOwner: leaseOwner,
                observedFailures: aggregate,
                cancellationToken);
            return ServiceResult<ValidationExperimentDto>.Fail(
                handled.UserSafeErrorMessage,
                handled.ErrorCode);
        }

        var bodyException = executionResult.BodyException?.SourceException;
        if (bodyException is not null)
        {
            var primary = aggregate.PrimaryFailure;
            trial.Status = ValidationTrialStatus.Failed;
            trial.ErrorMessage = primary?.UserSafeMessage ?? "Validation training trial execution failed.";
            trial.CompletedAtUtc = DateTime.UtcNow;
            ValidationTrainingFailurePersistence.ApplyTrialWarnings(trial, aggregate);
            ValidationTrainingFailurePersistence.ApplyToExperiment(experiment, aggregate);
            await ValidationTrainingDbRetry.ExecuteAsync(() => _trials.UpdateAsync(trial, cancellationToken));
            await _experiments.UpdateAsync(experiment, cancellationToken);

            if (executionResult.DisposalException is not null)
            {
                return ServiceResult<ValidationExperimentDto>.Fail(
                    aggregate.PrimaryFailure?.UserSafeMessage
                    ?? ValidationTrainingFailureHandler.UserSafeCleanupMessage,
                    aggregate.PrimaryFailure?.Code
                    ?? ValidationTrainingFailureCodes.TrialExecutionFailed);
            }

            return null;
        }

        if (executionResult.DisposalException is not null)
        {
            EnsureRecoverableAfterCleanupFailure(experiment, aggregate);
            ValidationTrainingFailurePersistence.ApplyTrialWarnings(trial, aggregate);
            ValidationTrainingFailurePersistence.ApplyToExperiment(experiment, aggregate);
            experiment.UpdatedAtUtc = DateTime.UtcNow;
            await ValidationTrainingDbRetry.ExecuteAsync(() => _trials.UpdateAsync(trial, cancellationToken));
            await _experiments.UpdateAsync(experiment, cancellationToken);
            return ServiceResult<ValidationExperimentDto>.Fail(
                aggregate.PrimaryFailure?.UserSafeMessage
                ?? ValidationTrainingFailureHandler.UserSafeCleanupMessage,
                aggregate.PrimaryFailure?.Code
                ?? ValidationTrainingFailureCodes.TrainingCleanupFailed);
        }

        return null;
    }

    private async Task<ServiceResult<ValidationExperimentDto>?> HandleOuterScopeExecutionFailureAsync(
        ValidationExperiment experiment,
        ValidationParameterTrial trial,
        ValidationTrainingScopeExecutionResult scopeResult,
        string leaseOwner,
        CancellationToken cancellationToken)
    {
        var aggregate = scopeResult.ToFailureAggregate();
        if (aggregate.HasAuditDurabilityFailure)
        {
            var exception = ResolveHandlerException(
                aggregate,
                scopeResult.BodyException,
                scopeResult.FlushException);
            var handled = await _trainingFailureHandler.HandleAuditPersistenceFailureAsync(
                experiment,
                trial,
                exception,
                leaseOwner: leaseOwner,
                observedFailures: aggregate,
                cancellationToken);
            return ServiceResult<ValidationExperimentDto>.Fail(
                handled.UserSafeErrorMessage,
                handled.ErrorCode);
        }

        if (aggregate.HasBoundaryFailure)
        {
            var primary = aggregate.PrimaryFailure;
            if (scopeResult.DisposalException is not null)
            {
                ValidationTrainingFailurePersistence.ApplyToExperiment(experiment, aggregate);
                experiment.UpdatedAtUtc = DateTime.UtcNow;
                await _experiments.UpdateAsync(experiment, cancellationToken);
            }

            return ServiceResult<ValidationExperimentDto>.Fail(
                primary?.UserSafeMessage ?? ValidationTrainingFailureHandler.UserSafeLeakageMessage,
                primary?.Code ?? ValidationTrainingFailureCodes.ValidationDataLeakage);
        }

        if (scopeResult.DisposalException is not null || aggregate.HasCleanupFailure)
        {
            EnsureRecoverableAfterCleanupFailure(experiment, aggregate);
            ValidationTrainingFailurePersistence.ApplyTrialWarnings(trial, aggregate);
            ValidationTrainingFailurePersistence.ApplyToExperiment(experiment, aggregate);
            experiment.UpdatedAtUtc = DateTime.UtcNow;
            await _trials.UpdateAsync(trial, cancellationToken);
            await _experiments.UpdateAsync(experiment, cancellationToken);
            return ServiceResult<ValidationExperimentDto>.Fail(
                aggregate.PrimaryFailure?.UserSafeMessage
                ?? ValidationTrainingFailureHandler.UserSafeCleanupMessage,
                aggregate.PrimaryFailure?.Code
                ?? ValidationTrainingFailureCodes.TrainingCleanupFailed);
        }

        return null;
    }

    private static bool HasHigherPrecedenceFailure(
        ValidationTrainingFailureRecord? existingPrimary,
        ValidationTrainingFailureRecord incomingFailure) =>
        existingPrimary is not null
        && (int)existingPrimary.Precedence < (int)incomingFailure.Precedence;

    private static bool IsCompletenessEvidenceCode(ValidationAuditCompletenessCode code) =>
        code is ValidationAuditCompletenessCode.SequenceGap
            or ValidationAuditCompletenessCode.DuplicateSequence
            or ValidationAuditCompletenessCode.BatchOverlap
            or ValidationAuditCompletenessCode.ManifestMissing
            or ValidationAuditCompletenessCode.EventMissing
            or ValidationAuditCompletenessCode.PayloadMismatch
            or ValidationAuditCompletenessCode.ScopeIdentityMismatch;

    private static Exception ResolveHandlerException(
        ValidationTrainingFailureAggregate aggregate,
        System.Runtime.ExceptionServices.ExceptionDispatchInfo? bodyException,
        System.Runtime.ExceptionServices.ExceptionDispatchInfo? flushException) =>
        aggregate.SelectPrimaryDispatchInfo()?.SourceException
        ?? flushException?.SourceException
        ?? bodyException!.SourceException;

    private async Task<ValidationTrainingCleanupOutcome> TryReleaseTrainingLeaseAsync(
        ValidationExperiment experiment,
        string leaseOwner,
        CancellationToken cancellationToken)
    {
        try
        {
            var release = await _trainingLease.ReleaseAsync(experiment.Id, leaseOwner, cancellationToken);
            // NotFound is idempotent success (lease already gone). Conflict is authoritative failure.
            if (release.Status is ValidationLeaseOperationStatus.Succeeded
                or ValidationLeaseOperationStatus.NotFound)
            {
                return ValidationTrainingCleanupOutcome.Ok();
            }

            return await PersistCleanupFailureAsync(
                experiment,
                new InvalidOperationException(release.Message ?? "Training lease release was rejected."),
                ValidationTrainingFailurePhase.LeaseRelease,
                cancellationToken);
        }
        catch (Exception ex)
        {
            return await PersistCleanupFailureAsync(
                experiment,
                ex,
                ValidationTrainingFailurePhase.LeaseRelease,
                cancellationToken);
        }
    }

    private async Task<ValidationTrainingCleanupOutcome> TryHeartbeatTrainingLeaseAsync(
        ValidationExperiment experiment,
        string leaseOwner,
        CancellationToken cancellationToken)
    {
        try
        {
            var heartbeat = await _trainingLease.HeartbeatAsync(
                experiment.Id,
                leaseOwner,
                TrainingLeaseTtl,
                cancellationToken);
            if (heartbeat.Status == ValidationLeaseOperationStatus.Succeeded)
            {
                return ValidationTrainingCleanupOutcome.Ok();
            }

            return await PersistCleanupFailureAsync(
                experiment,
                new InvalidOperationException(heartbeat.Message ?? "Training lease heartbeat was rejected."),
                ValidationTrainingFailurePhase.LeaseHeartbeat,
                cancellationToken);
        }
        catch (Exception ex)
        {
            return await PersistCleanupFailureAsync(
                experiment,
                ex,
                ValidationTrainingFailurePhase.LeaseHeartbeat,
                cancellationToken);
        }
    }

    private async Task<ValidationTrainingCleanupOutcome> PersistCleanupFailureAsync(
        ValidationExperiment experiment,
        Exception exception,
        ValidationTrainingFailurePhase phase,
        CancellationToken cancellationToken)
    {
        var aggregate = ValidationTrainingFailurePersistence.MergeExisting(experiment.FailureReasonsJson);
        var originalPrimary = aggregate.PrimaryFailure;
        aggregate.Observe(exception, phase);
        EnsureRecoverableAfterCleanupFailure(experiment, aggregate);

        try
        {
            ValidationTrainingFailurePersistence.ApplyToExperiment(experiment, aggregate);
            experiment.UpdatedAtUtc = DateTime.UtcNow;
            await ValidationTrainingDbRetry.ExecuteAsync(
                    () => _experiments.UpdateAsync(experiment, cancellationToken),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception persistEx)
        {
            aggregate.Observe(persistEx, ValidationTrainingFailurePhase.ExperimentStatusPersistence);
            EnsureRecoverableAfterCleanupFailure(experiment, aggregate);
            ValidationTrainingFailurePersistence.ApplyToExperiment(experiment, aggregate);
            experiment.UpdatedAtUtc = DateTime.UtcNow;
            try
            {
                await ValidationTrainingDbRetry.ExecuteAsync(
                        () => _experiments.UpdateAsync(experiment, cancellationToken),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch
            {
                // Bounded best-effort only — never leak raw persistence details or return success.
            }

            var preserved = aggregate.PrimaryFailure ?? originalPrimary;
            return ValidationTrainingCleanupOutcome.Failed(
                preserved?.Code ?? ValidationTrainingFailureCodes.TrainingCleanupFailed,
                preserved?.UserSafeMessage ?? ValidationTrainingFailureHandler.UserSafeCleanupMessage,
                aggregate);
        }

        var primary = aggregate.PrimaryFailure!;
        return ValidationTrainingCleanupOutcome.Failed(
            primary.Code,
            primary.UserSafeMessage,
            aggregate);
    }

    private static void EnsureRecoverableAfterCleanupFailure(
        ValidationExperiment experiment,
        ValidationTrainingFailureAggregate aggregate)
    {
        experiment.IsQualificationCapable = false;

        // Never leave TrainingCompleted after cleanup failure; keep existing recoverable statuses.
        if (experiment.Status == ValidationExperimentStatus.TrainingCompleted
            || ValidationLifecycleGate.IsTrainingInProgress(experiment.Status))
        {
            experiment.Status = ValidationExperimentStatus.Failed;
            if (string.IsNullOrWhiteSpace(experiment.CurrentStage)
                || experiment.CurrentStage is "Training" or "TrainingCompleted" or "ResumeTraining")
            {
                experiment.CurrentStage = "CleanupFailed";
            }
        }

        var primary = aggregate.PrimaryFailure;
        if (primary is not null)
        {
            experiment.ErrorMessage = primary.UserSafeMessage;
            experiment.PrimaryFailureReason = primary.Code;
        }
    }

    private async Task<ServiceResult<ValidationExperimentDto>> ApplyCleanupOutcomeToResultAsync(
        ValidationExperiment experiment,
        string leaseOwner,
        ServiceResult<ValidationExperimentDto> result,
        CancellationToken cancellationToken)
    {
        var cleanup = await TryReleaseTrainingLeaseAsync(experiment, leaseOwner, cancellationToken);
        if (cleanup.Succeeded)
        {
            return result;
        }

        var primary = ValidationTrainingFailurePersistence
            .MergeExisting(experiment.FailureReasonsJson)
            .PrimaryFailure;
        return ServiceResult<ValidationExperimentDto>.Fail(
            primary?.UserSafeMessage
            ?? cleanup.UserSafeErrorMessage
            ?? result.ErrorMessage
            ?? ValidationTrainingFailureHandler.UserSafeCleanupMessage,
            primary?.Code ?? cleanup.ErrorCode ?? result.ErrorField);
    }

    private async Task PersistScopeDisposalFailureAsync(
        ValidationExperiment experiment,
        ValidationParameterTrial trial,
        ExceptionDispatchInfo disposal,
        CancellationToken cancellationToken)
    {
        var aggregate = new ValidationTrainingFailureAggregate();
        aggregate.ObserveDispatchInfo(disposal, ValidationTrainingFailurePhase.ScopeDisposal);
        EnsureRecoverableAfterCleanupFailure(experiment, aggregate);
        ValidationTrainingFailurePersistence.ApplyTrialWarnings(trial, aggregate);
        ValidationTrainingFailurePersistence.ApplyToExperiment(experiment, aggregate);
        experiment.UpdatedAtUtc = DateTime.UtcNow;
        try
        {
            await _trials.UpdateAsync(trial, cancellationToken);
            await _experiments.UpdateAsync(experiment, cancellationToken);
        }
        catch (Exception persistEx)
        {
            try
            {
                aggregate.Observe(persistEx, ValidationTrainingFailurePhase.ExperimentStatusPersistence);
                ValidationTrainingFailurePersistence.ApplyToExperiment(experiment, aggregate);
            }
            catch
            {
                // Best-effort only.
            }
        }
    }

    private async Task EnsureTrialRowsAsync(
        ValidationExperiment experiment,
        IReadOnlyList<Dictionary<string, string>> combos,
        CancellationToken cancellationToken)
    {
        for (var i = 0; i < combos.Count; i++)
        {
            var combo = combos[i];
            var fingerprint = ParameterFingerprint(combo);
            var existing = await _trials.GetByExperimentAndFingerprintAsync(
                experiment.Id, fingerprint, cancellationToken);
            if (existing is not null)
            {
                continue;
            }

            await _trials.AddAsync(new ValidationParameterTrial
            {
                ValidationExperimentId = experiment.Id,
                TrialNumber = i + 1,
                ParameterSnapshotJson = JsonSerializer.Serialize(combo, JsonOptions),
                ParameterFingerprint = fingerprint,
                Status = ValidationTrialStatus.Pending,
                GuardrailDecision = "NotEvaluated"
            }, cancellationToken);
        }
    }

    private async Task MarkInterruptedRunningTrialsAsync(long experimentId, CancellationToken cancellationToken)
    {
        var trials = await _trials.GetByExperimentIdAsync(experimentId, cancellationToken);
        foreach (var trial in trials.Where(t => t.Status == ValidationTrialStatus.Running))
        {
            trial.Status = ValidationTrialStatus.Interrupted;
            trial.ErrorMessage ??= "Marked interrupted because no active owner was detected on resume.";
            trial.CompletedAtUtc = DateTime.UtcNow;
            await _trials.UpdateAsync(trial, cancellationToken);
        }
    }

    /// <summary>
    /// Milestone 23.0D WP23 — explicit MetricsVersion routing. ValidationMetrics/v1.3.2
    /// experiments use the trial metrics calculator (path inputs + population contract +
    /// persisted snapshot); older versions keep the legacy summary/candidate mapping.
    /// </summary>
    private async Task PopulateTrialMetricsAsync(
        ValidationExperiment experiment,
        ValidationParameterTrial trial,
        IReadOnlyDictionary<string, string> combo,
        StrategyLabRun run,
        ValidationQualificationProfile profile,
        CancellationToken cancellationToken)
    {
        var candidates = await _candidates.GetByRunIdAsync(run.Id, cancellationToken);
        trial.ParameterSnapshotJson = JsonSerializer.Serialize(combo, JsonOptions);
        _trialMetricsRouter.ApplyTrialMetrics(experiment, trial, run, candidates, profile);
    }

    private async Task UpdateExperimentProgressAsync(
        ValidationExperiment experiment,
        int generatedTrialCount,
        CancellationToken cancellationToken)
    {
        var trials = await _trials.GetByExperimentIdAsync(experiment.Id, cancellationToken);
        var progress = ValidationTrainingProgressCalculator.Calculate(experiment, trials, generatedTrialCount);
        experiment.PercentComplete = progress.ProgressPercent;
        experiment.UpdatedAtUtc = DateTime.UtcNow;
        await _experiments.UpdateAsync(experiment, cancellationToken);
    }

    private async Task<ServiceResult<ValidationExperimentDto>> FinalizeTrainingAsync(
        ValidationExperiment experiment,
        DraftConfiguration draft,
        int comboCount,
        CancellationToken cancellationToken,
        string leaseOwner)
    {
        // Milestone 23.0E2C3A1 — scan readable negative evidence before population load / revalidation.
        if (await TryBlockTrainingOnNegativeEvidenceAsync(experiment, draft, cancellationToken))
        {
            return await ApplyCleanupOutcomeToResultAsync(
                experiment,
                leaseOwner,
                ServiceResult<ValidationExperimentDto>.Fail(
                    experiment.ErrorMessage
                    ?? ValidationTrainingFailureHandler.UserSafeLeakageMessage,
                    experiment.PrimaryFailureReason
                    ?? ValidationTrainingFailureCodes.ValidationDataLeakage),
                cancellationToken);
        }

        var trialPopulationLoad = await ValidationAuthoritativeEvaluationSafety.TryGetTrialsByExperimentIdAsync(
            _trials, experiment, cancellationToken);
        if (!trialPopulationLoad.Succeeded)
        {
            var loadAggregate = trialPopulationLoad.FailureAggregate!;
            ValidationTrainingFailurePersistence.ApplyToExperiment(experiment, loadAggregate);
            experiment.IsQualificationCapable = false;
            experiment.Status = ValidationExperimentStatus.Failed;
            experiment.CurrentStage = "AuditPersistenceFailed";
            experiment.ErrorMessage = loadAggregate.PrimaryFailure?.UserSafeMessage
                ?? ValidationAuthoritativeAuditQualificationEvaluator.UserSafeIncompleteMessage;
            experiment.UpdatedAtUtc = DateTime.UtcNow;
            await _experiments.UpdateAsync(experiment, cancellationToken);
            return await ApplyCleanupOutcomeToResultAsync(
                experiment,
                leaseOwner,
                ServiceResult<ValidationExperimentDto>.Fail(
                    experiment.ErrorMessage,
                    loadAggregate.PrimaryFailure?.Code
                    ?? ValidationTrainingFailureCodes.ValidationAccessAuditPersistenceFailed),
                cancellationToken);
        }

        var trialEntities = trialPopulationLoad.Trials!.ToList();
        var useSnapshotSelection =
            ValidationMetricsContract.IsPopulationPathMetricsVersion(experiment.ValidationMetricsVersion);

        // Milestone 23.0E2C3 — revalidate authoritative audit evidence before ranking/selection.
        var populationRevalidation = await ValidationAuthoritativeEvaluationSafety.TryRevalidatePopulationAsync(
            _authoritativeAuditQualification,
            experiment,
            trialEntities,
            cancellationToken);
        if (!populationRevalidation.Succeeded)
        {
            var populationAggregate = populationRevalidation.FailureAggregate!;
            ValidationTrainingFailurePersistence.ApplyToExperiment(experiment, populationAggregate);
            experiment.IsQualificationCapable = false;
            experiment.Status = ValidationExperimentStatus.Failed;
            experiment.CurrentStage = "AuditPersistenceFailed";
            experiment.ErrorMessage = populationAggregate.PrimaryFailure?.UserSafeMessage
                ?? ValidationAuthoritativeAuditQualificationEvaluator.UserSafeIncompleteMessage;
            experiment.UpdatedAtUtc = DateTime.UtcNow;
            await _experiments.UpdateAsync(experiment, cancellationToken);
            return await ApplyCleanupOutcomeToResultAsync(
                experiment,
                leaseOwner,
                ServiceResult<ValidationExperimentDto>.Fail(
                    experiment.ErrorMessage,
                    populationAggregate.PrimaryFailure?.Code
                    ?? ValidationTrainingFailureCodes.ValidationAccessAuditPersistenceFailed),
                cancellationToken);
        }
        foreach (var trial in trialEntities)
        {
            await _trials.UpdateAsync(trial, cancellationToken);
        }

        ValidationTrialRanker.AssignRanks(trialEntities, useSnapshotSelection);
        foreach (var trial in trialEntities)
        {
            await _trials.UpdateAsync(trial, cancellationToken);
        }

        var selection = _trainingSelection.FinalizeTrainingSelection(experiment, trialEntities);
        experiment.TrialPopulationSummaryJson = JsonSerializer.Serialize(selection.Population, JsonOptions);
        experiment.SelectionIntegrityStatus = selection.IntegrityStatus;

        if (selection.ShouldFailExperiment)
        {
            experiment.SelectedTrialId = null;
            experiment.SelectedTrialNumber = null;
            experiment.SelectedTrialParameterSnapshotJson = null;
            experiment.SelectedTrialParameterFingerprint = null;
            experiment.SelectedMetricFingerprint = null;
            experiment.TrainingStrategyLabRunId = null;
            experiment.ValidationStrategyLabRunId = null;
            experiment.FrozenStrategyParameterSnapshotJson = null;
            experiment.FrozenParameterFingerprint = null;
            experiment.FrozenAtUtc = null;
            experiment.Status = ValidationExperimentStatus.Failed;
            experiment.CurrentStage = selection.AuditEvidenceIncomplete
                ? "AuditPersistenceFailed"
                : "FailedNoEligibleTrials";
            experiment.StrategyRobustnessDecision = selection.FailureCode;
            experiment.DecisionExplanation = selection.FailureMessage;
            experiment.PercentComplete = 100m;
            experiment.DecidedAtUtc = DateTime.UtcNow;
            experiment.IsQualificationCapable = false;

            var existingFailures = ValidationTrainingFailurePersistence.MergeExisting(experiment.FailureReasonsJson);
            var selectionAggregate = new ValidationTrainingFailureAggregate();
            if (selection.AuditEvidenceIncomplete)
            {
                selectionAggregate.Observe(new ValidationTrainingFailureRecord
                {
                    Code = ValidationTrainingFailureCodes.ValidationAccessAuditPersistenceFailed,
                    Category = ValidationTrainingFailureCategory.AuditDurability,
                    Precedence = ValidationTrainingFailurePrecedence.AuditDurability,
                    Phase = ValidationTrainingFailurePhase.CompletenessVerification,
                    UserSafeMessage = selection.FailureMessage
                        ?? ValidationAuthoritativeAuditQualificationEvaluator.UserSafeIncompleteMessage,
                    OccurredAtUtc = DateTime.UtcNow,
                    IsQualificationBlocking = true
                });
                experiment.ErrorMessage = selection.FailureMessage
                    ?? ValidationAuthoritativeAuditQualificationEvaluator.UserSafeIncompleteMessage;
            }
            else if (!existingFailures.HasBoundaryFailure
                && !existingFailures.HasAuditDurabilityFailure
                && !existingFailures.HasTrialExecutionFailure
                && !string.IsNullOrWhiteSpace(selection.FailureCode?.ToString()))
            {
                selectionAggregate.Observe(new ValidationTrainingFailureRecord
                {
                    Code = selection.FailureCode.ToString()!,
                    Category = ValidationTrainingFailureCategory.TrialExecution,
                    Precedence = ValidationTrainingFailurePrecedence.TrialExecution,
                    Phase = ValidationTrainingFailurePhase.ExperimentStatusPersistence,
                    UserSafeMessage = selection.FailureMessage ?? "No eligible training trials.",
                    OccurredAtUtc = DateTime.UtcNow,
                    IsQualificationBlocking = true
                });
                experiment.ErrorMessage = selection.FailureMessage;
            }
            else
            {
                experiment.ErrorMessage = existingFailures.PrimaryFailure?.UserSafeMessage
                    ?? selection.FailureMessage;
            }

            ValidationTrainingFailurePersistence.ApplyToExperiment(experiment, selectionAggregate);
            AppendDiagnostic(experiment, selection.FailureCode?.ToString()
                ?? (selection.AuditEvidenceIncomplete
                    ? ValidationTrainingFailureCodes.ValidationAccessAuditPersistenceFailed
                    : "FailedNoEligibleTrials"),
                selection.FailureMessage ?? string.Empty);
            experiment.UpdatedAtUtc = DateTime.UtcNow;
            await _experiments.UpdateAsync(experiment, cancellationToken);

            var closed = ValidationTrainingFailurePersistence.MergeExisting(experiment.FailureReasonsJson);
            var mustFailResult = closed.HasBoundaryFailure
                || closed.HasAuditDurabilityFailure
                || closed.HasCleanupFailure;
            var selectionResult = mustFailResult
                ? ServiceResult<ValidationExperimentDto>.Fail(
                    closed.PrimaryFailure?.UserSafeMessage
                    ?? experiment.ErrorMessage
                    ?? selection.FailureMessage
                    ?? "Training failed.",
                    closed.PrimaryFailure?.Code ?? selection.FailureCode?.ToString())
                : ServiceResult<ValidationExperimentDto>.Ok(MapDto(experiment));
            return await ApplyCleanupOutcomeToResultAsync(
                experiment,
                leaseOwner,
                selectionResult,
                cancellationToken);
        }

        // Existing cleanup / boundary / audit / trial failure evidence must block successful finalization.
        var authoritativeFailures = ValidationTrainingFailurePersistence.MergeExisting(experiment.FailureReasonsJson);
        if (authoritativeFailures.HasAnyFailure)
        {
            experiment.IsQualificationCapable = false;
            EnsureRecoverableAfterCleanupFailure(experiment, authoritativeFailures);
            experiment.UpdatedAtUtc = DateTime.UtcNow;
            await _experiments.UpdateAsync(experiment, cancellationToken);
            return await ApplyCleanupOutcomeToResultAsync(
                experiment,
                leaseOwner,
                ServiceResult<ValidationExperimentDto>.Fail(
                    authoritativeFailures.PrimaryFailure?.UserSafeMessage
                    ?? ValidationTrainingFailureHandler.UserSafeCleanupMessage,
                    authoritativeFailures.PrimaryFailure?.Code
                    ?? ValidationTrainingFailureCodes.TrainingCleanupFailed),
                cancellationToken);
        }

        var winner = selection.SelectedTrial;
        if (winner is not null)
        {
            experiment.SelectedTrialId = winner.Id;
            experiment.SelectedTrialNumber = winner.TrialNumber;
            experiment.SelectedTrialParameterSnapshotJson = winner.ParameterSnapshotJson;
            experiment.SelectedTrialParameterFingerprint = winner.ParameterFingerprint;
            experiment.SelectedMetricFingerprint = winner.TrialMetricFingerprint;
            draft.Parameters = DeserializeStringDictionary(winner.ParameterSnapshotJson);
            experiment.DraftConfigurationJson = SerializeDraft(draft);
            experiment.TrainingStrategyLabRunId = winner.StrategyLabRunId;
            experiment.IsQualificationCapable =
                selection.IntegrityStatus != ValidationSelectionIntegrityStatus.InfrastructureOnlyFallback;

            if (winner.StrategyLabRunId is long trainRunId)
            {
                await _segmentResultWriter.BuildAndPersistSegmentResultsAsync(
                    experiment,
                    trainRunId,
                    ValidationSegmentType.Training,
                    experiment.TrainingCandleCount,
                    cancellationToken);
            }

            if (useSnapshotSelection)
            {
                // WP21 — the selected trial's persisted metric snapshot must reproduce the
                // RawStrategy training segment result; a mismatch blocks freeze.
                var reconciliation = await _trialSegmentReconciliation.ReconcileAsync(
                    experiment, winner, cancellationToken);
                experiment.TrialSegmentReconciliationStatus = reconciliation.Status;
                experiment.TrialSegmentReconciliationJson =
                    ValidationTrialSegmentReconciliationService.Serialize(reconciliation);
                if (reconciliation.Status == ValidationTrialSegmentReconciliationStatus.Mismatched)
                {
                    AppendDiagnostic(
                        experiment,
                        ValidationTrialSegmentReconciliationReport.MismatchCode,
                        string.Join("; ", reconciliation.MismatchReasons));
                }
            }
        }

        var stability = ValidationParameterStabilityAnalyzer.AnalyzeForExperimentType(
            experiment.ExperimentType, trialEntities);
        experiment.ParameterStabilityJson = ValidationParameterStabilityAnalyzer.Serialize(stability);
        experiment.ParameterStabilityApplicability = stability.Applicability;

        if (experiment.ValidationStartUtc is not null
            && experiment.TrainingStartUtc is not null
            && experiment.TrainingEndUtc is not null)
        {
            if (await TryFinalizeLeakageOrBlockTrainingAsync(experiment, draft, leaseOwner, cancellationToken))
            {
                return await ApplyCleanupOutcomeToResultAsync(
                    experiment,
                    leaseOwner,
                    ServiceResult<ValidationExperimentDto>.Fail(
                        experiment.ErrorMessage
                        ?? ValidationTrainingFailureHandler.UserSafeLeakageMessage,
                        experiment.PrimaryFailureReason
                        ?? ValidationTrainingFailureCodes.ValidationDataLeakage),
                    cancellationToken);
            }
        }
        else if (experiment.LeakageAuditStatus != ValidationLeakageAuditStatus.Failed)
        {
            experiment.LeakageAuditStatus = ValidationLeakageAuditStatus.NotAvailable;
        }

        // Re-check immediately before success assignment — no path may reverse earlier ineligibility.
        authoritativeFailures = ValidationTrainingFailurePersistence.MergeExisting(experiment.FailureReasonsJson);
        if (authoritativeFailures.HasAnyFailure)
        {
            experiment.IsQualificationCapable = false;
            EnsureRecoverableAfterCleanupFailure(experiment, authoritativeFailures);
            experiment.UpdatedAtUtc = DateTime.UtcNow;
            await _experiments.UpdateAsync(experiment, cancellationToken);
            return await ApplyCleanupOutcomeToResultAsync(
                experiment,
                leaseOwner,
                ServiceResult<ValidationExperimentDto>.Fail(
                    authoritativeFailures.PrimaryFailure?.UserSafeMessage
                    ?? ValidationTrainingFailureHandler.UserSafeCleanupMessage,
                    authoritativeFailures.PrimaryFailure?.Code
                    ?? ValidationTrainingFailureCodes.TrainingCleanupFailed),
                cancellationToken);
        }

        // Milestone 23.0E2C3 — selected trial must currently verify authoritative audit completion.
        if (winner is not null
            && selection.IntegrityStatus != ValidationSelectionIntegrityStatus.InfrastructureOnlyFallback
            && ValidationAuthoritativeAuditQualificationEvaluator.IsTrainingAuditQualificationApplicable(experiment))
        {
            var selectedReload = await ValidationAuthoritativeEvaluationSafety.TryGetTrialByFingerprintAsync(
                _trials, experiment, winner.ParameterFingerprint, cancellationToken);
            if (!selectedReload.Succeeded)
            {
                var reloadAggregate = selectedReload.FailureAggregate!;
                ValidationTrainingFailurePersistence.ApplyToExperiment(experiment, reloadAggregate);
                experiment.IsQualificationCapable = false;
                experiment.Status = ValidationExperimentStatus.Failed;
                experiment.CurrentStage = "AuditPersistenceFailed";
                experiment.ErrorMessage = reloadAggregate.PrimaryFailure?.UserSafeMessage
                    ?? ValidationAuthoritativeAuditQualificationEvaluator.UserSafeIncompleteMessage;
                experiment.UpdatedAtUtc = DateTime.UtcNow;
                await _experiments.UpdateAsync(experiment, cancellationToken);
                return await ApplyCleanupOutcomeToResultAsync(
                    experiment,
                    leaseOwner,
                    ServiceResult<ValidationExperimentDto>.Fail(
                        experiment.ErrorMessage,
                        reloadAggregate.PrimaryFailure?.Code
                        ?? ValidationTrainingFailureCodes.ValidationAccessAuditPersistenceFailed),
                    cancellationToken);
            }

            var selectedFresh = selectedReload.Trial ?? winner;
            var selectedAuditAttempt = await ValidationAuthoritativeEvaluationSafety.TryEvaluateTrialAsync(
                _authoritativeAuditQualification,
                experiment,
                selectedFresh,
                cancellationToken);
            if (!selectedAuditAttempt.Succeeded)
            {
                var auditAggregate = selectedAuditAttempt.FailureAggregate!;
                ValidationTrainingFailurePersistence.ApplyToExperiment(experiment, auditAggregate);
                experiment.IsQualificationCapable = false;
                experiment.Status = ValidationExperimentStatus.Failed;
                experiment.CurrentStage = "AuditPersistenceFailed";
                experiment.ErrorMessage = auditAggregate.PrimaryFailure?.UserSafeMessage
                    ?? ValidationAuthoritativeAuditQualificationEvaluator.UserSafeIncompleteMessage;
                experiment.UpdatedAtUtc = DateTime.UtcNow;
                await _experiments.UpdateAsync(experiment, cancellationToken);
                return await ApplyCleanupOutcomeToResultAsync(
                    experiment,
                    leaseOwner,
                    ServiceResult<ValidationExperimentDto>.Fail(
                        experiment.ErrorMessage,
                        auditAggregate.PrimaryFailure?.Code
                        ?? ValidationTrainingFailureCodes.ValidationAccessAuditPersistenceFailed),
                    cancellationToken);
            }

            var selectedAudit = selectedAuditAttempt.Evaluation!;
            if (!selectedAudit.IsQualificationEligible)
            {
                ValidationAuthoritativeAuditQualificationEvaluator.ApplyPopulationMarker(
                    selectedFresh, selectedAudit);
                await _trials.UpdateAsync(selectedFresh, cancellationToken);

                experiment.SelectedTrialId = null;
                experiment.SelectedTrialNumber = null;
                experiment.SelectedTrialParameterSnapshotJson = null;
                experiment.SelectedTrialParameterFingerprint = null;
                experiment.SelectedMetricFingerprint = null;
                experiment.FrozenStrategyParameterSnapshotJson = null;
                experiment.FrozenParameterFingerprint = null;
                experiment.FrozenAtUtc = null;
                experiment.IsQualificationCapable = false;
                experiment.Status = ValidationExperimentStatus.Failed;
                experiment.CurrentStage = "AuditPersistenceFailed";
                experiment.ErrorMessage = selectedAudit.UserSafeBlockingReason
                    ?? ValidationAuthoritativeAuditQualificationEvaluator.UserSafeIncompleteMessage;

                var auditAggregate = new ValidationTrainingFailureAggregate();
                auditAggregate.Observe(new ValidationTrainingFailureRecord
                {
                    Code = ValidationTrainingFailureCodes.ValidationAccessAuditPersistenceFailed,
                    Category = ValidationTrainingFailureCategory.AuditDurability,
                    Precedence = ValidationTrainingFailurePrecedence.AuditDurability,
                    Phase = ValidationTrainingFailurePhase.CompletenessVerification,
                    UserSafeMessage = experiment.ErrorMessage,
                    OccurredAtUtc = DateTime.UtcNow,
                    IsQualificationBlocking = true
                });
                ValidationTrainingFailurePersistence.ApplyToExperiment(experiment, auditAggregate);
                experiment.UpdatedAtUtc = DateTime.UtcNow;
                await _experiments.UpdateAsync(experiment, cancellationToken);
                return await ApplyCleanupOutcomeToResultAsync(
                    experiment,
                    leaseOwner,
                    ServiceResult<ValidationExperimentDto>.Fail(
                        experiment.ErrorMessage,
                        ValidationTrainingFailureCodes.ValidationAccessAuditPersistenceFailed),
                    cancellationToken);
            }
        }

        if (selection.IntegrityStatus == ValidationSelectionIntegrityStatus.InfrastructureOnlyFallback)
        {
            experiment.IsQualificationCapable = false;
        }

        experiment.Status = ValidationExperimentStatus.TrainingCompleted;
        experiment.CurrentStage = "TrainingCompleted";
        experiment.PercentComplete = 75m;
        experiment.UpdatedAtUtc = DateTime.UtcNow;
        await _experiments.UpdateAsync(experiment, cancellationToken);

        var releaseOutcome = await TryReleaseTrainingLeaseAsync(experiment, leaseOwner, cancellationToken);
        if (!releaseOutcome.Succeeded)
        {
            var primary = ValidationTrainingFailurePersistence
                .MergeExisting(experiment.FailureReasonsJson)
                .PrimaryFailure;
            return ServiceResult<ValidationExperimentDto>.Fail(
                primary?.UserSafeMessage
                ?? releaseOutcome.UserSafeErrorMessage
                ?? ValidationTrainingFailureHandler.UserSafeCleanupMessage,
                primary?.Code
                ?? releaseOutcome.ErrorCode
                ?? ValidationTrainingFailureCodes.TrainingCleanupFailed);
        }

        return ServiceResult<ValidationExperimentDto>.Ok(MapDto(experiment));
    }

    /// <summary>
    /// Milestone 23.0E2C3A1 — observe readable negative evidence before lower-precedence blockers.
    /// Returns true when training must fail closed on Boundary or access-load AuditDurability.
    /// </summary>
    private async Task<bool> TryBlockTrainingOnNegativeEvidenceAsync(
        ValidationExperiment experiment,
        DraftConfiguration draft,
        CancellationToken cancellationToken)
    {
        if (experiment.ValidationStartUtc is null
            || experiment.TrainingStartUtc is null
            || experiment.TrainingEndUtc is null)
        {
            return false;
        }

        var optimizerFp = _parameterFingerprint.ComputeFingerprint(draft.Parameters);
        IReadOnlyList<ValidationCandleAccessAudit> allAudits;
        try
        {
            allAudits = await _candleAccessAudits.GetByExperimentIdAsync(experiment.Id, cancellationToken);
        }
        catch (Exception loadEx)
        {
            var loadAggregate = ValidationAuthoritativeEvaluationSafety.ObserveRepositoryException(
                experiment, loadEx);
            ValidationTrainingFailurePersistence.ApplyToExperiment(experiment, loadAggregate);
            experiment.IsQualificationCapable = false;
            experiment.Status = ValidationExperimentStatus.Failed;
            experiment.CurrentStage = "AuditPersistenceFailed";
            experiment.ErrorMessage = loadAggregate.PrimaryFailure?.UserSafeMessage
                ?? ValidationAuthoritativeAuditQualificationEvaluator.UserSafeIncompleteMessage;
            experiment.UpdatedAtUtc = DateTime.UtcNow;
            await _experiments.UpdateAsync(experiment, cancellationToken);
            return true;
        }

        var deniedOrLeakage = ValidationNegativeEvidenceGate.Scan(allAudits);
        if (deniedOrLeakage.Count == 0)
        {
            return false;
        }

        var aggregate = ValidationNegativeEvidenceGate.BuildBoundaryAggregate(experiment);
        ValidationNegativeEvidenceGate.UpdateLeakageAuditJsonFromNegativeRows(
            experiment, deniedOrLeakage, _leakageAuditor, optimizerFp);
        ValidationNegativeEvidenceGate.ApplyBoundaryBlock(experiment, aggregate, invalidateTentativeSelection: true);
        experiment.Status = ValidationExperimentStatus.Failed;
        experiment.CurrentStage = "LeakageDetected";
        experiment.ErrorMessage = aggregate.PrimaryFailure?.UserSafeMessage
            ?? ValidationTrainingFailureHandler.UserSafeLeakageMessage;
        experiment.UpdatedAtUtc = DateTime.UtcNow;
        await _experiments.UpdateAsync(experiment, cancellationToken);
        return true;
    }

    /// <summary>
    /// Returns true when leakage finalization blocks training completion.
    /// </summary>
    private async Task<bool> TryFinalizeLeakageOrBlockTrainingAsync(
        ValidationExperiment experiment,
        DraftConfiguration draft,
        string leaseOwner,
        CancellationToken cancellationToken)
    {
        _ = leaseOwner;
        var optimizerFp = _parameterFingerprint.ComputeFingerprint(draft.Parameters);
        IReadOnlyList<ValidationCandleAccessAudit> allAudits;
        try
        {
            allAudits = await _candleAccessAudits.GetByExperimentIdAsync(experiment.Id, cancellationToken);
        }
        catch (Exception loadEx)
        {
            var loadAggregate = ValidationAuthoritativeEvaluationSafety.ObserveRepositoryException(
                experiment,
                loadEx);
            ValidationTrainingFailurePersistence.ApplyToExperiment(experiment, loadAggregate);
            experiment.IsQualificationCapable = false;
            experiment.Status = ValidationExperimentStatus.Failed;
            experiment.CurrentStage = "AuditPersistenceFailed";
            experiment.ErrorMessage = loadAggregate.PrimaryFailure?.UserSafeMessage
                ?? ValidationAuthoritativeAuditQualificationEvaluator.UserSafeIncompleteMessage;
            experiment.UpdatedAtUtc = DateTime.UtcNow;
            await _experiments.UpdateAsync(experiment, cancellationToken);
            return true;
        }

        var deniedOrLeakage = ValidationNegativeEvidenceGate.Scan(allAudits);
        if (deniedOrLeakage.Count > 0)
        {
            var aggregate = ValidationNegativeEvidenceGate.BuildBoundaryAggregate(experiment);
            ValidationNegativeEvidenceGate.UpdateLeakageAuditJsonFromNegativeRows(
                experiment, deniedOrLeakage, _leakageAuditor, optimizerFp);
            ValidationNegativeEvidenceGate.ApplyBoundaryBlock(experiment, aggregate, invalidateTentativeSelection: true);
            experiment.Status = ValidationExperimentStatus.Failed;
            experiment.CurrentStage = "LeakageDetected";
            experiment.ErrorMessage = aggregate.PrimaryFailure?.UserSafeMessage
                ?? ValidationTrainingFailureHandler.UserSafeLeakageMessage;
            experiment.UpdatedAtUtc = DateTime.UtcNow;
            await _experiments.UpdateAsync(experiment, cancellationToken);
            return true;
        }

        await FinalizePositiveLeakageEvidenceAsync(experiment, draft, allAudits, optimizerFp, cancellationToken);

        var postPositiveFailures = ValidationTrainingFailurePersistence.MergeExisting(experiment.FailureReasonsJson);
        if (postPositiveFailures.IsQualificationBlocking)
        {
            experiment.IsQualificationCapable = false;
            experiment.Status = ValidationExperimentStatus.Failed;
            experiment.CurrentStage = postPositiveFailures.HasBoundaryFailure
                ? "LeakageDetected"
                : "AuditPersistenceFailed";
            experiment.ErrorMessage = postPositiveFailures.PrimaryFailure?.UserSafeMessage
                ?? ValidationAuthoritativeAuditQualificationEvaluator.UserSafeIncompleteMessage;
            experiment.UpdatedAtUtc = DateTime.UtcNow;
            await _experiments.UpdateAsync(experiment, cancellationToken);
            return true;
        }

        if (experiment.LeakageAuditStatus == ValidationLeakageAuditStatus.Failed)
        {
            var aggregate = ValidationNegativeEvidenceGate.BuildBoundaryAggregate(experiment);
            ValidationNegativeEvidenceGate.ApplyBoundaryBlock(experiment, aggregate, invalidateTentativeSelection: true);
            experiment.Status = ValidationExperimentStatus.Failed;
            experiment.CurrentStage = "LeakageDetected";
            experiment.ErrorMessage = aggregate.PrimaryFailure?.UserSafeMessage
                ?? ValidationTrainingFailureHandler.UserSafeLeakageMessage;
            experiment.UpdatedAtUtc = DateTime.UtcNow;
            await _experiments.UpdateAsync(experiment, cancellationToken);
            return true;
        }

        return false;
    }

    private async Task FinalizePositiveLeakageEvidenceAsync(
        ValidationExperiment experiment,
        DraftConfiguration draft,
        IReadOnlyList<ValidationCandleAccessAudit> allAudits,
        string optimizerFp,
        CancellationToken cancellationToken)
    {
        _ = draft;
        var trialLoad = await ValidationAuthoritativeEvaluationSafety.TryGetTrialsByExperimentIdAsync(
            _trials, experiment, cancellationToken);
        if (!trialLoad.Succeeded)
        {
            ValidationTrainingFailurePersistence.ApplyToExperiment(experiment, trialLoad.FailureAggregate!);
            experiment.IsQualificationCapable = false;
            return;
        }

        var trials = trialLoad.Trials!;
        var evaluations = new List<(ValidationParameterTrial Trial, ValidationAuthoritativeAuditQualificationResult Evaluation)>();
        foreach (var trial in trials)
        {
            if (!ValidationAuthoritativeAuditQualificationEvaluator.IsGuardrailPassedCompleted(trial))
            {
                continue;
            }

            var evaluationAttempt = await ValidationAuthoritativeEvaluationSafety.TryEvaluateTrialAsync(
                _authoritativeAuditQualification,
                experiment,
                trial,
                cancellationToken);
            if (!evaluationAttempt.Succeeded)
            {
                var auditAggregate = evaluationAttempt.FailureAggregate!;
                ValidationTrainingFailurePersistence.ApplyToExperiment(experiment, auditAggregate);
                experiment.IsQualificationCapable = false;
                return;
            }

            evaluations.Add((trial, evaluationAttempt.Evaluation!));
        }

        var positiveSelection = ValidationLeakageEvidenceSelector.SelectPositiveEvidence(allAudits, evaluations);
        if (positiveSelection.AuthoritativeEvidenceIncomplete || positiveSelection.PositiveRows.Count == 0)
        {
            experiment.LeakageAuditStatus = ValidationLeakageAuditStatus.NotAvailable;
            experiment.LeakageAuditJson = _leakageAuditor.Serialize(new ValidationLeakageAuditReport
            {
                Status = ValidationLeakageAuditStatus.NotAvailable,
                ValidationStartUtc = experiment.ValidationStartUtc!.Value,
                TrainingStartUtc = experiment.TrainingStartUtc!.Value,
                TrainingEndUtc = experiment.TrainingEndUtc!.Value,
                OptimizerInputFingerprint = optimizerFp,
                Reason = positiveSelection.AuthoritativeEvidenceIncomplete
                    ? ValidationAuthoritativeAuditQualificationEvaluator.UserSafeIncompleteMessage
                    : "No authoritative verifier-complete access evidence was available for positive leakage evaluation.",
                BlocksFreezeOrPassed = false,
                AccessEvidenceCount = 0,
                DeniedAccessCount = 0
            });
            return;
        }

        var leakage = _leakageAuditor.EvaluateFromAccessEvidence(
            positiveSelection.PositiveRows,
            experiment.ValidationStartUtc!.Value,
            experiment.TrainingStartUtc!.Value,
            experiment.TrainingEndUtc!.Value,
            optimizerFp);
        experiment.LeakageAuditJson = _leakageAuditor.Serialize(leakage);
        experiment.LeakageAuditStatus = leakage.Status;
    }

    private async Task PersistTrainingCandleAccessLogAsync(
        IValidationTrainingCandleScope scope,
        CancellationToken cancellationToken)
    {
        await _candleAccessRecorder.FlushAsync(scope, cancellationToken);
    }

    private async Task<StrategyLabRun> CreateLabRunAsync(
        ValidationExperiment experiment,
        IReadOnlyDictionary<string, string> parameters,
        DraftConfiguration draft,
        DateTime fromUtc,
        DateTime toUtc,
        string name,
        CancellationToken cancellationToken)
    {
        var feeJson = JsonSerializer.Serialize(new
        {
            makerFeeRate = draft.MakerFeeRate,
            takerFeeRate = draft.TakerFeeRate
        }, JsonOptions);
        var slipJson = JsonSerializer.Serialize(new { slippagePercent = draft.SlippagePercent }, JsonOptions);
        var featureFlagsJson = JsonSerializer.Serialize(new { observationSettings = draft.ObservationSettings }, JsonOptions);
        var fingerprint = ExperimentFingerprintBuilder.Build(
            experiment.StrategyCode,
            experiment.StrategyVersion,
            experiment.ExchangeId,
            experiment.SymbolId,
            experiment.Symbol,
            experiment.Timeframe,
            fromUtc,
            toUtc,
            StrategyLabExecutionMode.FullPipelineComparison,
            parameters,
            featureFlagsJson,
            experiment.InitialBalance,
            feeJson,
            slipJson);

        var run = new StrategyLabRun
        {
            Name = name,
            StrategyCode = experiment.StrategyCode,
            StrategyVersion = experiment.StrategyVersion,
            ExchangeId = experiment.ExchangeId,
            SymbolId = experiment.SymbolId,
            Symbol = experiment.Symbol,
            Timeframe = experiment.Timeframe,
            FromUtc = fromUtc,
            ToUtc = toUtc,
            ExecutionMode = StrategyLabExecutionMode.FullPipelineComparison,
            ParametersJson = JsonSerializer.Serialize(parameters, JsonOptions),
            StrategyFeatureFlagsJson = featureFlagsJson,
            InitialBalance = experiment.InitialBalance,
            FeeSettingsJson = feeJson,
            SlippageSettingsJson = slipJson,
            Status = StrategyLabRunStatus.Created,
            ExperimentFingerprint = fingerprint,
            AppVersion = "1.0.0",
            StrategyCodeFingerprint = fingerprint,
            RiskProfileId = draft.ObservationSettings?.RiskProfileId,
            CreatedAtUtc = DateTime.UtcNow,
            CandleLoadContractVersion = StrategyLabCandleLoadContractVersions.Current
        };

        await _labRuns.AddAsync(run, cancellationToken);
        return run;
    }

    /// <summary>
    /// Ensures the trial has an active authoritative durable audit execution before candle access.
    /// Recovers/supersedes incomplete prior attempts as needed (WP6/WP10, E2C1B).
    /// </summary>
    private async Task<AuthoritativeAuditExecutionEnsureResult> EnsureAuthoritativeAuditExecutionAsync(
        ValidationExperiment experiment,
        ValidationParameterTrial trial,
        string leaseOwner,
        bool isResume,
        CancellationToken cancellationToken)
    {
        var recoveryRequest = new ValidationAuditExecutionRecoveryRequest
        {
            CurrentLeaseOwner = leaseOwner,
            IsResume = isResume,
            TrialStatus = trial.Status
        };

        if (trial.AuthoritativeAuditExecutionId is Guid existingId)
        {
            var existing = await _auditExecutions.GetByAuditExecutionIdAsync(existingId, cancellationToken);
            if (existing is null)
            {
                // Stale Complete / pointer without durable execution must not mint a new proof on resume.
                if (isResume && trial.Status == ValidationTrialStatus.Completed)
                {
                    return new AuthoritativeAuditExecutionEnsureResult
                    {
                        Execution = new ValidationAuditExecution
                        {
                            AuditExecutionId = existingId,
                            ScopeExecutionId = Guid.Empty,
                            ExecutionToken = "missing",
                            AttemptNumber = trial.AuditAttemptNumber > 0 ? trial.AuditAttemptNumber : 1,
                            ValidationExperimentId = experiment.Id,
                            ValidationTrialId = trial.Id,
                            Status = ValidationAuditExecutionStatus.Failed,
                            StartedAtUtc = DateTime.UtcNow,
                            UpdatedAtUtc = DateTime.UtcNow,
                            AuditContractVersion = ValidationAuditExecution.ContractVersionV1
                        },
                        FailClosed = true,
                        RecoveryDecision = ValidationAuditRecoveryDecision.FailClosed,
                        CompletenessCode = ValidationAuditCompletenessCode.ExecutionMissing
                    };
                }
            }
            else
            {
                EnsureKnownAuditContractVersion(existing);

                if (existing.Status == ValidationAuditExecutionStatus.Superseded
                    && isResume
                    && trial.Status == ValidationTrialStatus.Completed)
                {
                    return new AuthoritativeAuditExecutionEnsureResult
                    {
                        Execution = existing,
                        FailClosed = true,
                        RecoveryDecision = ValidationAuditRecoveryDecision.FailClosed,
                        CompletenessCode = ValidationAuditCompletenessCode.Superseded
                    };
                }

                if (existing.Status == ValidationAuditExecutionStatus.Completed)
                {
                    var completedRecovery = await _auditRecovery.RecoverAsync(
                        existing.AuditExecutionId,
                        recoveryRequest,
                        cancellationToken);

                    existing = await _auditExecutions.GetByAuditExecutionIdAsync(
                        existing.AuditExecutionId, cancellationToken) ?? existing;

                    if (completedRecovery.IsComplete
                        && completedRecovery.RecoveryDecision == ValidationAuditRecoveryDecision.AlreadyCompleted)
                    {
                        return new AuthoritativeAuditExecutionEnsureResult
                        {
                            Execution = existing,
                            VerifiedFinalizationOnly = true,
                            FinalizationOnly = true,
                            RecoveryDecision = ValidationAuditRecoveryDecision.VerifiedFinalizationOnly
                        };
                    }

                    return new AuthoritativeAuditExecutionEnsureResult
                    {
                        Execution = existing,
                        FailClosed = true,
                        RecoveryDecision = ValidationAuditRecoveryDecision.FailClosed,
                        CompletenessCode = ParseCompletenessCode(completedRecovery.FailureCode)
                    };
                }

                if (existing.Status is ValidationAuditExecutionStatus.InProgress
                    or ValidationAuditExecutionStatus.Created
                    or ValidationAuditExecutionStatus.FlushManifested
                    or ValidationAuditExecutionStatus.EventsConfirmed
                    or ValidationAuditExecutionStatus.RecoveryRequired
                    or ValidationAuditExecutionStatus.Failed)
                {
                    var recovery = await _auditRecovery.RecoverAsync(
                        existing.AuditExecutionId,
                        recoveryRequest,
                        cancellationToken);

                    if (recovery.RecoveryDecision == ValidationAuditRecoveryDecision.FinalizationOnlyRecovery)
                    {
                        existing = await _auditExecutions.GetByAuditExecutionIdAsync(
                            existing.AuditExecutionId, cancellationToken) ?? existing;
                        return new AuthoritativeAuditExecutionEnsureResult
                        {
                            Execution = existing,
                            FinalizationOnly = true,
                            RecoveryDecision = recovery.RecoveryDecision
                        };
                    }

                    if (recovery.MustRerunTrial
                        || recovery.RequiresStrategyLabExecution
                        || recovery.RecoveryDecision == ValidationAuditRecoveryDecision.SupersedeAndRerun)
                    {
                        existing = await _auditExecutions.GetByAuditExecutionIdAsync(
                            existing.AuditExecutionId, cancellationToken) ?? existing;
                        if (existing.Status != ValidationAuditExecutionStatus.Superseded
                            && existing.Status != ValidationAuditExecutionStatus.Completed)
                        {
                            var superseded = await _auditSupersession.SupersedeForRerunAsync(
                                existing.AuditExecutionId,
                                newExecutionToken: Guid.NewGuid().ToString("N"),
                                reasonCode: recovery.FailureCode ?? "PREVIOUS_EXECUTION_NOT_TERMINAL",
                                leaseOwner: leaseOwner,
                                cancellationToken: cancellationToken);
                            return new AuthoritativeAuditExecutionEnsureResult
                            {
                                Execution = superseded,
                                RecoveryDecision = ValidationAuditRecoveryDecision.SupersedeAndRerun
                            };
                        }
                    }

                    if (recovery.RecoveryDecision == ValidationAuditRecoveryDecision.NoRecoveryNeeded
                        || recovery.RecoveryDecision == ValidationAuditRecoveryDecision.ConfirmedCommittedBatch)
                    {
                        existing = await _auditExecutions.GetByAuditExecutionIdAsync(
                            existing.AuditExecutionId, cancellationToken) ?? existing;
                        return new AuthoritativeAuditExecutionEnsureResult { Execution = existing };
                    }

                    if (existing.Status is ValidationAuditExecutionStatus.InProgress
                        or ValidationAuditExecutionStatus.Created
                        or ValidationAuditExecutionStatus.FlushManifested
                        or ValidationAuditExecutionStatus.EventsConfirmed)
                    {
                        return new AuthoritativeAuditExecutionEnsureResult { Execution = existing };
                    }
                }
            }
        }

        var token = Guid.NewGuid().ToString("N");
        var created = await _auditExecutionFactory.CreateForTrialAsync(
            experiment,
            trial,
            leaseOwner,
            token,
            cancellationToken);
        return new AuthoritativeAuditExecutionEnsureResult { Execution = created };
    }

    private static ValidationAuditCompletenessCode? ParseCompletenessCode(string? failureCode)
    {
        if (string.IsNullOrWhiteSpace(failureCode))
        {
            return null;
        }

        return Enum.TryParse<ValidationAuditCompletenessCode>(failureCode, out var code)
            ? code
            : null;
    }

    private async Task<ServiceResult<ValidationExperimentDto>?> FinalizeTrialAuditWithVerifierAsync(
        ValidationExperiment experiment,
        ValidationParameterTrial trial,
        IReadOnlyDictionary<string, string> combo,
        string fingerprint,
        ValidationAuditExecution auditExecution,
        string leaseOwner,
        CancellationToken cancellationToken)
    {
        _ = combo;
        var finalExpected = auditExecution.LastConfirmedSequence;
        if (finalExpected <= 0)
        {
            var sequenceFailure = new ValidationAuditExecutionException(
                ValidationAuditCompletenessCode.FinalSequenceMissing.ToString(),
                "Finalization-only recovery requires a positive confirmed sequence.");
            return await FailFinalizationThroughAggregateAsync(
                experiment,
                trial,
                sequenceFailure,
                ValidationTrainingFailurePhase.AuditFinalization,
                leaseOwner,
                cancellationToken);
        }

        ValidationAuditExecutionCompletionResult completion;
        try
        {
            completion = await _auditFinalizer.CompleteAsync(
                auditExecution.AuditExecutionId,
                finalExpected,
                cancellationToken);
        }
        catch (ValidationAuditCompletenessVerificationException ex)
        {
            return await FailFinalizationThroughAggregateAsync(
                experiment,
                trial,
                ex,
                ValidationTrainingFailurePhase.CompletenessVerification,
                leaseOwner,
                cancellationToken);
        }
        catch (Exception ex)
        {
            return await FailFinalizationThroughAggregateAsync(
                experiment,
                trial,
                ex,
                ValidationTrainingFailurePhase.AuditFinalization,
                leaseOwner,
                cancellationToken);
        }

        try
        {
            trial = await _trials.GetByExperimentAndFingerprintAsync(
                experiment.Id, fingerprint, cancellationToken) ?? trial;
        }
        catch (Exception reloadTrialEx)
        {
            return await FailFinalizationThroughAggregateAsync(
                experiment,
                trial,
                reloadTrialEx,
                ValidationTrainingFailurePhase.AuditFinalization,
                leaseOwner,
                cancellationToken);
        }

        try
        {
            auditExecution = await _auditExecutions.GetByAuditExecutionIdAsync(
                auditExecution.AuditExecutionId, cancellationToken) ?? auditExecution;
        }
        catch (Exception reloadExecutionEx)
        {
            return await FailFinalizationThroughAggregateAsync(
                experiment,
                trial,
                reloadExecutionEx,
                ValidationTrainingFailurePhase.AuditFinalization,
                leaseOwner,
                cancellationToken);
        }

        if (!completion.IsComplete)
        {
            var incomplete = new ValidationAuditExecutionException(
                completion.FailureCode ?? completion.CompletionCode.ToString(),
                $"Audit finalization failed: {completion.FailureCode ?? completion.CompletionCode.ToString()}.");
            var phase = IsCompletenessEvidenceCode(completion.CompletionCode)
                ? ValidationTrainingFailurePhase.CompletenessVerification
                : ValidationTrainingFailurePhase.AuditFinalization;
            return await FailFinalizationThroughAggregateAsync(
                experiment,
                trial,
                incomplete,
                phase,
                leaseOwner,
                cancellationToken);
        }

        ValidationAuditCompletenessResult completeness;
        try
        {
            var batches = await _auditBatches.GetByAuditExecutionIdAsync(
                auditExecution.AuditExecutionId, cancellationToken);
            var accessRows = (await _candleAccessAudits.GetByExperimentIdAsync(experiment.Id, cancellationToken))
                .Where(r => r.ScopeExecutionId == auditExecution.ScopeExecutionId)
                .ToList();
            completeness = _auditCompletenessVerifier.Verify(trial, auditExecution, batches, accessRows);
        }
        catch (Exception ex)
        {
            return await FailFinalizationThroughAggregateAsync(
                experiment,
                trial,
                ex,
                ValidationTrainingFailurePhase.CompletenessVerification,
                leaseOwner,
                cancellationToken);
        }

        var metricsPassed = string.Equals(trial.GuardrailDecision, "Passed", StringComparison.OrdinalIgnoreCase)
                            && trial.Status != ValidationTrialStatus.GuardrailRejected
                            && trial.Status != ValidationTrialStatus.Failed;

        if (metricsPassed
            && _trialAuditCompletionGate.CanMarkTrialCompleted(trial, auditExecution, completeness))
        {
            _trialAuditCompletionGate.ApplyCompletedStatus(trial, auditExecution, completeness);
        }
        else if (metricsPassed)
        {
            var completenessFailure = new ValidationAuditExecutionException(
                completeness.CompletionCode.ToString(),
                $"Audit completeness verification failed: {completeness.CompletionCode}.");
            return await FailFinalizationThroughAggregateAsync(
                experiment,
                trial,
                completenessFailure,
                ValidationTrainingFailurePhase.CompletenessVerification,
                leaseOwner,
                cancellationToken);
        }

        await _trials.UpdateAsync(trial, cancellationToken);
        return null;
    }

    private async Task<ServiceResult<ValidationExperimentDto>?> ApplyCompletedTrialAuditRevalidationFailureAsync(
        ValidationExperiment experiment,
        ValidationParameterTrial trial,
        ValidationAuditCompletenessCode? completenessCode,
        string message,
        string leaseOwner,
        CancellationToken cancellationToken)
    {
        var code = completenessCode?.ToString() ?? "AuditEvidenceRevalidationFailed";
        var auditFailure = new ValidationAuditExecutionException(code, message);
        var observed = new ValidationTrainingFailureAggregate();
        observed.Observe(auditFailure, ValidationTrainingFailurePhase.CompletenessVerification);
        ValidationTrainingFailurePersistence.AppendRankIneligibleReasons(trial, [code]);

        var handled = await _trainingFailureHandler.HandleAuditPersistenceFailureAsync(
            experiment,
            trial,
            auditFailure,
            leaseOwner: leaseOwner,
            observedFailures: observed,
            cancellationToken: cancellationToken);

        return ServiceResult<ValidationExperimentDto>.Fail(
            handled.UserSafeErrorMessage,
            handled.ErrorCode);
    }

    private async Task<ServiceResult<ValidationExperimentDto>> FailFinalizationThroughAggregateAsync(
        ValidationExperiment experiment,
        ValidationParameterTrial trial,
        Exception exception,
        ValidationTrainingFailurePhase phase,
        string leaseOwner,
        CancellationToken cancellationToken)
    {
        var observed = new ValidationTrainingFailureAggregate();
        observed.Observe(exception, phase);
        if (exception is ValidationAuditExecutionException auditEx
            && !string.IsNullOrWhiteSpace(auditEx.ErrorCode))
        {
            ValidationTrainingFailurePersistence.AppendRankIneligibleReasons(trial, [auditEx.ErrorCode]);
        }

        var handled = await _trainingFailureHandler.HandleAuditPersistenceFailureAsync(
            experiment,
            trial,
            exception,
            leaseOwner: leaseOwner,
            observedFailures: observed,
            cancellationToken: cancellationToken);

        return ServiceResult<ValidationExperimentDto>.Fail(
            handled.UserSafeErrorMessage,
            handled.ErrorCode);
    }

    private static void EnsureKnownAuditContractVersion(ValidationAuditExecution execution)
    {
        if (!string.Equals(
                execution.AuditContractVersion,
                ValidationAuditExecution.ContractVersionV1,
                StringComparison.Ordinal))
        {
            throw new ValidationAuditExecutionException(
                "VALIDATION_AUDIT_UNKNOWN_CONTRACT_VERSION",
                $"Unknown AuditContractVersion '{execution.AuditContractVersion}'. Expected '{ValidationAuditExecution.ContractVersionV1}'.");
        }
    }
}
