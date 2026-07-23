using System.Text.Json;
using MomoQuant.Application.Common;
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
            ServiceResult<ValidationExperimentDto>? result = null;
            await _trainingScopeExecution.ExecuteWithScopeAsync(experiment, async trainingScope =>
            {
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
                        // Production owns leakage/boundary status transitions (flush, trial, experiment, op-status).
                        var optimizerFp = _parameterFingerprint.ComputeFingerprint(draft.Parameters);
                        var handled = await _trainingFailureHandler.HandleBoundaryFailureAsync(
                            experiment,
                            trial,
                            trainingScope,
                            ex,
                            optimizerInputFingerprint: optimizerFp,
                            leaseOwner: leaseOwner,
                            cancellationToken: cancellationToken);
                        await _trainingLease.ReleaseAsync(experiment.Id, leaseOwner, cancellationToken);
                        result = ServiceResult<ValidationExperimentDto>.Fail(
                            handled.UserSafeErrorMessage,
                            handled.ErrorCode);
                        return;
                    }
                    catch (Exception ex) when (ValidationTrainingDbRetry.IsTransient(ex))
                    {
                        await PersistTrainingCandleAccessLogAsync(trainingScope, cancellationToken);
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
                        return;
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
                }

                result = await FinalizeTrainingAsync(experiment, draft, combos.Count, cancellationToken, leaseOwner);
            }, cancellationToken);

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

    private async Task PopulateTrialMetricsAsync(
        ValidationExperiment experiment,
        ValidationParameterTrial trial,
        IReadOnlyDictionary<string, string> combo,
        StrategyLabRun run,
        ValidationQualificationProfile profile,
        CancellationToken cancellationToken)
    {
        var candidates = await _candidates.GetByRunIdAsync(run.Id, cancellationToken);
        var boundary = experiment.ValidationStartUtc.HasValue
            ? ValidationMetricsMapper.CountBoundaryCensored(candidates, experiment.ValidationStartUtc.Value)
            : 0;
        var metricsCandidates = experiment.ValidationStartUtc.HasValue
            ? ValidationMetricsMapper.ExcludeBoundaryFromMetrics(candidates, experiment.ValidationStartUtc.Value)
            : candidates;

        var (summary, riskOnly, fullPipeline) = ParseResultSummary(run.ResultSummaryJson);
        var rawMetrics = ValidationMetricsMapper.FromCandidates(
            metricsCandidates,
            experiment.TrainingCandleCount,
            boundary,
            ValidationLayerType.RawStrategy);
        if (summary is not null)
        {
            rawMetrics = ValidationMetricsMapper.FromStrategyLabSummary(
                summary,
                experiment.TrainingCandleCount,
                metricsCandidates.Count,
                boundary,
                riskOnly,
                fullPipeline,
                ValidationLayerType.RawStrategy);
        }

        var feeImpact = rawMetrics.FeeToGrossProfitPercent;
        var oppRate = rawMetrics.OpportunityRatePer1000Candles;
        var score = ValidationTrainingScoreCalculator.Calculate(
            rawMetrics.ClosedTradeCount,
            rawMetrics.NetExpectancyR,
            rawMetrics.ProfitFactor,
            rawMetrics.MaximumRealizedDrawdownPercent,
            feeImpact,
            oppRate,
            profile.MinimumTrainingClosedTrades);

        var guardrailFailures = new List<string>();
        if (rawMetrics.ClosedTradeCount < profile.MinimumTrainingClosedTrades)
            guardrailFailures.Add($"ClosedTrades<{profile.MinimumTrainingClosedTrades}");
        if ((rawMetrics.ProfitFactor ?? 0m) < profile.MinimumTrainingProfitFactor)
            guardrailFailures.Add($"ProfitFactor<{profile.MinimumTrainingProfitFactor}");
        if ((rawMetrics.NetExpectancyR ?? 0m) < profile.MinimumTrainingNetExpectancyR)
            guardrailFailures.Add($"NetExpectancyR<{profile.MinimumTrainingNetExpectancyR}");
        if ((rawMetrics.MaximumRealizedDrawdownPercent ?? 0m) > profile.MaximumTrainingDrawdownPercent)
            guardrailFailures.Add($"MaxDD>{profile.MaximumTrainingDrawdownPercent}");

        var passed = guardrailFailures.Count == 0;
        trial.ParameterSnapshotJson = JsonSerializer.Serialize(combo, JsonOptions);
        trial.Status = passed ? ValidationTrialStatus.Completed : ValidationTrialStatus.GuardrailRejected;
        trial.CompletedAtUtc = DateTime.UtcNow;
        trial.RawCandidateCount = metricsCandidates.Count;
        trial.ClosedTradeCount = rawMetrics.ClosedTradeCount;
        trial.WinnerCount = rawMetrics.WinnerCount;
        trial.LoserCount = rawMetrics.LoserCount;
        trial.ExpiredCount = rawMetrics.ExpiredCount;
        trial.NetExpectancyR = rawMetrics.NetExpectancyR;
        trial.GrossPnl = rawMetrics.GrossPnl;
        trial.NetPnl = rawMetrics.NetPnl;
        trial.ProfitFactor = rawMetrics.ProfitFactor;
        trial.MaximumDrawdownPercent = rawMetrics.MaximumRealizedDrawdownPercent;
        trial.FeeImpactPercent = feeImpact;
        trial.TrainingScore = score.Total;
        trial.GuardrailDecision = passed ? "Passed" : "Failed";
        trial.GuardrailFailureReasonsJson = guardrailFailures.Count == 0
            ? null
            : JsonSerializer.Serialize(guardrailFailures, JsonOptions);
        trial.StrategyLabRunId = run.Id;
        trial.ErrorMessage = null;
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
        ValidationTrialRanker.AssignRanks(trialEntities);
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
            CreatedAtUtc = DateTime.UtcNow
        };

        await _labRuns.AddAsync(run, cancellationToken);
        return run;
    }
}
