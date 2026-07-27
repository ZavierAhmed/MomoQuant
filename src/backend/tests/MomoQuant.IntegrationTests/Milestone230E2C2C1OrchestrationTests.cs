using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Common;
using MomoQuant.Application.ValidationLab;
using MomoQuant.Application.ValidationLab.Dtos;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.ValidationLab;
using MomoQuant.Persistence;

namespace MomoQuant.IntegrationTests;

/// <summary>Milestone 23.0E2C2C1 — transient cleanup and recovery-exception closure proofs.</summary>
[Collection("Integration")]
public sealed class Milestone230E2C2C1OrchestrationTests
{
    [Fact]
    public void TransientSeam_IsRecognizedByValidationTrainingDbRetry()
    {
        Assert.True(ValidationTrainingDbRetry.IsTransient(new E2C2TransientDatabaseException()));
        Assert.False(ValidationTrainingDbRetry.IsTransient(
            new InvalidOperationException("E2C2 simulated experiment persistence failure.")));
    }

    [Fact]
    public async Task LeaseAcquired_InitialTransientPersistenceAndRecoveryPersistenceFail_ReleasesLease()
    {
        await using var factory = new E2C2OrchestrationFactory();
        factory.Controls.RunnerMode = E2C2RunnerMode.AllowedComplete;
        long? experimentId = null;

        try
        {
            var (id, _) = await E2C2ExperimentFactory.CreatePreparedSingleTrialExperimentAsync(
                factory, "e2c2c1-transient-lease");
            experimentId = id;
            factory.Controls.FailExperimentUpdateTransientCount = 1;
            // First recovery persist fails; bounded retry must succeed so the aggregate is durable.
            factory.Controls.FailExperimentUpdateCount = 1;

            await using var scope = factory.Services.CreateAsyncScope();
            var lab = scope.ServiceProvider.GetRequiredService<IValidationLabService>();
            var result = await lab.RunTrainingAsync(id);

            Assert.False(result.Succeeded);
            Assert.DoesNotContain("deadlock", result.ErrorMessage ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("E2C2 simulated", result.ErrorMessage ?? string.Empty, StringComparison.Ordinal);

            await using var assertScope = factory.Services.CreateAsyncScope();
            var experiment = await ReloadExperimentAsync(assertScope, id);
            Assert.False(experiment.IsQualificationCapable);
            Assert.True(ValidationLifecycleGate.CanResumeTraining(experiment.Status));
            Assert.NotEqual(ValidationExperimentStatus.TrainingCompleted, experiment.Status);
            Assert.Equal(ValidationTrainingFailureCodes.TrialExecutionFailed, experiment.PrimaryFailureReason);
            E2C2FailureReasonHelpers.AssertExactFailureRecords(
                experiment,
                (ValidationTrainingFailureCodes.TrialExecutionFailed, ValidationTrainingFailurePhase.TrialBody),
                (ValidationTrainingFailureCodes.TrainingCleanupFailed,
                    ValidationTrainingFailurePhase.ExperimentStatusPersistence));
            Assert.False(await IsLeaseActiveAsync(assertScope, id));
            Assert.False(await CanFreezeOrQualifyAsync(
                assertScope.ServiceProvider.GetRequiredService<IValidationLabService>(), id));
        }
        finally
        {
            if (experimentId is long eid)
            {
                await E2C2ExperimentFactory.CleanupExperimentAsync(factory, eid);
            }
        }
    }

    [Fact]
    public async Task OuterTransientCatch_PersistenceRetryFails_ReturnsSafeFailure()
    {
        await using var factory = new E2C2OrchestrationFactory();
        factory.Controls.RunnerMode = E2C2RunnerMode.AllowedComplete;
        long? experimentId = null;

        try
        {
            var (id, _) = await E2C2ExperimentFactory.CreatePreparedSingleTrialExperimentAsync(
                factory, "e2c2c1-transient-safe");
            experimentId = id;
            factory.Controls.FailExperimentUpdateTransientCount = 1;
            // First recovery persist fails; bounded retry must succeed so the aggregate is durable.
            factory.Controls.FailExperimentUpdateCount = 1;

            await using (var runScope = factory.Services.CreateAsyncScope())
            {
                var lab = runScope.ServiceProvider.GetRequiredService<IValidationLabService>();
                var result = await lab.RunTrainingAsync(id);
                Assert.False(result.Succeeded);
                Assert.Equal(ValidationTrainingFailureCodes.TrialExecutionFailed, result.ErrorField);
                Assert.DoesNotContain("Number=", result.ErrorMessage ?? string.Empty, StringComparison.Ordinal);
                Assert.DoesNotContain("Server=", result.ErrorMessage ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            }

            await using var assertScope = factory.Services.CreateAsyncScope();
            var experiment = await ReloadExperimentAsync(assertScope, id);
            E2C2FailureReasonHelpers.AssertNoSensitiveMessages(experiment);
            Assert.False(await IsLeaseActiveAsync(assertScope, id));
        }
        finally
        {
            if (experimentId is long eid)
            {
                await E2C2ExperimentFactory.CleanupExperimentAsync(factory, eid);
            }
        }
    }

    [Fact]
    public async Task OuterTransientCatch_OriginalPrimarySurvivesPersistenceFailure()
    {
        await using var factory = new E2C2OrchestrationFactory();
        factory.Controls.RunnerMode = E2C2RunnerMode.AllowedComplete;
        long? experimentId = null;

        try
        {
            var (id, _) = await E2C2ExperimentFactory.CreatePreparedSingleTrialExperimentAsync(
                factory, "e2c2c1-primary-survives");
            experimentId = id;
            factory.Controls.FailExperimentUpdateTransientCount = 1;
            // First recovery persist fails; bounded retry must succeed so the aggregate is durable.
            factory.Controls.FailExperimentUpdateCount = 1;

            await using (var runScope = factory.Services.CreateAsyncScope())
            {
                var result = await runScope.ServiceProvider.GetRequiredService<IValidationLabService>()
                    .RunTrainingAsync(id);
                Assert.False(result.Succeeded);
            }

            await using var assertScope = factory.Services.CreateAsyncScope();
            var experiment = await ReloadExperimentAsync(assertScope, id);
            var records = E2C2FailureReasonHelpers.ParseRecords(experiment.FailureReasonsJson);
            Assert.Equal(ValidationTrainingFailureCodes.TrialExecutionFailed, experiment.PrimaryFailureReason);
            Assert.Equal(ValidationTrainingFailureCategory.TrialExecution, records[0].Category);
            Assert.Equal(ValidationTrainingFailurePhase.TrialBody, records[0].Phase);
            Assert.Contains(
                records,
                r => r.Phase == ValidationTrainingFailurePhase.ExperimentStatusPersistence
                     && r.Code == ValidationTrainingFailureCodes.TrainingCleanupFailed);
            Assert.False(experiment.IsQualificationCapable);
            Assert.False(await IsLeaseActiveAsync(assertScope, id));
        }
        finally
        {
            if (experimentId is long eid)
            {
                await E2C2ExperimentFactory.CleanupExperimentAsync(factory, eid);
            }
        }
    }

    [Fact]
    public async Task FinalizationOnlyResume_RecoveryServiceThrows_ReturnsAuditFailureWithoutRunnerReentry()
    {
        await using var factory = new E2C2OrchestrationFactory();
        long? experimentId = null;

        try
        {
            var (id, combo) = await E2C2ExperimentFactory.CreatePreparedSingleTrialExperimentAsync(
                factory, "e2c2c1-recovery-throw");
            experimentId = id;

            await using (var seedScope = factory.Services.CreateAsyncScope())
            {
                await SeedFinalizationOnlyAuditStateAsync(seedScope.ServiceProvider, id, combo, "rec-throw");
            }

            factory.Controls.ThrowOnAuditRecovery = true;

            await using (var resumeScope = factory.Services.CreateAsyncScope())
            {
                var result = await resumeScope.ServiceProvider.GetRequiredService<IValidationLabService>()
                    .ResumeTrainingAsync(id);
                Assert.False(result.Succeeded);
                Assert.DoesNotContain("recovery service", result.ErrorMessage ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            }

            Assert.Equal(0, factory.Controls.RunnerInvocationCount);

            await using var assertScope = factory.Services.CreateAsyncScope();
            await AssertAuditDurabilityOutcomeAsync(
                assertScope,
                id,
                ValidationTrainingFailurePhase.AuditFinalization);
        }
        finally
        {
            if (experimentId is long eid)
            {
                await E2C2ExperimentFactory.CleanupExperimentAsync(factory, eid);
            }
        }
    }

    [Fact]
    public async Task FinalizationOnlyResume_UnknownAuditContract_ReturnsAuditFailureWithoutRunnerReentry()
    {
        await using var factory = new E2C2OrchestrationFactory();
        long? experimentId = null;

        try
        {
            var (id, combo) = await E2C2ExperimentFactory.CreatePreparedSingleTrialExperimentAsync(
                factory, "e2c2c1-unknown-contract");
            experimentId = id;

            await using (var seedScope = factory.Services.CreateAsyncScope())
            {
                var (_, _, execution) = await SeedFinalizationOnlyAuditStateAsync(
                    seedScope.ServiceProvider, id, combo, "unk-contract");
                execution.AuditContractVersion = "unknown-contract/v0";
                await seedScope.ServiceProvider.GetRequiredService<IValidationAuditExecutionRepository>()
                    .UpdateAsync(execution);
            }

            await using (var resumeScope = factory.Services.CreateAsyncScope())
            {
                var result = await resumeScope.ServiceProvider.GetRequiredService<IValidationLabService>()
                    .ResumeTrainingAsync(id);
                Assert.False(result.Succeeded);
                Assert.DoesNotContain("unknown-contract", result.ErrorMessage ?? string.Empty, StringComparison.Ordinal);
            }

            Assert.Equal(0, factory.Controls.RunnerInvocationCount);

            await using var assertScope = factory.Services.CreateAsyncScope();
            var experiment = await ReloadExperimentAsync(assertScope, id);
            Assert.False(experiment.IsQualificationCapable);
            Assert.Equal("VALIDATION_AUDIT_UNKNOWN_CONTRACT_VERSION", experiment.PrimaryFailureReason);
            var records = E2C2FailureReasonHelpers.ParseRecords(experiment.FailureReasonsJson);
            Assert.Equal(ValidationTrainingFailureCategory.AuditDurability, records[0].Category);
            Assert.Equal(ValidationTrainingFailurePhase.AuditFinalization, records[0].Phase);
            Assert.False(await IsLeaseActiveAsync(assertScope, id));
            Assert.False(await CanFreezeOrQualifyAsync(
                assertScope.ServiceProvider.GetRequiredService<IValidationLabService>(), id));
        }
        finally
        {
            if (experimentId is long eid)
            {
                await E2C2ExperimentFactory.CleanupExperimentAsync(factory, eid);
            }
        }
    }

    [Fact]
    public async Task FinalizationOnlyResume_TrialReloadThrows_ReturnsAuditFailureWithoutRunnerReentry()
    {
        await using var factory = new E2C2OrchestrationFactory();
        long? experimentId = null;

        try
        {
            var (id, combo) = await E2C2ExperimentFactory.CreatePreparedSingleTrialExperimentAsync(
                factory, "e2c2c1-trial-reload");
            experimentId = id;

            await using (var seedScope = factory.Services.CreateAsyncScope())
            {
                await SeedFinalizationOnlyAuditStateAsync(seedScope.ServiceProvider, id, combo, "trial-reload");
            }

            factory.Controls.ArmTrialFingerprintGetFailureAfterFinalizer = true;

            await using (var resumeScope = factory.Services.CreateAsyncScope())
            {
                var result = await resumeScope.ServiceProvider.GetRequiredService<IValidationLabService>()
                    .ResumeTrainingAsync(id);
                Assert.False(result.Succeeded);
                Assert.DoesNotContain("fingerprint reload", result.ErrorMessage ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            }

            Assert.Equal(0, factory.Controls.RunnerInvocationCount);

            await using var assertScope = factory.Services.CreateAsyncScope();
            await AssertAuditDurabilityOutcomeAsync(
                assertScope,
                id,
                ValidationTrainingFailurePhase.AuditFinalization);
        }
        finally
        {
            if (experimentId is long eid)
            {
                await E2C2ExperimentFactory.CleanupExperimentAsync(factory, eid);
            }
        }
    }

    [Fact]
    public async Task FinalizationOnlyResume_AuditExecutionReloadThrows_ReturnsAuditFailureWithoutRunnerReentry()
    {
        await using var factory = new E2C2OrchestrationFactory();
        long? experimentId = null;

        try
        {
            var (id, combo) = await E2C2ExperimentFactory.CreatePreparedSingleTrialExperimentAsync(
                factory, "e2c2c1-exec-reload");
            experimentId = id;

            await using (var seedScope = factory.Services.CreateAsyncScope())
            {
                await SeedFinalizationOnlyAuditStateAsync(seedScope.ServiceProvider, id, combo, "exec-reload");
            }

            factory.Controls.ArmAuditExecutionGetFailureAfterFinalizer = true;

            await using (var resumeScope = factory.Services.CreateAsyncScope())
            {
                var result = await resumeScope.ServiceProvider.GetRequiredService<IValidationLabService>()
                    .ResumeTrainingAsync(id);
                Assert.False(result.Succeeded);
                Assert.DoesNotContain("audit execution reload", result.ErrorMessage ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            }

            Assert.Equal(0, factory.Controls.RunnerInvocationCount);

            await using var assertScope = factory.Services.CreateAsyncScope();
            await AssertAuditDurabilityOutcomeAsync(
                assertScope,
                id,
                ValidationTrainingFailurePhase.AuditFinalization);
        }
        finally
        {
            if (experimentId is long eid)
            {
                await E2C2ExperimentFactory.CleanupExperimentAsync(factory, eid);
            }
        }
    }

    [Fact]
    public async Task CompletedAuditRevalidation_RecoveryException_CannotReachSelectionSuccess()
    {
        await using var factory = new E2C2OrchestrationFactory();
        long? experimentId = null;

        try
        {
            var (id, combo) = await E2C2ExperimentFactory.CreatePreparedSingleTrialExperimentAsync(
                factory, "e2c2c1-reval-recovery");
            experimentId = id;

            await using (var seedScope = factory.Services.CreateAsyncScope())
            {
                await SeedCompletedCorruptAuditStateAsync(seedScope.ServiceProvider, id, combo, "reval-rec");
            }

            factory.Controls.ThrowOnAuditRecovery = true;

            await using (var resumeScope = factory.Services.CreateAsyncScope())
            {
                var result = await resumeScope.ServiceProvider.GetRequiredService<IValidationLabService>()
                    .ResumeTrainingAsync(id);
                Assert.False(result.Succeeded);
                Assert.True(result.Data is null
                    || result.Data.Status != ValidationExperimentStatus.TrainingCompleted);
            }

            Assert.Equal(0, factory.Controls.RunnerInvocationCount);

            await using var assertScope = factory.Services.CreateAsyncScope();
            var experiment = await ReloadExperimentAsync(assertScope, id);
            Assert.NotEqual(ValidationExperimentStatus.TrainingCompleted, experiment.Status);
            Assert.False(experiment.IsQualificationCapable);
            Assert.Null(experiment.SelectedTrialId);
            var records = E2C2FailureReasonHelpers.ParseRecords(experiment.FailureReasonsJson);
            Assert.NotEmpty(records);
            Assert.Equal(ValidationTrainingFailureCategory.AuditDurability, records[0].Category);
            Assert.Equal(ValidationTrainingFailurePhase.AuditFinalization, records[0].Phase);
            Assert.False(await IsLeaseActiveAsync(assertScope, id));
            Assert.False(await CanFreezeOrQualifyAsync(
                assertScope.ServiceProvider.GetRequiredService<IValidationLabService>(), id));
        }
        finally
        {
            if (experimentId is long eid)
            {
                await E2C2ExperimentFactory.CleanupExperimentAsync(factory, eid);
            }
        }
    }

    private static async Task AssertAuditDurabilityOutcomeAsync(
        IServiceScope scope,
        long id,
        ValidationTrainingFailurePhase expectedPhase)
    {
        var lab = scope.ServiceProvider.GetRequiredService<IValidationLabService>();
        var experiment = await ReloadExperimentAsync(scope, id);
        Assert.False(experiment.IsQualificationCapable);
        Assert.True(ValidationLifecycleGate.CanResumeTraining(experiment.Status)
                    || experiment.Status == ValidationExperimentStatus.Failed);
        Assert.NotEqual(ValidationExperimentStatus.TrainingCompleted, experiment.Status);
        var records = E2C2FailureReasonHelpers.ParseRecords(experiment.FailureReasonsJson);
        Assert.NotEmpty(records);
        Assert.Equal(ValidationTrainingFailureCategory.AuditDurability, records[0].Category);
        Assert.Equal(expectedPhase, records[0].Phase);
        Assert.Equal(records[0].Code, experiment.PrimaryFailureReason);
        E2C2FailureReasonHelpers.AssertNoSensitiveMessages(experiment);
        Assert.False(await IsLeaseActiveAsync(scope, id));
        Assert.False(await CanFreezeOrQualifyAsync(lab, id));
    }

    private static async Task<(
        ValidationExperiment Experiment,
        ValidationParameterTrial Trial,
        ValidationAuditExecution Execution)> SeedFinalizationOnlyAuditStateAsync(
        IServiceProvider sp,
        long experimentId,
        IReadOnlyDictionary<string, string> combo,
        string suffix)
    {
        var trials = sp.GetRequiredService<IValidationParameterTrialRepository>();
        var experiments = sp.GetRequiredService<IValidationExperimentRepository>();
        var fingerprint = ValidationLabService.ParameterFingerprint(combo);

        await trials.AddAsync(new ValidationParameterTrial
        {
            ValidationExperimentId = experimentId,
            TrialNumber = 1,
            ParameterSnapshotJson = JsonSerializer.Serialize(combo),
            ParameterFingerprint = fingerprint,
            Status = ValidationTrialStatus.Interrupted,
            GuardrailDecision = "Passed",
            StrategyLabRunId = 1,
            StartedAtUtc = DateTime.UtcNow,
            AuditCompletionStatus = ValidationAuditCompletionStatus.InProgress
        });

        var experiment = await experiments.GetByIdAsync(experimentId)
            ?? throw new InvalidOperationException("Experiment missing.");
        var trial = await trials.GetByExperimentAndFingerprintAsync(experimentId, fingerprint)
            ?? throw new InvalidOperationException("Trial missing.");

        var hasher = new ValidationAuditPayloadSetHasher();
        var execution = E2C1AuditFixtures.NewExecution(experiment, trial);
        await sp.GetRequiredService<IValidationAuditExecutionRepository>()
            .CreateAndAssignTrialAuthoritativeAsync(execution, trial);

        var eventId = Guid.NewGuid();
        var access = E2BAuditFixtures.NewAudit(experimentId, eventId, execution.ScopeExecutionId, 1, suffix);
        var canonicalizer = new ValidationAccessPayloadCanonicalizer();
        var hash = canonicalizer.ComputeSha256(access);
        access.AccessPayloadHash = hash;
        access.AccessPayloadContractVersion = ValidationAccessPayloadContractVersions.Current;

        var entries = new[]
        {
            new ValidationAuditPayloadSetEntry(1, eventId, hash, ValidationAccessPayloadContractVersions.Current)
        };
        var setHash = hasher.ComputeSetHash(entries);
        var (ids, hashes) = hasher.BuildManifestJsons(entries);

        var batch = new ValidationAuditBatch
        {
            AuditBatchId = Guid.NewGuid(),
            AuditExecutionId = execution.AuditExecutionId,
            BatchNumber = 1,
            FirstSequence = 1,
            LastSequence = 1,
            ExpectedEventCount = 1,
            ExpectedEventIdsJson = ids,
            ExpectedPayloadHashesJson = hashes,
            ExpectedPayloadSetHash = setHash,
            Status = ValidationAuditBatchStatus.Confirmed,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            ConfirmedAtUtc = DateTime.UtcNow,
            AuditBatchContractVersion = ValidationAuditBatch.ContractVersionV1,
            RowVersion = 1
        };
        await sp.GetRequiredService<IValidationAuditBatchRepository>().AddAsync(batch);
        await sp.GetRequiredService<IValidationCandleAccessAuditRepository>()
            .AddRangeIdempotentByAccessEventIdAsync([access]);

        execution.LastConfirmedSequence = 1;
        execution.ConfirmedEventCount = 1;
        execution.FinalExpectedSequence = null;
        execution.ExpectedEventCount = null;
        execution.FinalPayloadSetHash = null;
        execution.Status = ValidationAuditExecutionStatus.EventsConfirmed;
        execution.RecoveryStatus = ValidationAuditRecoveryStatus.RecoveredFromConfirmedBatch;
        trial.AuditCompletionStatus = ValidationAuditCompletionStatus.InProgress;
        trial.GuardrailDecision = "Passed";
        await sp.GetRequiredService<IValidationAuditExecutionRepository>().UpdateAsync(execution);
        await trials.UpdateAsync(trial);

        experiment.Status = ValidationExperimentStatus.TrainingInterrupted;
        experiment.CurrentStage = "TrainingInterrupted";
        experiment.UpdatedAtUtc = DateTime.UtcNow;
        await experiments.UpdateAsync(experiment);
        return (experiment, trial, execution);
    }

    private static async Task SeedCompletedCorruptAuditStateAsync(
        IServiceProvider sp,
        long experimentId,
        IReadOnlyDictionary<string, string> combo,
        string suffix)
    {
        var db = sp.GetRequiredService<MomoQuantDbContext>();
        var trials = sp.GetRequiredService<IValidationParameterTrialRepository>();
        var experiments = sp.GetRequiredService<IValidationExperimentRepository>();
        var fingerprint = ValidationLabService.ParameterFingerprint(combo);

        await trials.AddAsync(new ValidationParameterTrial
        {
            ValidationExperimentId = experimentId,
            TrialNumber = 1,
            ParameterSnapshotJson = JsonSerializer.Serialize(combo),
            ParameterFingerprint = fingerprint,
            Status = ValidationTrialStatus.Completed,
            GuardrailDecision = "Passed",
            TrialRankEligibility = ValidationTrialRankEligibility.Eligible,
            Rank = 1,
            TrainingScore = 1.25m,
            StrategyLabRunId = 1,
            StartedAtUtc = DateTime.UtcNow.AddHours(-1),
            CompletedAtUtc = DateTime.UtcNow,
            AuditCompletionStatus = ValidationAuditCompletionStatus.Complete
        });

        var experiment = await experiments.GetByIdAsync(experimentId)
            ?? throw new InvalidOperationException("Experiment missing.");
        var trial = await trials.GetByExperimentAndFingerprintAsync(experimentId, fingerprint)
            ?? throw new InvalidOperationException("Trial missing.");

        var hasher = new ValidationAuditPayloadSetHasher();
        var execution = E2C1AuditFixtures.NewExecution(experiment, trial);
        await sp.GetRequiredService<IValidationAuditExecutionRepository>()
            .CreateAndAssignTrialAuthoritativeAsync(execution, trial);

        var eventId = Guid.NewGuid();
        var access = E2BAuditFixtures.NewAudit(experimentId, eventId, execution.ScopeExecutionId, 1, suffix);
        var canonicalizer = new ValidationAccessPayloadCanonicalizer();
        var hash = canonicalizer.ComputeSha256(access);
        access.AccessPayloadHash = hash;
        access.AccessPayloadContractVersion = ValidationAccessPayloadContractVersions.Current;

        var entries = new[]
        {
            new ValidationAuditPayloadSetEntry(1, eventId, hash, ValidationAccessPayloadContractVersions.Current)
        };
        var setHash = hasher.ComputeSetHash(entries);
        var (ids, hashes) = hasher.BuildManifestJsons(entries);

        var batch = new ValidationAuditBatch
        {
            AuditBatchId = Guid.NewGuid(),
            AuditExecutionId = execution.AuditExecutionId,
            BatchNumber = 1,
            FirstSequence = 1,
            LastSequence = 1,
            ExpectedEventCount = 1,
            ExpectedEventIdsJson = ids,
            ExpectedPayloadHashesJson = hashes,
            ExpectedPayloadSetHash = setHash,
            Status = ValidationAuditBatchStatus.Confirmed,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            ConfirmedAtUtc = DateTime.UtcNow,
            AuditBatchContractVersion = ValidationAuditBatch.ContractVersionV1,
            RowVersion = 1
        };
        await sp.GetRequiredService<IValidationAuditBatchRepository>().AddAsync(batch);
        await sp.GetRequiredService<IValidationCandleAccessAuditRepository>()
            .AddRangeIdempotentByAccessEventIdAsync([access]);

        execution.LastConfirmedSequence = 1;
        execution.ConfirmedEventCount = 1;
        execution.FinalExpectedSequence = 1;
        execution.ExpectedEventCount = 1;
        execution.FinalPayloadSetHash = setHash;
        execution.Status = ValidationAuditExecutionStatus.Completed;
        execution.CompletedAtUtc = DateTime.UtcNow;
        trial.Status = ValidationTrialStatus.Completed;
        trial.AuditCompletionStatus = ValidationAuditCompletionStatus.Complete;
        trial.TrialRankEligibility = ValidationTrialRankEligibility.Eligible;
        await sp.GetRequiredService<IValidationAuditExecutionRepository>().UpdateAsync(execution);
        await trials.UpdateAsync(trial);

        await db.ValidationCandleAccessAudits
            .Where(a => a.AccessEventId == eventId)
            .ExecuteDeleteAsync();

        experiment.Status = ValidationExperimentStatus.TrainingInterrupted;
        experiment.CurrentStage = "TrainingInterrupted";
        experiment.SelectedTrialId = trial.Id;
        experiment.SelectedTrialNumber = trial.TrialNumber;
        experiment.UpdatedAtUtc = DateTime.UtcNow;
        await experiments.UpdateAsync(experiment);
    }

    private static async Task<ValidationExperiment> ReloadExperimentAsync(IServiceScope scope, long id)
    {
        var experiment = await scope.ServiceProvider.GetRequiredService<IValidationExperimentRepository>()
            .GetByIdAsync(id);
        Assert.NotNull(experiment);
        return experiment!;
    }

    private static async Task<bool> IsLeaseActiveAsync(IServiceScope scope, long id) =>
        await scope.ServiceProvider.GetRequiredService<IValidationTrainingExecutionLeaseService>()
            .IsActiveAsync(id);

    private static async Task<bool> CanFreezeOrQualifyAsync(IValidationLabService lab, long id)
    {
        var freeze = await lab.FreezeAsync(id);
        if (freeze.Succeeded)
        {
            return true;
        }

        var validation = await lab.RunValidationAsync(id);
        return validation.Succeeded;
    }
}
