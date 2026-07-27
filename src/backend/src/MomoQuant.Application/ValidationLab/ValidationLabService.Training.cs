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

        experiment.Status = isResume
            ? ValidationExperimentStatus.TrainingResumed
            : ValidationExperimentStatus.TrainingRunning;
        experiment.CurrentStage = isResume ? "ResumeTraining" : "Training";
        experiment.ErrorMessage = null;
        experiment.ValidationRevealStatus = ValidationRevealStatus.Hidden;
        experiment.UpdatedAtUtc = DateTime.UtcNow;
        await _experiments.UpdateAsync(experiment, cancellationToken);

        cancellationToken = CancellationToken.None;

        try
        {
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
                await _trainingLease.ReleaseAsync(experiment.Id, leaseOwner, cancellationToken);
                return ServiceResult<ValidationExperimentDto>.Fail(experiment.ErrorMessage);
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
                await _trainingScopeExecution.ExecuteWithScopeAsync(experiment, scopeRequest, async bootstrapScope =>
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

                    if (trial.Status is ValidationTrialStatus.Completed or ValidationTrialStatus.GuardrailRejected)
                    {
                        await UpdateExperimentProgressAsync(experiment, combos.Count, cancellationToken);
                        await _trainingLease.HeartbeatAsync(experiment.Id, leaseOwner, TrainingLeaseTtl, cancellationToken);
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

                    trial.Status = ValidationTrialStatus.Running;
                    trial.StartedAtUtc = DateTime.UtcNow;
                    trial.ErrorMessage = null;
                    await ValidationTrainingDbRetry.ExecuteAsync(() => _trials.UpdateAsync(trial, cancellationToken));

                    // Recover / supersede incomplete authoritative audit before access.
                    var ensureResult = await EnsureAuthoritativeAuditExecutionAsync(
                        experiment, trial, leaseOwner, isResume, cancellationToken);
                    var auditExecution = ensureResult.Execution;

                    if (ensureResult.FailClosed)
                    {
                        trial.Status = ValidationTrialStatus.AuditPersistenceFailed;
                        trial.ErrorMessage =
                            $"Completed audit execution failed verifier revalidation: {ensureResult.CompletenessCode?.ToString() ?? "FailClosed"}.";
                        trial.AuditCompletionStatus = ValidationAuditCompletionStatus.RecoveryRequired;
                        await ValidationTrainingDbRetry.ExecuteAsync(() => _trials.UpdateAsync(trial, cancellationToken));
                        await UpdateExperimentProgressAsync(experiment, combos.Count, cancellationToken);
                        await _trainingLease.HeartbeatAsync(experiment.Id, leaseOwner, TrainingLeaseTtl, cancellationToken);
                        continue;
                    }

                    if (ensureResult.VerifiedFinalizationOnly || ensureResult.FinalizationOnly)
                    {
                        await FinalizeTrialAuditWithVerifierAsync(
                            experiment,
                            trial,
                            combo,
                            fingerprint,
                            auditExecution,
                            cancellationToken);
                        await UpdateExperimentProgressAsync(experiment, combos.Count, cancellationToken);
                        await _trainingLease.HeartbeatAsync(experiment.Id, leaseOwner, TrainingLeaseTtl, cancellationToken);
                        continue;
                    }

                    if (auditExecution.Status == ValidationAuditExecutionStatus.Completed)
                    {
                        trial.Status = ValidationTrialStatus.AuditPersistenceFailed;
                        trial.ErrorMessage =
                            "Completed audit execution cannot re-enter StrategyLab training scope.";
                        trial.AuditCompletionStatus = ValidationAuditCompletionStatus.RecoveryRequired;
                        await ValidationTrainingDbRetry.ExecuteAsync(() => _trials.UpdateAsync(trial, cancellationToken));
                        await UpdateExperimentProgressAsync(experiment, combos.Count, cancellationToken);
                        await _trainingLease.HeartbeatAsync(experiment.Id, leaseOwner, TrainingLeaseTtl, cancellationToken);
                        continue;
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
                        await _trainingScopeExecution.ExecuteWithScopeAsync(
                            experiment,
                            trialScopeRequest,
                            async trainingScope =>
                            {
                                try
                                {
                                    await _trainingScopeExecution.ExecuteTrialAsync(
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
                                }
                                catch (ValidationTrainingBoundaryException ex)
                                {
                                    var optimizerFp = _parameterFingerprint.ComputeFingerprint(draft.Parameters);
                                    var handled = await _trainingFailureHandler.HandleBoundaryFailureAsync(
                                        experiment,
                                        trial,
                                        trainingScope,
                                        ex,
                                        optimizerInputFingerprint: optimizerFp,
                                        leaseOwner: leaseOwner,
                                        cancellationToken: cancellationToken);
                                    result = ServiceResult<ValidationExperimentDto>.Fail(
                                        handled.UserSafeErrorMessage,
                                        handled.ErrorCode);
                                    return;
                                }

                                // Flush already ran in ExecuteTrialAsync finally; finalize durable audit.
                                if (result is not null)
                                {
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
                                        trial.Status = ValidationTrialStatus.AuditPersistenceFailed;
                                        trial.ErrorMessage =
                                            $"Audit finalization failed: {completion.FailureCode ?? completion.CompletionCode.ToString()}.";
                                        trial.CompletedAtUtc = DateTime.UtcNow;
                                        trial.AuditCompletionStatus = ValidationAuditCompletionStatus.RecoveryRequired;
                                        await ValidationTrainingDbRetry.ExecuteAsync(
                                            () => _trials.UpdateAsync(trial, cancellationToken));
                                        return;
                                    }

                                    var batches = await _auditBatches.GetByAuditExecutionIdAsync(
                                        auditExecution.AuditExecutionId, cancellationToken);
                                    var accessRows = (await _candleAccessAudits.GetByExperimentIdAsync(
                                        experiment.Id, cancellationToken))
                                        .Where(r => r.ScopeExecutionId == auditExecution.ScopeExecutionId)
                                        .ToList();
                                    var completeness = _auditCompletenessVerifier.Verify(
                                        trial, auditExecution, batches, accessRows);

                                    if (_trialAuditCompletionGate.CanMarkTrialCompleted(
                                            trial, auditExecution, completeness))
                                    {
                                        _trialAuditCompletionGate.ApplyCompletedStatus(
                                            trial, auditExecution, completeness);
                                    }
                                    else
                                    {
                                        trial.Status = ValidationTrialStatus.AuditPersistenceFailed;
                                        trial.ErrorMessage =
                                            $"Audit completeness verification failed: {completeness.CompletionCode}.";
                                        trial.CompletedAtUtc = DateTime.UtcNow;
                                        trial.AuditCompletionStatus = ValidationAuditCompletionStatus.RecoveryRequired;
                                    }

                                    await ValidationTrainingDbRetry.ExecuteAsync(
                                        () => _trials.UpdateAsync(trial, cancellationToken));
                                }
                                catch (ValidationAuditExecutionException ex)
                                {
                                    trial.Status = ValidationTrialStatus.AuditPersistenceFailed;
                                    trial.ErrorMessage = ex.Message;
                                    trial.CompletedAtUtc = DateTime.UtcNow;
                                    trial.AuditCompletionStatus = ValidationAuditCompletionStatus.RecoveryRequired;
                                    await ValidationTrainingDbRetry.ExecuteAsync(
                                        () => _trials.UpdateAsync(trial, cancellationToken));
                                }
                            },
                            cancellationToken);

                        if (result is not null)
                        {
                            await _trainingLease.ReleaseAsync(experiment.Id, leaseOwner, cancellationToken);
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
                        await _trainingLease.ReleaseAsync(experiment.Id, leaseOwner, cancellationToken);
                        result = ServiceResult<ValidationExperimentDto>.Fail(
                            handled.UserSafeErrorMessage,
                            handled.ErrorCode);
                        break;
                    }
                    catch (ValidationAuditExecutionIdentityMismatchException ex)
                    {
                        trial.Status = ValidationTrialStatus.AuditPersistenceFailed;
                        trial.ErrorMessage = ex.SafeMessage;
                        trial.CompletedAtUtc = DateTime.UtcNow;
                        await ValidationTrainingDbRetry.ExecuteAsync(() => _trials.UpdateAsync(trial, cancellationToken));
                        await _trainingLease.ReleaseAsync(experiment.Id, leaseOwner, cancellationToken);
                        result = ServiceResult<ValidationExperimentDto>.Fail(ex.SafeMessage, ex.ErrorCode);
                        break;
                    }
                    catch (Exception ex) when (ValidationTrainingDbRetry.IsTransient(ex))
                    {
                        trial.Status = ValidationTrialStatus.Interrupted;
                        trial.ErrorMessage = ex.Message;
                        trial.CompletedAtUtc = DateTime.UtcNow;
                        await ValidationTrainingDbRetry.ExecuteAsync(() => _trials.UpdateAsync(trial, cancellationToken));

                        experiment.Status = ValidationExperimentStatus.TrainingInterrupted;
                        experiment.ErrorMessage = ex.Message;
                        experiment.CurrentStage = "TrainingInterrupted";
                        await UpdateExperimentProgressAsync(experiment, combos.Count, cancellationToken);
                        await _trainingLease.ReleaseAsync(experiment.Id, leaseOwner, cancellationToken);
                        result = ServiceResult<ValidationExperimentDto>.Fail(ex.Message);
                        break;
                    }
                    catch (Exception ex)
                    {
                        trial.Status = ValidationTrialStatus.Failed;
                        trial.ErrorMessage = ex.Message;
                        trial.CompletedAtUtc = DateTime.UtcNow;
                        await ValidationTrainingDbRetry.ExecuteAsync(() => _trials.UpdateAsync(trial, cancellationToken));
                    }

                    await UpdateExperimentProgressAsync(experiment, combos.Count, cancellationToken);
                    await _trainingLease.HeartbeatAsync(experiment.Id, leaseOwner, TrainingLeaseTtl, cancellationToken);

                    if (result is not null)
                    {
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
                experiment.PrimaryFailureReason = ValidationTrainingFailureCodes.InsufficientWarmup;
                experiment.FailureReasonsJson = JsonSerializer.Serialize(
                    new[] { ValidationTrainingFailureCodes.InsufficientWarmup },
                    JsonOptions);
                experiment.CurrentStage = "InsufficientWarmup";
                experiment.UpdatedAtUtc = DateTime.UtcNow;
                AppendDiagnostic(experiment, ValidationTrainingFailureCodes.InsufficientWarmup, experiment.ErrorMessage);
                experiment.WarmupSnapshotJson = JsonSerializer.Serialize(new
                {
                    requiredWarmupCandleCount = ex.RequiredWarmupCandleCount,
                    availableWarmupCandleCount = ex.AvailableWarmupCandleCount,
                    warmupStatus = ex.WarmupStatus.ToString(),
                    requirementsVersion = requirements.RequirementsVersion
                }, JsonOptions);
                await _experiments.UpdateAsync(experiment, cancellationToken);
                await _trainingLease.ReleaseAsync(experiment.Id, leaseOwner, cancellationToken);
                return ServiceResult<ValidationExperimentDto>.Fail(
                    experiment.ErrorMessage,
                    ValidationTrainingFailureCodes.InsufficientWarmup);
            }

            return result ?? ServiceResult<ValidationExperimentDto>.Fail("Training ended without a result.");
        }
        catch (Exception ex) when (ValidationTrainingDbRetry.IsTransient(ex))
        {
            experiment.Status = ValidationExperimentStatus.TrainingInterrupted;
            experiment.ErrorMessage = ex.Message;
            experiment.CurrentStage = "TrainingInterrupted";
            experiment.UpdatedAtUtc = DateTime.UtcNow;
            AppendDiagnostic(experiment, "TrainingInterrupted", ex.Message);
            await _experiments.UpdateAsync(experiment, cancellationToken);
            await _trainingLease.ReleaseAsync(experiment.Id, leaseOwner, cancellationToken);
            return ServiceResult<ValidationExperimentDto>.Fail(ex.Message);
        }
        catch (Exception ex)
        {
            experiment.Status = ValidationExperimentStatus.Failed;
            experiment.ErrorMessage = ex.Message;
            experiment.CurrentStage = "Training";
            experiment.UpdatedAtUtc = DateTime.UtcNow;
            AppendDiagnostic(experiment, "TrainingFailed", ex.Message);
            await _experiments.UpdateAsync(experiment, cancellationToken);
            await _trainingLease.ReleaseAsync(experiment.Id, leaseOwner, cancellationToken);
            return ServiceResult<ValidationExperimentDto>.Fail(ex.Message);
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
        var trialEntities = (await _trials.GetByExperimentIdAsync(experiment.Id, cancellationToken)).ToList();
        var useSnapshotSelection =
            ValidationMetricsContract.IsPopulationPathMetricsVersion(experiment.ValidationMetricsVersion);
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
            experiment.CurrentStage = "FailedNoEligibleTrials";
            experiment.StrategyRobustnessDecision = selection.FailureCode;
            experiment.PrimaryFailureReason = selection.FailureCode?.ToString();
            experiment.FailureReasonsJson = JsonSerializer.Serialize(new[] { selection.FailureCode?.ToString() }, JsonOptions);
            experiment.DecisionExplanation = selection.FailureMessage;
            experiment.ErrorMessage = selection.FailureMessage;
            experiment.PercentComplete = 100m;
            experiment.DecidedAtUtc = DateTime.UtcNow;
            experiment.IsQualificationCapable = false;
            AppendDiagnostic(experiment, selection.FailureCode?.ToString() ?? "FailedNoEligibleTrials", selection.FailureMessage ?? string.Empty);
            experiment.UpdatedAtUtc = DateTime.UtcNow;
            await _experiments.UpdateAsync(experiment, cancellationToken);
            await _trainingLease.ReleaseAsync(experiment.Id, leaseOwner, cancellationToken);
            return ServiceResult<ValidationExperimentDto>.Ok(MapDto(experiment));
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
            experiment.IsQualificationCapable = selection.IntegrityStatus != ValidationSelectionIntegrityStatus.InfrastructureOnlyFallback;

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
            await FinalizeLeakageFromPersistedEvidenceAsync(experiment, draft, cancellationToken);
        }
        else
        {
            experiment.LeakageAuditStatus = ValidationLeakageAuditStatus.NotAvailable;
        }

        experiment.Status = ValidationExperimentStatus.TrainingCompleted;
        experiment.CurrentStage = "TrainingCompleted";
        experiment.PercentComplete = 75m;
        experiment.UpdatedAtUtc = DateTime.UtcNow;
        await _experiments.UpdateAsync(experiment, cancellationToken);
        await _trainingLease.ReleaseAsync(experiment.Id, leaseOwner, cancellationToken);
        return ServiceResult<ValidationExperimentDto>.Ok(MapDto(experiment));
    }

    private async Task FinalizeLeakageFromPersistedEvidenceAsync(
        ValidationExperiment experiment,
        DraftConfiguration draft,
        CancellationToken cancellationToken)
    {
        var optimizerFp = _parameterFingerprint.ComputeFingerprint(draft.Parameters);
        var audits = await _candleAccessAudits.GetByExperimentIdAsync(experiment.Id, cancellationToken);
        var leakage = _leakageAuditor.EvaluateFromAccessEvidence(
            audits,
            experiment.ValidationStartUtc!.Value,
            experiment.TrainingStartUtc!.Value,
            experiment.TrainingEndUtc!.Value,
            optimizerFp);
        experiment.LeakageAuditJson = _leakageAuditor.Serialize(leakage);
        experiment.LeakageAuditStatus = leakage.Status;
        if (leakage.Status == ValidationLeakageAuditStatus.Failed)
        {
            AppendDiagnostic(experiment, "ValidationDataLeakageDetected", leakage.Reason ?? "Leakage audit failed.");
        }
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
            if (existing is not null)
            {
                EnsureKnownAuditContractVersion(existing);

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

    private async Task FinalizeTrialAuditWithVerifierAsync(
        ValidationExperiment experiment,
        ValidationParameterTrial trial,
        IReadOnlyDictionary<string, string> combo,
        string fingerprint,
        ValidationAuditExecution auditExecution,
        CancellationToken cancellationToken)
    {
        var finalExpected = auditExecution.LastConfirmedSequence;
        if (finalExpected <= 0)
        {
            trial.Status = ValidationTrialStatus.AuditPersistenceFailed;
            trial.ErrorMessage = "Finalization-only recovery requires a positive confirmed sequence.";
            trial.AuditCompletionStatus = ValidationAuditCompletionStatus.RecoveryRequired;
            await _trials.UpdateAsync(trial, cancellationToken);
            return;
        }

        var completion = await _auditFinalizer.CompleteAsync(
            auditExecution.AuditExecutionId,
            finalExpected,
            cancellationToken);

        trial = await _trials.GetByExperimentAndFingerprintAsync(
            experiment.Id, fingerprint, cancellationToken) ?? trial;
        auditExecution = await _auditExecutions.GetByAuditExecutionIdAsync(
            auditExecution.AuditExecutionId, cancellationToken) ?? auditExecution;

        if (!completion.IsComplete)
        {
            trial.Status = ValidationTrialStatus.AuditPersistenceFailed;
            trial.ErrorMessage =
                $"Audit finalization failed: {completion.FailureCode ?? completion.CompletionCode.ToString()}.";
            trial.AuditCompletionStatus = ValidationAuditCompletionStatus.RecoveryRequired;
            await _trials.UpdateAsync(trial, cancellationToken);
            return;
        }

        var batches = await _auditBatches.GetByAuditExecutionIdAsync(
            auditExecution.AuditExecutionId, cancellationToken);
        var accessRows = (await _candleAccessAudits.GetByExperimentIdAsync(experiment.Id, cancellationToken))
            .Where(r => r.ScopeExecutionId == auditExecution.ScopeExecutionId)
            .ToList();
        var completeness = _auditCompletenessVerifier.Verify(trial, auditExecution, batches, accessRows);

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
            trial.Status = ValidationTrialStatus.AuditPersistenceFailed;
            trial.ErrorMessage = $"Audit completeness verification failed: {completeness.CompletionCode}.";
            trial.AuditCompletionStatus = ValidationAuditCompletionStatus.RecoveryRequired;
        }

        await _trials.UpdateAsync(trial, cancellationToken);
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
