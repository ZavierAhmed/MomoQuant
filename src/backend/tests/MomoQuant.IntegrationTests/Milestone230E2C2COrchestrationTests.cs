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

/// <summary>Milestone 23.0E2C2C — failure-persistence and recovery-result closure proofs.</summary>
[Collection("Integration")]
public sealed class Milestone230E2C2COrchestrationTests
{
    [Fact]
    public async Task LeaseAcquired_InitialExperimentPersistenceFails_ReleasesAndReturnsSafeFailure()
    {
        await using var factory = new E2C2OrchestrationFactory();
        factory.Controls.RunnerMode = E2C2RunnerMode.AllowedComplete;
        long? experimentId = null;

        try
        {
            var (id, _) = await E2C2ExperimentFactory.CreatePreparedSingleTrialExperimentAsync(
                factory, "e2c2c-lease-init");
            experimentId = id;
            factory.Controls.FailExperimentUpdateCount = 1;

            await using var scope = factory.Services.CreateAsyncScope();
            var lab = scope.ServiceProvider.GetRequiredService<IValidationLabService>();
            var result = await lab.RunTrainingAsync(id);

            Assert.False(result.Succeeded);
            Assert.Equal(ValidationTrainingFailureCodes.TrainingCleanupFailed, result.ErrorField);
            Assert.DoesNotContain("E2C2 simulated", result.ErrorMessage ?? string.Empty, StringComparison.Ordinal);
            Assert.DoesNotContain("StackTrace", result.ErrorMessage ?? string.Empty, StringComparison.OrdinalIgnoreCase);

            var experiment = await scope.ServiceProvider.GetRequiredService<IValidationExperimentRepository>()
                .GetByIdAsync(id);
            Assert.NotNull(experiment);
            Assert.Equal(ValidationExperimentStatus.Failed, experiment!.Status);
            Assert.True(ValidationLifecycleGate.CanResumeTraining(experiment.Status));
            Assert.False(experiment.IsQualificationCapable);
            Assert.Null(experiment.SelectedTrialId);
            E2C2FailureReasonHelpers.AssertExactFailureRecords(
                experiment,
                (ValidationTrainingFailureCodes.TrainingCleanupFailed,
                    ValidationTrainingFailurePhase.ExperimentStatusPersistence));
            E2C2FailureReasonHelpers.AssertNoSensitiveMessages(experiment, result.ErrorMessage);

            Assert.False(await scope.ServiceProvider
                .GetRequiredService<IValidationTrainingExecutionLeaseService>()
                .IsActiveAsync(id));
            Assert.False(await CanFreezeOrQualifyAsync(lab, id));
            Assert.Equal(0, factory.Controls.RunnerInvocationCount);
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
    public async Task BoundaryFailure_TrialPersistenceFails_PreservesBoundaryPrimary()
    {
        await using var factory = new E2C2OrchestrationFactory();
        factory.Controls.RunnerMode = E2C2RunnerMode.AdversarialBoundary;
        factory.Controls.FailTrialUpdateForStatuses.Add(ValidationTrialStatus.LeakageFailed);
        long? experimentId = null;

        try
        {
            var (id, _) = await E2C2ExperimentFactory.CreatePreparedSingleTrialExperimentAsync(
                factory, "e2c2c-b-trial");
            experimentId = id;

            await using var scope = factory.Services.CreateAsyncScope();
            var lab = scope.ServiceProvider.GetRequiredService<IValidationLabService>();
            var result = await lab.RunTrainingAsync(id);

            Assert.False(result.Succeeded);
            Assert.Equal(ValidationTrainingFailureCodes.ValidationDataLeakage, result.ErrorField);

            var experiment = await ReloadExperimentAsync(scope, id);
            var trial = (await scope.ServiceProvider.GetRequiredService<IValidationParameterTrialRepository>()
                .GetByExperimentIdAsync(id)).Single();

            await AssertCommonFailureGates(experiment, result, lab, id);
            E2C2FailureReasonHelpers.AssertExactFailureRecords(
                experiment,
                (ValidationTrainingFailureCodes.ValidationDataLeakage, ValidationTrainingFailurePhase.TrialBody),
                (ValidationTrainingFailureCodes.TrainingCleanupFailed,
                    ValidationTrainingFailurePhase.TrialStatusPersistence));
            Assert.Equal(ValidationTrialStatus.LeakageFailed, trial.Status);
            Assert.Equal(ValidationTrialRankEligibility.Ineligible, trial.TrialRankEligibility);
            Assert.DoesNotContain("E2C2 simulated", result.ErrorMessage ?? string.Empty, StringComparison.Ordinal);
            Assert.False(await IsLeaseActiveAsync(scope, id));
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
    public async Task BoundaryFailure_ExperimentPersistenceFails_PreservesBoundaryPrimary()
    {
        await using var factory = new E2C2OrchestrationFactory();
        factory.Controls.RunnerMode = E2C2RunnerMode.AdversarialBoundary;
        factory.Controls.FailExperimentUpdateForStages.Add("LeakageDetected");
        long? experimentId = null;

        try
        {
            var (id, _) = await E2C2ExperimentFactory.CreatePreparedSingleTrialExperimentAsync(
                factory, "e2c2c-b-exp");
            experimentId = id;

            await using var scope = factory.Services.CreateAsyncScope();
            var lab = scope.ServiceProvider.GetRequiredService<IValidationLabService>();
            var result = await lab.RunTrainingAsync(id);

            Assert.False(result.Succeeded);
            Assert.Equal(ValidationTrainingFailureCodes.ValidationDataLeakage, result.ErrorField);

            var experiment = await ReloadExperimentAsync(scope, id);
            await AssertCommonFailureGates(experiment, result, lab, id);
            E2C2FailureReasonHelpers.AssertExactFailureRecords(
                experiment,
                (ValidationTrainingFailureCodes.ValidationDataLeakage, ValidationTrainingFailurePhase.TrialBody),
                (ValidationTrainingFailureCodes.TrainingCleanupFailed,
                    ValidationTrainingFailurePhase.ExperimentStatusPersistence));
            Assert.DoesNotContain("E2C2 simulated", result.ErrorMessage ?? string.Empty, StringComparison.Ordinal);
            Assert.False(await IsLeaseActiveAsync(scope, id));
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
    public async Task AuditFailure_TrialPersistenceFails_PreservesAuditPrimary()
    {
        await using var factory = new E2C2OrchestrationFactory();
        factory.Controls.RunnerMode = E2C2RunnerMode.AllowedComplete;
        factory.Controls.ThrowOnCompletenessVerifier = true;
        factory.Controls.FailTrialUpdateForStatuses.Add(ValidationTrialStatus.AuditPersistenceFailed);
        long? experimentId = null;

        try
        {
            var (id, _) = await E2C2ExperimentFactory.CreatePreparedSingleTrialExperimentAsync(
                factory, "e2c2c-a-trial");
            experimentId = id;

            await using var scope = factory.Services.CreateAsyncScope();
            var lab = scope.ServiceProvider.GetRequiredService<IValidationLabService>();
            var result = await lab.RunTrainingAsync(id);

            Assert.False(result.Succeeded);
            Assert.Equal(
                ValidationTrainingFailureCodes.ValidationAccessAuditPersistenceFailed,
                result.ErrorField);

            var experiment = await ReloadExperimentAsync(scope, id);
            var trial = (await scope.ServiceProvider.GetRequiredService<IValidationParameterTrialRepository>()
                .GetByExperimentIdAsync(id)).Single();

            await AssertCommonFailureGates(experiment, result, lab, id);
            E2C2FailureReasonHelpers.AssertExactFailureRecords(
                experiment,
                (ValidationTrainingFailureCodes.ValidationAccessAuditPersistenceFailed,
                    ValidationTrainingFailurePhase.CompletenessVerification),
                (ValidationTrainingFailureCodes.TrainingCleanupFailed,
                    ValidationTrainingFailurePhase.TrialStatusPersistence));
            Assert.Equal(ValidationTrialStatus.AuditPersistenceFailed, trial.Status);
            Assert.DoesNotContain("boom", result.ErrorMessage ?? string.Empty, StringComparison.Ordinal);
            Assert.False(await IsLeaseActiveAsync(scope, id));
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
    public async Task AuditFailure_ExperimentPersistenceFails_PreservesAuditPrimary()
    {
        await using var factory = new E2C2OrchestrationFactory();
        factory.Controls.RunnerMode = E2C2RunnerMode.AllowedComplete;
        factory.Controls.ThrowOnCompletenessVerifier = true;
        factory.Controls.FailExperimentUpdateForStages.Add("AuditPersistenceFailed");
        long? experimentId = null;

        try
        {
            var (id, _) = await E2C2ExperimentFactory.CreatePreparedSingleTrialExperimentAsync(
                factory, "e2c2c-a-exp");
            experimentId = id;

            await using var scope = factory.Services.CreateAsyncScope();
            var lab = scope.ServiceProvider.GetRequiredService<IValidationLabService>();
            var result = await lab.RunTrainingAsync(id);

            Assert.False(result.Succeeded);
            Assert.Equal(
                ValidationTrainingFailureCodes.ValidationAccessAuditPersistenceFailed,
                result.ErrorField);

            var experiment = await ReloadExperimentAsync(scope, id);
            await AssertCommonFailureGates(experiment, result, lab, id);
            E2C2FailureReasonHelpers.AssertExactFailureRecords(
                experiment,
                (ValidationTrainingFailureCodes.ValidationAccessAuditPersistenceFailed,
                    ValidationTrainingFailurePhase.CompletenessVerification),
                (ValidationTrainingFailureCodes.TrainingCleanupFailed,
                    ValidationTrainingFailurePhase.ExperimentStatusPersistence));
            Assert.DoesNotContain("boom", result.ErrorMessage ?? string.Empty, StringComparison.Ordinal);
            Assert.False(await IsLeaseActiveAsync(scope, id));
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
    public async Task OperationStatusFailure_SecondaryPersistenceFails_PreservesOriginalPrimary()
    {
        await using var factory = new E2C2OrchestrationFactory();
        factory.Controls.RunnerMode = E2C2RunnerMode.AdversarialBoundary;
        factory.Controls.FailOperationStatusSync = true;
        factory.Controls.FailExperimentUpdateWhenCleanupReasonPresent = true;
        long? experimentId = null;

        try
        {
            var (id, _) = await E2C2ExperimentFactory.CreatePreparedSingleTrialExperimentAsync(
                factory, "e2c2c-op-secondary");
            experimentId = id;

            await using var scope = factory.Services.CreateAsyncScope();
            var lab = scope.ServiceProvider.GetRequiredService<IValidationLabService>();
            var result = await lab.RunTrainingAsync(id);

            Assert.False(result.Succeeded);
            Assert.Equal(ValidationTrainingFailureCodes.ValidationDataLeakage, result.ErrorField);

            var experiment = await ReloadExperimentAsync(scope, id);
            await AssertCommonFailureGates(experiment, result, lab, id);
            E2C2FailureReasonHelpers.AssertExactFailureRecords(
                experiment,
                (ValidationTrainingFailureCodes.ValidationDataLeakage, ValidationTrainingFailurePhase.TrialBody),
                (ValidationTrainingFailureCodes.TrainingCleanupFailed,
                    ValidationTrainingFailurePhase.OperationStatusSync),
                (ValidationTrainingFailureCodes.TrainingCleanupFailed,
                    ValidationTrainingFailurePhase.ExperimentStatusPersistence));
            Assert.False(await IsLeaseActiveAsync(scope, id));
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
    public async Task FinalizationOnlyResume_IncompleteFinalization_ReturnsFailureWithoutRunnerReentry()
    {
        await using var factory = new E2C2OrchestrationFactory();
        factory.Controls.RunnerMode = E2C2RunnerMode.AllowedComplete;
        long? experimentId = null;

        try
        {
            var (id, combo) = await E2C2ExperimentFactory.CreatePreparedSingleTrialExperimentAsync(
                factory, "e2c2c-fin-only-fin");
            experimentId = id;

            await using (var seedScope = factory.Services.CreateAsyncScope())
            {
                await SeedFinalizationOnlyAuditStateAsync(seedScope.ServiceProvider, id, combo, "fin-only-fin");
            }

            factory.Controls.FailAuditFinalizationIncomplete = true;

            await using (var resumeScope = factory.Services.CreateAsyncScope())
            {
                var lab = resumeScope.ServiceProvider.GetRequiredService<IValidationLabService>();
                var result = await lab.ResumeTrainingAsync(id);
                Assert.False(result.Succeeded);
                Assert.False(string.IsNullOrWhiteSpace(result.ErrorField));
                Assert.DoesNotContain("StackTrace", result.ErrorMessage ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            }

            Assert.Equal(0, factory.Controls.RunnerInvocationCount);

            await using (var assertScope = factory.Services.CreateAsyncScope())
            {
                var lab = assertScope.ServiceProvider.GetRequiredService<IValidationLabService>();
                var experiment = await ReloadExperimentAsync(assertScope, id);
                await AssertCommonFailureGates(experiment, null, lab, id);
                var records = E2C2FailureReasonHelpers.ParseRecords(experiment.FailureReasonsJson);
                Assert.NotEmpty(records);
                Assert.Equal(ValidationTrainingFailureCategory.AuditDurability, records[0].Category);
                Assert.Equal(ValidationTrainingFailurePhase.AuditFinalization, records[0].Phase);
                Assert.Equal(records[0].Code, experiment.PrimaryFailureReason);
                Assert.False(await IsLeaseActiveAsync(assertScope, id));
            }
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
    public async Task FinalizationOnlyResume_IncompleteVerification_ReturnsFailureWithoutRunnerReentry()
    {
        await using var factory = new E2C2OrchestrationFactory();
        factory.Controls.RunnerMode = E2C2RunnerMode.AllowedComplete;
        long? experimentId = null;

        try
        {
            var (id, combo) = await E2C2ExperimentFactory.CreatePreparedSingleTrialExperimentAsync(
                factory, "e2c2c-fin-only-ver");
            experimentId = id;

            await using (var seedScope = factory.Services.CreateAsyncScope())
            {
                await SeedFinalizationOnlyAuditStateAsync(seedScope.ServiceProvider, id, combo, "fin-only-ver");
            }

            factory.Controls.FailCompletenessVerification = true;

            await using (var resumeScope = factory.Services.CreateAsyncScope())
            {
                var lab = resumeScope.ServiceProvider.GetRequiredService<IValidationLabService>();
                var result = await lab.ResumeTrainingAsync(id);
                Assert.False(result.Succeeded);
                Assert.False(string.IsNullOrWhiteSpace(result.ErrorField));
            }

            Assert.Equal(0, factory.Controls.RunnerInvocationCount);

            await using (var assertScope = factory.Services.CreateAsyncScope())
            {
                var lab = assertScope.ServiceProvider.GetRequiredService<IValidationLabService>();
                var experiment = await ReloadExperimentAsync(assertScope, id);
                await AssertCommonFailureGates(experiment, null, lab, id);
                var records = E2C2FailureReasonHelpers.ParseRecords(experiment.FailureReasonsJson);
                Assert.NotEmpty(records);
                Assert.Equal(ValidationTrainingFailureCategory.AuditDurability, records[0].Category);
                Assert.Equal(ValidationTrainingFailurePhase.CompletenessVerification, records[0].Phase);
                Assert.Equal(
                    ValidationAuditCompletenessCode.SequenceGap.ToString(),
                    records[0].Code);
                Assert.False(await IsLeaseActiveAsync(assertScope, id));
            }
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
    public async Task CompletedAuditRevalidationFailure_ReturnsFailureWithoutRunnerReentry()
    {
        await using var factory = new E2C2OrchestrationFactory();
        factory.Controls.RunnerMode = E2C2RunnerMode.AllowedComplete;
        long? experimentId = null;

        try
        {
            var (id, combo) = await E2C2ExperimentFactory.CreatePreparedSingleTrialExperimentAsync(
                factory, "e2c2c-reval");
            experimentId = id;

            await using (var seedScope = factory.Services.CreateAsyncScope())
            {
                await SeedCompletedCorruptAuditStateAsync(seedScope.ServiceProvider, id, combo, "reval");
            }

            await using (var resumeScope = factory.Services.CreateAsyncScope())
            {
                var lab = resumeScope.ServiceProvider.GetRequiredService<IValidationLabService>();
                var result = await lab.ResumeTrainingAsync(id);
                Assert.False(result.Succeeded);
                Assert.False(string.IsNullOrWhiteSpace(result.ErrorField));
                Assert.DoesNotContain("StackTrace", result.ErrorMessage ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            }

            Assert.Equal(0, factory.Controls.RunnerInvocationCount);

            await using (var assertScope = factory.Services.CreateAsyncScope())
            {
                var lab = assertScope.ServiceProvider.GetRequiredService<IValidationLabService>();
                var experiment = await ReloadExperimentAsync(assertScope, id);
                await AssertCommonFailureGates(experiment, null, lab, id);
                var records = E2C2FailureReasonHelpers.ParseRecords(experiment.FailureReasonsJson);
                Assert.NotEmpty(records);
                Assert.Equal(ValidationTrainingFailureCategory.AuditDurability, records[0].Category);
                Assert.Equal(ValidationTrainingFailurePhase.CompletenessVerification, records[0].Phase);
                Assert.Equal(ValidationAuditCompletenessCode.EventMissing.ToString(), records[0].Code);
                Assert.Null(experiment.SelectedTrialId);
                Assert.False(await IsLeaseActiveAsync(assertScope, id));
            }
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
    public async Task GenericVerifierException_WithUnrelatedMessage_IsCompletenessVerification()
    {
        await using var factory = new E2C2OrchestrationFactory();
        factory.Controls.RunnerMode = E2C2RunnerMode.AllowedComplete;
        factory.Controls.ThrowOnCompletenessVerifier = true;
        long? experimentId = null;

        try
        {
            var (id, _) = await E2C2ExperimentFactory.CreatePreparedSingleTrialExperimentAsync(
                factory, "e2c2c-ver-boom");
            experimentId = id;

            await using var scope = factory.Services.CreateAsyncScope();
            var lab = scope.ServiceProvider.GetRequiredService<IValidationLabService>();
            var result = await lab.RunTrainingAsync(id);

            Assert.False(result.Succeeded);
            Assert.DoesNotContain("boom", result.ErrorMessage ?? string.Empty, StringComparison.Ordinal);
            var experiment = await ReloadExperimentAsync(scope, id);
            await AssertCommonFailureGates(experiment, result, lab, id);
            E2C2FailureReasonHelpers.AssertExactFailureRecords(
                experiment,
                (ValidationTrainingFailureCodes.ValidationAccessAuditPersistenceFailed,
                    ValidationTrainingFailurePhase.CompletenessVerification));
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
    public async Task GenericFinalizerException_WithUnrelatedMessage_IsAuditFinalization()
    {
        await using var factory = new E2C2OrchestrationFactory();
        factory.Controls.RunnerMode = E2C2RunnerMode.AllowedComplete;
        factory.Controls.ThrowOnAuditFinalizer = true;
        long? experimentId = null;

        try
        {
            var (id, _) = await E2C2ExperimentFactory.CreatePreparedSingleTrialExperimentAsync(
                factory, "e2c2c-fin-db");
            experimentId = id;

            await using var scope = factory.Services.CreateAsyncScope();
            var lab = scope.ServiceProvider.GetRequiredService<IValidationLabService>();
            var result = await lab.RunTrainingAsync(id);

            Assert.False(result.Succeeded);
            Assert.DoesNotContain("database unavailable", result.ErrorMessage ?? string.Empty, StringComparison.Ordinal);
            var experiment = await ReloadExperimentAsync(scope, id);
            await AssertCommonFailureGates(experiment, result, lab, id);
            E2C2FailureReasonHelpers.AssertExactFailureRecords(
                experiment,
                (ValidationTrainingFailureCodes.ValidationAccessAuditPersistenceFailed,
                    ValidationTrainingFailurePhase.AuditFinalization));
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
    public async Task RevalidationFailure_PreservesPreexistingRankIneligibleReasonThroughProductionPath()
    {
        await using var factory = new E2C2OrchestrationFactory();
        factory.Controls.RunnerMode = E2C2RunnerMode.AllowedComplete;
        long? experimentId = null;

        try
        {
            var (id, combo) = await E2C2ExperimentFactory.CreatePreparedSingleTrialExperimentAsync(
                factory, "e2c2c-rank-preserve");
            experimentId = id;

            await using (var seedScope = factory.Services.CreateAsyncScope())
            {
                await SeedCompletedCorruptAuditStateAsync(seedScope.ServiceProvider, id, combo, "rank-preserve");
                var trials = seedScope.ServiceProvider.GetRequiredService<IValidationParameterTrialRepository>();
                var trial = (await trials.GetByExperimentIdAsync(id)).Single();
                trial.RankIneligibleReasonsJson = JsonSerializer.Serialize(new[] { "PriorSeededReason" });
                await trials.UpdateAsync(trial);
            }

            await using (var resumeScope = factory.Services.CreateAsyncScope())
            {
                var lab = resumeScope.ServiceProvider.GetRequiredService<IValidationLabService>();
                var result = await lab.ResumeTrainingAsync(id);
                Assert.False(result.Succeeded);
            }

            Assert.Equal(0, factory.Controls.RunnerInvocationCount);

            await using (var assertScope = factory.Services.CreateAsyncScope())
            {
                var lab = assertScope.ServiceProvider.GetRequiredService<IValidationLabService>();
                var experiment = await ReloadExperimentAsync(assertScope, id);
                var trial = (await assertScope.ServiceProvider
                    .GetRequiredService<IValidationParameterTrialRepository>()
                    .GetByExperimentIdAsync(id)).Single();
                await AssertCommonFailureGates(experiment, null, lab, id);
                Assert.Contains("PriorSeededReason", trial.RankIneligibleReasonsJson!, StringComparison.Ordinal);
                Assert.Contains(
                    ValidationAuditCompletenessCode.EventMissing.ToString(),
                    trial.RankIneligibleReasonsJson!,
                    StringComparison.Ordinal);
            }
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
    public async Task CleanupPersistenceFailure_UsesInjectedRepositoryFailureAndNeverReturnsSuccess()
    {
        await using var factory = new E2C2OrchestrationFactory();
        factory.Controls.RunnerMode = E2C2RunnerMode.AllowedComplete;
        factory.Controls.FailLeaseRelease = true;
        factory.Controls.FailExperimentUpdateWhenCleanupReasonPresent = true;
        long? experimentId = null;

        try
        {
            var (id, _) = await E2C2ExperimentFactory.CreatePreparedSingleTrialExperimentAsync(
                factory, "e2c2c-cleanup-persist");
            experimentId = id;

            await using var scope = factory.Services.CreateAsyncScope();
            var lab = scope.ServiceProvider.GetRequiredService<IValidationLabService>();
            var result = await lab.RunTrainingAsync(id);

            Assert.False(result.Succeeded);
            Assert.Equal(ValidationTrainingFailureCodes.TrainingCleanupFailed, result.ErrorField);
            Assert.DoesNotContain("E2C2 simulated", result.ErrorMessage ?? string.Empty, StringComparison.Ordinal);

            var experiment = await ReloadExperimentAsync(scope, id);
            Assert.False(experiment.IsQualificationCapable);
            Assert.True(ValidationLifecycleGate.CanResumeTraining(experiment.Status));
            Assert.NotEqual(ValidationExperimentStatus.TrainingCompleted, experiment.Status);
            var phases = E2C2FailureReasonHelpers.ParseRecords(experiment.FailureReasonsJson)
                .Select(r => r.Phase)
                .ToArray();
            Assert.Contains(ValidationTrainingFailurePhase.LeaseRelease, phases);
            Assert.Contains(ValidationTrainingFailurePhase.ExperimentStatusPersistence, phases);
            Assert.Equal(ValidationTrainingFailureCodes.TrainingCleanupFailed, experiment.PrimaryFailureReason);
            Assert.False(await CanFreezeOrQualifyAsync(lab, id));
        }
        finally
        {
            if (experimentId is long eid)
            {
                await E2C2ExperimentFactory.CleanupExperimentAsync(factory, eid);
            }
        }
    }

    private static async Task SeedFinalizationOnlyAuditStateAsync(
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
        _ = db;
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

    private static async Task AssertCommonFailureGates(
        ValidationExperiment experiment,
        ServiceResult<ValidationExperimentDto>? result,
        IValidationLabService lab,
        long id)
    {
        Assert.False(experiment.IsQualificationCapable);
        Assert.True(ValidationLifecycleGate.CanResumeTraining(experiment.Status)
                    || experiment.Status == ValidationExperimentStatus.Failed);
        Assert.NotEqual(ValidationExperimentStatus.TrainingCompleted, experiment.Status);
        if (result is not null)
        {
            Assert.False(result.Succeeded);
            E2C2FailureReasonHelpers.AssertNoSensitiveMessages(experiment, result.ErrorMessage);
        }
        else
        {
            E2C2FailureReasonHelpers.AssertNoSensitiveMessages(experiment);
        }

        Assert.False(await CanFreezeOrQualifyAsync(lab, id));
    }

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
