using Microsoft.Extensions.DependencyInjection;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.ValidationLab;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.ValidationLab;

namespace MomoQuant.IntegrationTests;

/// <summary>Milestone 23.0E2C2B — cleanup outcomes, disposal, and finalization aggregate proofs.</summary>
public sealed class Milestone230E2C2BOrchestrationTests
{
    [Fact]
    public async Task CleanupOnly_LeaseReleaseFailure_ReturnsFailureAndIsRecoverable()
    {
        await using var factory = new E2C2OrchestrationFactory();
        factory.Controls.RunnerMode = E2C2RunnerMode.AllowedComplete;
        factory.Controls.FailLeaseRelease = true;
        long? experimentId = null;

        try
        {
            var (id, _) = await E2C2ExperimentFactory.CreatePreparedSingleTrialExperimentAsync(factory, "e2c2b-cleanup-release");
            experimentId = id;

            await using var scope = factory.Services.CreateAsyncScope();
            var lab = scope.ServiceProvider.GetRequiredService<IValidationLabService>();
            var result = await lab.RunTrainingAsync(id);

            Assert.False(result.Succeeded);
            Assert.Equal(ValidationTrainingFailureCodes.TrainingCleanupFailed, result.ErrorField);

            var experiment = await scope.ServiceProvider.GetRequiredService<IValidationExperimentRepository>()
                .GetByIdAsync(id);
            Assert.NotNull(experiment);
            E2C2FailureReasonHelpers.AssertNoSensitiveMessages(experiment!, result.ErrorMessage);
            Assert.NotEqual(ValidationExperimentStatus.TrainingCompleted, experiment!.Status);
            Assert.True(ValidationLifecycleGate.CanResumeTraining(experiment.Status));
            Assert.False(experiment.IsQualificationCapable);
            Assert.Equal(ValidationTrainingFailureCodes.TrainingCleanupFailed, experiment.PrimaryFailureReason);
            E2C2FailureReasonHelpers.AssertExactFailureRecords(
                experiment,
                (ValidationTrainingFailureCodes.TrainingCleanupFailed, ValidationTrainingFailurePhase.LeaseRelease));

            var freeze = await lab.FreezeAsync(id);
            Assert.False(freeze.Succeeded);
            var validation = await lab.RunValidationAsync(id);
            Assert.False(validation.Succeeded);
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
    public async Task CleanupOnly_HeartbeatFailure_CannotReachTrainingCompleted()
    {
        await using var factory = new E2C2OrchestrationFactory();
        factory.Controls.RunnerMode = E2C2RunnerMode.AllowedComplete;
        factory.Controls.FailLeaseHeartbeat = true;
        long? experimentId = null;

        try
        {
            var (id, _) = await E2C2ExperimentFactory.CreatePreparedSingleTrialExperimentAsync(factory, "e2c2b-cleanup-hb");
            experimentId = id;

            await using var scope = factory.Services.CreateAsyncScope();
            var lab = scope.ServiceProvider.GetRequiredService<IValidationLabService>();
            var result = await lab.RunTrainingAsync(id);

            Assert.False(result.Succeeded);
            Assert.Equal(ValidationTrainingFailureCodes.TrainingCleanupFailed, result.ErrorField);

            var experiment = await scope.ServiceProvider.GetRequiredService<IValidationExperimentRepository>()
                .GetByIdAsync(id);
            Assert.NotNull(experiment);
            Assert.NotEqual(ValidationExperimentStatus.TrainingCompleted, experiment!.Status);
            Assert.True(ValidationLifecycleGate.CanResumeTraining(experiment.Status));
            Assert.False(experiment.IsQualificationCapable);
            Assert.Equal(ValidationTrainingFailureCodes.TrainingCleanupFailed, experiment.PrimaryFailureReason);
            Assert.Contains(
                E2C2FailureReasonHelpers.ParseRecords(experiment.FailureReasonsJson),
                r => r.Phase == ValidationTrainingFailurePhase.LeaseHeartbeat);

            var freeze = await lab.FreezeAsync(id);
            Assert.False(freeze.Succeeded);
            var validation = await lab.RunValidationAsync(id);
            Assert.False(validation.Succeeded);
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
    public async Task HeartbeatFailure_CannotReenableQualificationDuringSelection()
    {
        await using var factory = new E2C2OrchestrationFactory();
        factory.Controls.RunnerMode = E2C2RunnerMode.AllowedComplete;
        factory.Controls.FailLeaseHeartbeat = true;
        long? experimentId = null;

        try
        {
            var (id, _) = await E2C2ExperimentFactory.CreatePreparedSingleTrialExperimentAsync(factory, "e2c2b-hb-qualify");
            experimentId = id;

            await using var scope = factory.Services.CreateAsyncScope();
            var lab = scope.ServiceProvider.GetRequiredService<IValidationLabService>();
            _ = await lab.RunTrainingAsync(id);

            var experiment = await scope.ServiceProvider.GetRequiredService<IValidationExperimentRepository>()
                .GetByIdAsync(id);
            Assert.NotNull(experiment);
            Assert.False(experiment!.IsQualificationCapable);
            Assert.NotEqual(ValidationExperimentStatus.TrainingCompleted, experiment.Status);
            Assert.Contains(
                E2C2FailureReasonHelpers.ParseRecords(experiment.FailureReasonsJson),
                r => r.Code == ValidationTrainingFailureCodes.TrainingCleanupFailed);
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
    public async Task OuterScopeDisposalOnly_IsPersistedAndReturnsFailure()
    {
        await using var factory = new E2C2OrchestrationFactory();
        factory.Controls.RunnerMode = E2C2RunnerMode.AllowedComplete;
        factory.Controls.FailScopeDisposal = true;
        long? experimentId = null;

        try
        {
            var (id, _) = await E2C2ExperimentFactory.CreatePreparedSingleTrialExperimentAsync(factory, "e2c2b-disposal-only");
            experimentId = id;

            await using var scope = factory.Services.CreateAsyncScope();
            var lab = scope.ServiceProvider.GetRequiredService<IValidationLabService>();
            var result = await lab.RunTrainingAsync(id);

            Assert.False(result.Succeeded);
            var experiment = await scope.ServiceProvider.GetRequiredService<IValidationExperimentRepository>()
                .GetByIdAsync(id);
            Assert.NotNull(experiment);
            Assert.False(experiment!.IsQualificationCapable);
            Assert.NotEqual(ValidationExperimentStatus.TrainingCompleted, experiment.Status);
            E2C2FailureReasonHelpers.AssertExactFailureRecords(
                experiment,
                (ValidationTrainingFailureCodes.TrainingCleanupFailed, ValidationTrainingFailurePhase.ScopeDisposal));
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
    public async Task BoundaryAndScopeDisposal_PersistsBothWithBoundaryPrimary()
    {
        await using var factory = new E2C2OrchestrationFactory();
        factory.Controls.RunnerMode = E2C2RunnerMode.AdversarialBoundary;
        factory.Controls.FailScopeDisposal = true;
        long? experimentId = null;

        try
        {
            var (id, _) = await E2C2ExperimentFactory.CreatePreparedSingleTrialExperimentAsync(factory, "e2c2b-boundary-disposal");
            experimentId = id;

            await using var scope = factory.Services.CreateAsyncScope();
            var lab = scope.ServiceProvider.GetRequiredService<IValidationLabService>();
            var result = await lab.RunTrainingAsync(id);

            Assert.False(result.Succeeded);
            var experiment = await scope.ServiceProvider.GetRequiredService<IValidationExperimentRepository>()
                .GetByIdAsync(id);
            Assert.NotNull(experiment);
            E2C2FailureReasonHelpers.AssertExactFailureRecords(
                experiment!,
                (ValidationTrainingFailureCodes.ValidationDataLeakage, ValidationTrainingFailurePhase.TrialBody),
                (ValidationTrainingFailureCodes.TrainingCleanupFailed, ValidationTrainingFailurePhase.ScopeDisposal));
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
    public async Task AuditAndScopeDisposal_PersistsBothWithAuditPrimary()
    {
        await using var factory = new E2C2OrchestrationFactory();
        factory.Controls.RunnerMode = E2C2RunnerMode.AllowedComplete;
        factory.Controls.FailOnFlushNumbers.Add(1);
        factory.Controls.FailScopeDisposal = true;
        long? experimentId = null;

        try
        {
            var (id, _) = await E2C2ExperimentFactory.CreatePreparedSingleTrialExperimentAsync(factory, "e2c2b-audit-disposal");
            experimentId = id;

            await using var scope = factory.Services.CreateAsyncScope();
            var lab = scope.ServiceProvider.GetRequiredService<IValidationLabService>();
            var result = await lab.RunTrainingAsync(id);

            Assert.False(result.Succeeded);
            var experiment = await scope.ServiceProvider.GetRequiredService<IValidationExperimentRepository>()
                .GetByIdAsync(id);
            Assert.NotNull(experiment);
            var records = E2C2FailureReasonHelpers.ParseRecords(experiment!.FailureReasonsJson);
            Assert.Equal(ValidationTrainingFailureCodes.ValidationAccessAuditPersistenceFailed, experiment.PrimaryFailureReason);
            Assert.Contains(records, r => r.Category == ValidationTrainingFailureCategory.AuditDurability);
            Assert.Contains(records, r => r.Phase == ValidationTrainingFailurePhase.ScopeDisposal);
            Assert.Equal(ValidationTrainingFailureCodes.ValidationAccessAuditPersistenceFailed, records[0].Code);
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
    public async Task ExistingResultAndDisposalFailure_DoesNotLoseDisposal()
    {
        await using var factory = new E2C2OrchestrationFactory();
        factory.Controls.RunnerMode = E2C2RunnerMode.AdversarialBoundary;
        factory.Controls.FailScopeDisposal = true;
        long? experimentId = null;

        try
        {
            var (id, _) = await E2C2ExperimentFactory.CreatePreparedSingleTrialExperimentAsync(factory, "e2c2b-existing-disposal");
            experimentId = id;

            await using var scope = factory.Services.CreateAsyncScope();
            var lab = scope.ServiceProvider.GetRequiredService<IValidationLabService>();
            _ = await lab.RunTrainingAsync(id);

            var experiment = await scope.ServiceProvider.GetRequiredService<IValidationExperimentRepository>()
                .GetByIdAsync(id);
            Assert.NotNull(experiment);
            var records = E2C2FailureReasonHelpers.ParseRecords(experiment!.FailureReasonsJson);
            Assert.Contains(records, r => r.Code == ValidationTrainingFailureCodes.ValidationDataLeakage);
            Assert.Contains(records, r => r.Phase == ValidationTrainingFailurePhase.ScopeDisposal);
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
    public async Task FinalizationOnlyIncomplete_UsesCanonicalAggregate()
    {
        await using var factory = new E2C2OrchestrationFactory();
        factory.Controls.RunnerMode = E2C2RunnerMode.AllowedComplete;
        factory.Controls.FailAuditFinalizationIncomplete = true;
        long? experimentId = null;

        try
        {
            var (id, _) = await E2C2ExperimentFactory.CreatePreparedSingleTrialExperimentAsync(factory, "e2c2b-fin-incomplete");
            experimentId = id;

            await using var scope = factory.Services.CreateAsyncScope();
            var lab = scope.ServiceProvider.GetRequiredService<IValidationLabService>();
            var result = await lab.RunTrainingAsync(id);

            Assert.False(result.Succeeded);
            var experiment = await scope.ServiceProvider.GetRequiredService<IValidationExperimentRepository>()
                .GetByIdAsync(id);
            Assert.NotNull(experiment);
            Assert.False(experiment!.IsQualificationCapable);
            Assert.NotEqual(ValidationExperimentStatus.TrainingCompleted, experiment.Status);
            var records = E2C2FailureReasonHelpers.ParseRecords(experiment.FailureReasonsJson);
            Assert.Contains(records, r =>
                r.Category == ValidationTrainingFailureCategory.AuditDurability
                && r.Phase == ValidationTrainingFailurePhase.AuditFinalization);
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

    [Fact]
    public async Task FinalizationOnlyVerifierIncomplete_UsesCanonicalAggregate()
    {
        await using var factory = new E2C2OrchestrationFactory();
        factory.Controls.RunnerMode = E2C2RunnerMode.AllowedComplete;
        factory.Controls.FailCompletenessVerification = true;
        long? experimentId = null;

        try
        {
            var (id, _) = await E2C2ExperimentFactory.CreatePreparedSingleTrialExperimentAsync(factory, "e2c2b-ver-incomplete");
            experimentId = id;

            await using var scope = factory.Services.CreateAsyncScope();
            var lab = scope.ServiceProvider.GetRequiredService<IValidationLabService>();
            var result = await lab.RunTrainingAsync(id);

            Assert.False(result.Succeeded);
            var experiment = await scope.ServiceProvider.GetRequiredService<IValidationExperimentRepository>()
                .GetByIdAsync(id);
            Assert.NotNull(experiment);
            Assert.False(experiment!.IsQualificationCapable);
            var records = E2C2FailureReasonHelpers.ParseRecords(experiment.FailureReasonsJson);
            Assert.NotEmpty(records);
            Assert.Equal(ValidationTrainingFailureCategory.AuditDurability, records[0].Category);
            Assert.Equal(ValidationTrainingFailurePhase.CompletenessVerification, records[0].Phase);
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

    [Fact]
    public async Task GenericFinalizerException_IsAuditDurability()
    {
        await using var factory = new E2C2OrchestrationFactory();
        factory.Controls.RunnerMode = E2C2RunnerMode.AllowedComplete;
        factory.Controls.ThrowOnAuditFinalizer = true;
        long? experimentId = null;

        try
        {
            var (id, _) = await E2C2ExperimentFactory.CreatePreparedSingleTrialExperimentAsync(factory, "e2c2b-fin-throw");
            experimentId = id;

            await using var scope = factory.Services.CreateAsyncScope();
            var lab = scope.ServiceProvider.GetRequiredService<IValidationLabService>();
            var result = await lab.RunTrainingAsync(id);

            Assert.False(result.Succeeded);
            var experiment = await scope.ServiceProvider.GetRequiredService<IValidationExperimentRepository>()
                .GetByIdAsync(id);
            Assert.NotNull(experiment);
            E2C2FailureReasonHelpers.AssertExactFailureRecords(
                experiment!,
                (ValidationTrainingFailureCodes.ValidationAccessAuditPersistenceFailed,
                    ValidationTrainingFailurePhase.AuditFinalization));
            E2C2FailureReasonHelpers.AssertNoSensitiveMessages(experiment!, result.ErrorMessage);
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
    public async Task GenericVerifierException_IsAuditDurability()
    {
        await using var factory = new E2C2OrchestrationFactory();
        factory.Controls.RunnerMode = E2C2RunnerMode.AllowedComplete;
        factory.Controls.ThrowOnCompletenessVerifier = true;
        long? experimentId = null;

        try
        {
            var (id, _) = await E2C2ExperimentFactory.CreatePreparedSingleTrialExperimentAsync(factory, "e2c2b-ver-throw");
            experimentId = id;

            await using var scope = factory.Services.CreateAsyncScope();
            var lab = scope.ServiceProvider.GetRequiredService<IValidationLabService>();
            var result = await lab.RunTrainingAsync(id);

            Assert.False(result.Succeeded);
            var experiment = await scope.ServiceProvider.GetRequiredService<IValidationExperimentRepository>()
                .GetByIdAsync(id);
            Assert.NotNull(experiment);
            E2C2FailureReasonHelpers.AssertExactFailureRecords(
                experiment!,
                (ValidationTrainingFailureCodes.ValidationAccessAuditPersistenceFailed,
                    ValidationTrainingFailurePhase.CompletenessVerification));
            E2C2FailureReasonHelpers.AssertNoSensitiveMessages(experiment!, result.ErrorMessage);
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
    public async Task RevalidationFailure_AppendsRankIneligibleReasons()
    {
        await using var factory = new E2C2OrchestrationFactory();
        factory.Controls.RunnerMode = E2C2RunnerMode.AllowedComplete;
        factory.Controls.FailCompletenessVerification = true;
        long? experimentId = null;

        try
        {
            var (id, _) = await E2C2ExperimentFactory.CreatePreparedSingleTrialExperimentAsync(factory, "e2c2b-rank-append");
            experimentId = id;

            await using (var seedScope = factory.Services.CreateAsyncScope())
            {
                var trials = seedScope.ServiceProvider.GetRequiredService<IValidationParameterTrialRepository>();
                // Trial rows are created during training; seed after a no-op is not available.
                // Assert append behavior after the completeness failure path runs.
            }

            await using var scope = factory.Services.CreateAsyncScope();
            var lab = scope.ServiceProvider.GetRequiredService<IValidationLabService>();
            _ = await lab.RunTrainingAsync(id);

            var trial = (await scope.ServiceProvider.GetRequiredService<IValidationParameterTrialRepository>()
                .GetByExperimentIdAsync(id)).First();
            Assert.False(string.IsNullOrWhiteSpace(trial.RankIneligibleReasonsJson));
            Assert.Contains(
                ValidationAuditCompletenessCode.SequenceGap.ToString(),
                trial.RankIneligibleReasonsJson!,
                StringComparison.Ordinal);

            // Append must not overwrite: apply helper again and retain prior code.
            ValidationTrainingFailurePersistence.AppendRankIneligibleReasons(trial, ["PriorSeededReason"]);
            Assert.Contains("PriorSeededReason", trial.RankIneligibleReasonsJson!, StringComparison.Ordinal);
            Assert.Contains(
                ValidationAuditCompletenessCode.SequenceGap.ToString(),
                trial.RankIneligibleReasonsJson!,
                StringComparison.Ordinal);
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
    public async Task OperationStatusHeartbeatAndReleaseFailures_RetainDistinctPhases()
    {
        await using var factory = new E2C2OrchestrationFactory();
        factory.Controls.RunnerMode = E2C2RunnerMode.AllowedComplete;
        factory.Controls.FailLeaseHeartbeat = true;
        factory.Controls.FailLeaseRelease = true;
        long? experimentId = null;

        try
        {
            var (id, _) = await E2C2ExperimentFactory.CreatePreparedSingleTrialExperimentAsync(factory, "e2c2b-phases");
            experimentId = id;

            await using (var seedScope = factory.Services.CreateAsyncScope())
            {
                var experiments = seedScope.ServiceProvider.GetRequiredService<IValidationExperimentRepository>();
                var seeded = await experiments.GetByIdAsync(id)
                    ?? throw new InvalidOperationException("Experiment missing.");
                seeded.FailureReasonsJson = ValidationTrainingFailureJson.SerializeRecords(
                [
                    new ValidationTrainingFailureRecord
                    {
                        Code = ValidationTrainingFailureCodes.TrainingCleanupFailed,
                        Category = ValidationTrainingFailureCategory.Cleanup,
                        Precedence = ValidationTrainingFailurePrecedence.Cleanup,
                        Phase = ValidationTrainingFailurePhase.OperationStatusSync,
                        UserSafeMessage = ValidationTrainingFailureHandler.UserSafeCleanupMessage,
                        OccurredAtUtc = DateTime.UtcNow.AddMinutes(-1),
                        IsQualificationBlocking = false
                    }
                ]);
                seeded.PrimaryFailureReason = ValidationTrainingFailureCodes.TrainingCleanupFailed;
                seeded.IsQualificationCapable = false;
                await experiments.UpdateAsync(seeded);
            }

            await using var scope = factory.Services.CreateAsyncScope();
            var lab = scope.ServiceProvider.GetRequiredService<IValidationLabService>();
            var result = await lab.RunTrainingAsync(id);
            Assert.False(result.Succeeded);

            var experiment = await scope.ServiceProvider.GetRequiredService<IValidationExperimentRepository>()
                .GetByIdAsync(id);
            Assert.NotNull(experiment);
            var phases = E2C2FailureReasonHelpers.ParseRecords(experiment!.FailureReasonsJson)
                .Select(r => r.Phase)
                .ToArray();
            Assert.Contains(ValidationTrainingFailurePhase.OperationStatusSync, phases);
            Assert.Contains(ValidationTrainingFailurePhase.LeaseHeartbeat, phases);
            Assert.Contains(ValidationTrainingFailurePhase.LeaseRelease, phases);
            Assert.Equal(
                phases.Length,
                phases.Select(p => p).Distinct().Count());
        }
        finally
        {
            if (experimentId is long eid)
            {
                await E2C2ExperimentFactory.CleanupExperimentAsync(factory, eid);
            }
        }
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
