using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.ValidationLab;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.ValidationLab;
using MomoQuant.Persistence;

namespace MomoQuant.IntegrationTests;

/// <summary>
/// Milestone 23.0E2C3A2 — genuine ValidateExistingFrozenConfiguration applicability closure.
/// </summary>
[Collection("Integration")]
public sealed class Milestone230E2C3A2OrchestrationTests
{
    [Fact]
    public async Task ValidationStart_ExistingFrozen_NoTrials_InvokesRunner()
    {
        await using var factory = new E2C2OrchestrationFactory();
        factory.Controls.AllowNonTrainingRuns = true;
        long? experimentId = null;

        try
        {
            var id = await E2C2ExperimentFactory.CreateGenuineExistingFrozenExperimentAsync(
                factory, "no-trials");
            experimentId = id;

            await AssertGenuineExistingFrozenPreconditionsAsync(factory, id);

            factory.Controls.ArmTrialPopulationGetFailure = true;
            var runnerBefore = factory.Controls.RunnerInvocationCount;
            var trialGetsBefore = factory.Controls.TrialPopulationGetInvocationCount;
            var auditEvalBefore = factory.Controls.AuthoritativeAuditEvaluateTrialInvocationCount;

            await using (var runScope = factory.Services.CreateAsyncScope())
            {
                var result = await runScope.ServiceProvider.GetRequiredService<IValidationLabService>()
                    .RunValidationAsync(id);
                Assert.True(result.Succeeded, result.ErrorMessage ?? "Validation failed.");
            }

            Assert.Equal(runnerBefore + 1, factory.Controls.RunnerInvocationCount);
            Assert.Equal(trialGetsBefore, factory.Controls.TrialPopulationGetInvocationCount);
            Assert.Equal(auditEvalBefore, factory.Controls.AuthoritativeAuditEvaluateTrialInvocationCount);
            Assert.True(factory.Controls.ArmTrialPopulationGetFailure);

            factory.Controls.ArmTrialPopulationGetFailure = false;

            await using var assertScope = factory.Services.CreateAsyncScope();
            var experiment = await ReloadExperimentAsync(assertScope, id);
            Assert.Equal(ValidationExperimentStatus.Completed, experiment.Status);
            Assert.Equal(ValidationRevealStatus.Revealed, experiment.ValidationRevealStatus);
            Assert.NotNull(experiment.ValidationRevealedAtUtc);
            Assert.Equal(ValidationSelectionIntegrityStatus.NotEvaluated, experiment.SelectionIntegrityStatus);
            Assert.Null(experiment.SelectedTrialId);
            Assert.Null(experiment.SelectedTrialNumber);
            Assert.Null(experiment.SelectedTrialParameterFingerprint);
            Assert.False(experiment.IsQualificationCapable);
            Assert.Equal(ParameterStabilityApplicability.NotApplicable, experiment.ParameterStabilityApplicability);

            var trials = await assertScope.ServiceProvider
                .GetRequiredService<IValidationParameterTrialRepository>()
                .GetByExperimentIdAsync(id);
            Assert.Empty(trials);

            var executions = await assertScope.ServiceProvider.GetRequiredService<MomoQuantDbContext>()
                .ValidationAuditExecutions
                .Where(e => e.ValidationExperimentId == id)
                .CountAsync();
            Assert.Equal(0, executions);
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
    public async Task ValidationStart_ExistingFrozen_DoesNotReadTrialRepository()
    {
        await using var factory = new E2C2OrchestrationFactory();
        factory.Controls.AllowNonTrainingRuns = true;
        long? experimentId = null;

        try
        {
            var id = await E2C2ExperimentFactory.CreateGenuineExistingFrozenExperimentAsync(
                factory, "no-repo-read");
            experimentId = id;

            factory.Controls.ArmTrialPopulationGetFailure = true;
            var trialGetsBefore = factory.Controls.TrialPopulationGetInvocationCount;

            await using var scope = factory.Services.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<IValidationLabService>()
                .RunValidationAsync(id);
            Assert.True(result.Succeeded, result.ErrorMessage);
            Assert.Equal(trialGetsBefore, factory.Controls.TrialPopulationGetInvocationCount);
            Assert.True(factory.Controls.ArmTrialPopulationGetFailure);
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
    public async Task ValidationStart_ExistingFrozen_NotEvaluatedSelectionIntegrity_IsNotBlocked()
    {
        await using var factory = new E2C2OrchestrationFactory();
        factory.Controls.AllowNonTrainingRuns = true;
        long? experimentId = null;

        try
        {
            var id = await E2C2ExperimentFactory.CreateGenuineExistingFrozenExperimentAsync(
                factory, "not-eval",
                e => e.SelectionIntegrityStatus = ValidationSelectionIntegrityStatus.NotEvaluated);
            experimentId = id;

            factory.Controls.ArmTrialPopulationGetFailure = true;
            var runnerBefore = factory.Controls.RunnerInvocationCount;

            await using var scope = factory.Services.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<IValidationLabService>()
                .RunValidationAsync(id);
            Assert.True(result.Succeeded, result.ErrorMessage);
            Assert.Equal(runnerBefore + 1, factory.Controls.RunnerInvocationCount);
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
    public async Task ValidationStart_ExistingFrozen_NonQualificationCapable_IsNotBlocked()
    {
        await using var factory = new E2C2OrchestrationFactory();
        factory.Controls.AllowNonTrainingRuns = true;
        long? experimentId = null;

        try
        {
            var id = await E2C2ExperimentFactory.CreateGenuineExistingFrozenExperimentAsync(
                factory, "non-capable",
                e => e.IsQualificationCapable = false);
            experimentId = id;

            factory.Controls.ArmTrialPopulationGetFailure = true;
            var runnerBefore = factory.Controls.RunnerInvocationCount;

            await using var scope = factory.Services.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<IValidationLabService>()
                .RunValidationAsync(id);
            Assert.True(result.Succeeded, result.ErrorMessage);
            Assert.Equal(runnerBefore + 1, factory.Controls.RunnerInvocationCount);

            await using var assertScope = factory.Services.CreateAsyncScope();
            var experiment = await ReloadExperimentAsync(assertScope, id);
            Assert.False(experiment.IsQualificationCapable);
            Assert.Equal(ValidationExperimentStatus.Completed, experiment.Status);
            Assert.Equal(ValidationRevealStatus.Revealed, experiment.ValidationRevealStatus);
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
    public async Task ValidationStart_ExistingFrozen_MissingSelectedTrial_IsNotBlocked()
    {
        await using var factory = new E2C2OrchestrationFactory();
        factory.Controls.AllowNonTrainingRuns = true;
        long? experimentId = null;

        try
        {
            var id = await E2C2ExperimentFactory.CreateGenuineExistingFrozenExperimentAsync(
                factory, "no-selected",
                e =>
                {
                    e.SelectedTrialId = null;
                    e.SelectedTrialNumber = null;
                    e.SelectedTrialParameterFingerprint = null;
                });
            experimentId = id;

            factory.Controls.ArmTrialPopulationGetFailure = true;
            var runnerBefore = factory.Controls.RunnerInvocationCount;
            var auditEvalBefore = factory.Controls.AuthoritativeAuditEvaluateTrialInvocationCount;

            await using var scope = factory.Services.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<IValidationLabService>()
                .RunValidationAsync(id);
            Assert.True(result.Succeeded, result.ErrorMessage);
            Assert.Equal(runnerBefore + 1, factory.Controls.RunnerInvocationCount);
            Assert.Equal(auditEvalBefore, factory.Controls.AuthoritativeAuditEvaluateTrialInvocationCount);
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
    public async Task ValidationStart_ExistingFrozen_InvalidFrozenSnapshot_DoesNotInvokeRunner()
    {
        await using var factory = new E2C2OrchestrationFactory();
        factory.Controls.AllowNonTrainingRuns = true;
        long? experimentId = null;

        try
        {
            var id = await E2C2ExperimentFactory.CreateGenuineExistingFrozenExperimentAsync(
                factory, "bad-snap",
                e => e.FrozenStrategyParameterSnapshotJson = "{not-json");
            experimentId = id;

            var runnerBefore = factory.Controls.RunnerInvocationCount;

            await using var scope = factory.Services.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<IValidationLabService>()
                .RunValidationAsync(id);
            Assert.False(result.Succeeded);
            Assert.Equal(runnerBefore, factory.Controls.RunnerInvocationCount);

            await using var assertScope = factory.Services.CreateAsyncScope();
            AssertBlockedUnrevealed(await ReloadExperimentAsync(assertScope, id));
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
    public async Task ValidationStart_ExistingFrozen_InvalidFrozenFingerprint_DoesNotInvokeRunner()
    {
        await using var factory = new E2C2OrchestrationFactory();
        factory.Controls.AllowNonTrainingRuns = true;
        long? experimentId = null;

        try
        {
            var id = await E2C2ExperimentFactory.CreateGenuineExistingFrozenExperimentAsync(
                factory, "bad-fp",
                e => e.FrozenParameterFingerprint = ValidationParameterFingerprintService.EmptyContentFingerprint);
            experimentId = id;

            var runnerBefore = factory.Controls.RunnerInvocationCount;

            await using var scope = factory.Services.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<IValidationLabService>()
                .RunValidationAsync(id);
            Assert.False(result.Succeeded);
            Assert.Equal(runnerBefore, factory.Controls.RunnerInvocationCount);

            await using var assertScope = factory.Services.CreateAsyncScope();
            AssertBlockedUnrevealed(await ReloadExperimentAsync(assertScope, id));
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
    public async Task ValidationStart_ExistingFrozen_NegativeEvidence_DoesNotInvokeRunner()
    {
        await using var factory = new E2C2OrchestrationFactory();
        factory.Controls.AllowNonTrainingRuns = true;
        long? experimentId = null;

        try
        {
            var id = await E2C2ExperimentFactory.CreateGenuineExistingFrozenExperimentAsync(
                factory, "neg-ev");
            experimentId = id;

            await using (var seedScope = factory.Services.CreateAsyncScope())
            {
                await AddDeniedForeignAuditAsync(seedScope.ServiceProvider, id, "e2c3a2-neg");
            }

            var runnerBefore = factory.Controls.RunnerInvocationCount;

            await using var scope = factory.Services.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<IValidationLabService>()
                .RunValidationAsync(id);
            Assert.False(result.Succeeded);
            Assert.Equal(runnerBefore, factory.Controls.RunnerInvocationCount);

            await using var assertScope = factory.Services.CreateAsyncScope();
            var experiment = await ReloadExperimentAsync(assertScope, id);
            AssertBlockedUnrevealed(experiment);
            Assert.False(experiment.IsQualificationCapable);
            Assert.NotEqual(ValidationExperimentStatus.ValidationRunning, experiment.Status);
            var records = E2C2FailureReasonHelpers.ParseRecords(experiment.FailureReasonsJson);
            Assert.NotEmpty(records);
            Assert.Equal(ValidationTrainingFailureCategory.Boundary, records[0].Category);
            Assert.Equal(ValidationTrainingFailureCodes.ValidationDataLeakage, records[0].Code);
            E2C2FailureReasonHelpers.AssertNoSensitiveMessages(experiment, result.ErrorMessage);
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
    public async Task ValidationStart_TrainingSearch_StillRequiresSelectionIntegrity()
    {
        await using var factory = new E2C2OrchestrationFactory();
        factory.Controls.AllowNonTrainingRuns = true;
        long? experimentId = null;

        try
        {
            var (id, combo) = await E2C2ExperimentFactory.CreatePreparedSingleTrialExperimentAsync(
                factory, "train-integrity");
            experimentId = id;
            await RunSuccessfulTrainingAndFreezeAsync(factory, id, combo);

            await using (var seedScope = factory.Services.CreateAsyncScope())
            {
                var experiments = seedScope.ServiceProvider.GetRequiredService<IValidationExperimentRepository>();
                var seeded = await experiments.GetByIdAsync(id)
                    ?? throw new InvalidOperationException("Experiment missing.");
                seeded.SelectionIntegrityStatus = ValidationSelectionIntegrityStatus.NotEvaluated;
                await experiments.UpdateAsync(seeded);
            }

            var runnerBefore = factory.Controls.RunnerInvocationCount;
            var trialGetsBefore = factory.Controls.TrialPopulationGetInvocationCount;

            await using var scope = factory.Services.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<IValidationLabService>()
                .RunValidationAsync(id);
            Assert.False(result.Succeeded);
            Assert.Contains("selection integrity", result.ErrorMessage ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(runnerBefore, factory.Controls.RunnerInvocationCount);
            Assert.True(factory.Controls.TrialPopulationGetInvocationCount > trialGetsBefore);

            await using var assertScope = factory.Services.CreateAsyncScope();
            AssertBlockedUnrevealed(await ReloadExperimentAsync(assertScope, id));
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
    public async Task ValidationStart_TrainingSearch_StillRequiresAuthoritativeTrialAudit()
    {
        await using var factory = new E2C2OrchestrationFactory();
        factory.Controls.AllowNonTrainingRuns = true;
        long? experimentId = null;

        try
        {
            var (id, combo) = await E2C2ExperimentFactory.CreatePreparedSingleTrialExperimentAsync(
                factory, "train-audit");
            experimentId = id;
            await RunSuccessfulTrainingAndFreezeAsync(factory, id, combo);

            await using (var seedScope = factory.Services.CreateAsyncScope())
            {
                var trials = seedScope.ServiceProvider.GetRequiredService<IValidationParameterTrialRepository>();
                var trial = (await trials.GetByExperimentIdAsync(id)).Single();
                Assert.NotNull(trial.AuthoritativeAuditExecutionId);
                var auditId = trial.AuthoritativeAuditExecutionId!.Value;

                // Keep cached eligibility markers so CanStartValidation reaches the live evaluator,
                // then remove the durable execution so authoritative revalidation fails closed.
                var db = seedScope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
                await db.ValidationAuditBatches
                    .Where(b => b.AuditExecutionId == auditId)
                    .ExecuteDeleteAsync();
                await db.ValidationAuditExecutions
                    .Where(e => e.AuditExecutionId == auditId)
                    .ExecuteDeleteAsync();
            }

            var runnerBefore = factory.Controls.RunnerInvocationCount;
            var trialGetsBefore = factory.Controls.TrialPopulationGetInvocationCount;
            var auditEvalBefore = factory.Controls.AuthoritativeAuditEvaluateTrialInvocationCount;

            await using var scope = factory.Services.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<IValidationLabService>()
                .RunValidationAsync(id);
            Assert.False(result.Succeeded);
            Assert.Equal(runnerBefore, factory.Controls.RunnerInvocationCount);
            Assert.True(factory.Controls.TrialPopulationGetInvocationCount > trialGetsBefore);
            Assert.True(factory.Controls.AuthoritativeAuditEvaluateTrialInvocationCount > auditEvalBefore);

            await using var assertScope = factory.Services.CreateAsyncScope();
            AssertBlockedUnrevealed(await ReloadExperimentAsync(assertScope, id));
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
    public async Task RerunExactly_ExistingFrozen_CloneRunsWithoutTrainingArtifacts()
    {
        await using var factory = new E2C2OrchestrationFactory();
        factory.Controls.AllowNonTrainingRuns = true;
        long? sourceId = null;
        long? cloneId = null;

        try
        {
            sourceId = await E2C2ExperimentFactory.CreateGenuineExistingFrozenExperimentAsync(
                factory, "rerun-src");

            long cloned;
            await using (var cloneScope = factory.Services.CreateAsyncScope())
            {
                var rerun = await cloneScope.ServiceProvider.GetRequiredService<IValidationLabService>()
                    .RerunExactlyAsync(sourceId.Value);
                Assert.True(rerun.Succeeded, rerun.ErrorMessage);
                Assert.NotNull(rerun.Data);
                cloned = rerun.Data!.Id;
            }

            cloneId = cloned;

            await using (var preloadScope = factory.Services.CreateAsyncScope())
            {
                var clone = await ReloadExperimentAsync(preloadScope, cloned);
                Assert.Equal(ValidationExperimentType.ValidateExistingFrozenConfiguration, clone.ExperimentType);
                Assert.Equal(ValidationExperimentStatus.ConfigurationFrozen, clone.Status);
                Assert.Equal(ValidationSelectionIntegrityStatus.NotEvaluated, clone.SelectionIntegrityStatus);
                Assert.Null(clone.SelectedTrialId);
                Assert.Null(clone.SelectedTrialNumber);
                Assert.Null(clone.SelectedTrialParameterFingerprint);

                clone.IsQualificationCapable = false;
                await preloadScope.ServiceProvider.GetRequiredService<IValidationExperimentRepository>()
                    .UpdateAsync(clone);

                var trials = await preloadScope.ServiceProvider
                    .GetRequiredService<IValidationParameterTrialRepository>()
                    .GetByExperimentIdAsync(cloned);
                Assert.Empty(trials);
            }

            factory.Controls.ArmTrialPopulationGetFailure = true;
            var runnerBefore = factory.Controls.RunnerInvocationCount;
            var trialGetsBefore = factory.Controls.TrialPopulationGetInvocationCount;
            var auditEvalBefore = factory.Controls.AuthoritativeAuditEvaluateTrialInvocationCount;

            await using (var runScope = factory.Services.CreateAsyncScope())
            {
                var result = await runScope.ServiceProvider.GetRequiredService<IValidationLabService>()
                    .RunValidationAsync(cloned);
                Assert.True(result.Succeeded, result.ErrorMessage);
            }

            Assert.Equal(runnerBefore + 1, factory.Controls.RunnerInvocationCount);
            Assert.Equal(trialGetsBefore, factory.Controls.TrialPopulationGetInvocationCount);
            Assert.Equal(auditEvalBefore, factory.Controls.AuthoritativeAuditEvaluateTrialInvocationCount);

            await using var assertScope = factory.Services.CreateAsyncScope();
            var after = await ReloadExperimentAsync(assertScope, cloned);
            Assert.Equal(ValidationExperimentStatus.Completed, after.Status);
            Assert.Equal(ValidationRevealStatus.Revealed, after.ValidationRevealStatus);
            Assert.NotNull(after.ValidationRevealedAtUtc);
            Assert.False(after.IsQualificationCapable);
            Assert.Equal(ValidationSelectionIntegrityStatus.NotEvaluated, after.SelectionIntegrityStatus);
            Assert.Null(after.SelectedTrialId);
            Assert.Equal(ParameterStabilityApplicability.NotApplicable, after.ParameterStabilityApplicability);
        }
        finally
        {
            if (cloneId is long cid)
            {
                await E2C2ExperimentFactory.CleanupExperimentAsync(factory, cid);
            }

            if (sourceId is long sid)
            {
                await E2C2ExperimentFactory.CleanupExperimentAsync(factory, sid);
            }
        }
    }

    private static async Task AssertGenuineExistingFrozenPreconditionsAsync(
        E2C2OrchestrationFactory factory,
        long id)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var experiment = await ReloadExperimentAsync(scope, id);
        Assert.Equal(ValidationExperimentType.ValidateExistingFrozenConfiguration, experiment.ExperimentType);
        Assert.Equal(ValidationExperimentStatus.ConfigurationFrozen, experiment.Status);
        Assert.Null(experiment.SelectedTrialId);
        Assert.Null(experiment.SelectedTrialNumber);
        Assert.Null(experiment.SelectedTrialParameterFingerprint);
        Assert.Equal(ValidationSelectionIntegrityStatus.NotEvaluated, experiment.SelectionIntegrityStatus);
        Assert.False(experiment.IsQualificationCapable);
        Assert.NotNull(experiment.ValidationStartUtc);
        Assert.NotNull(experiment.ValidationEndUtc);
        Assert.False(string.IsNullOrWhiteSpace(experiment.FrozenStrategyParameterSnapshotJson));
        Assert.False(string.IsNullOrWhiteSpace(experiment.FrozenParameterFingerprint));
        Assert.NotEqual(
            ValidationParameterFingerprintService.EmptyContentFingerprint,
            experiment.FrozenParameterFingerprint);
        Assert.Equal(ValidationRevealStatus.Frozen, experiment.ValidationRevealStatus);

        var trials = await scope.ServiceProvider.GetRequiredService<IValidationParameterTrialRepository>()
            .GetByExperimentIdAsync(id);
        Assert.Empty(trials);
    }

    private static async Task RunSuccessfulTrainingAndFreezeAsync(
        E2C2OrchestrationFactory factory,
        long id,
        IReadOnlyDictionary<string, string> combo)
    {
        _ = combo;
        factory.Controls.RunnerMode = E2C2RunnerMode.AllowedComplete;
        await using (var trainScope = factory.Services.CreateAsyncScope())
        {
            var training = await trainScope.ServiceProvider.GetRequiredService<IValidationLabService>()
                .RunTrainingAsync(id);
            Assert.True(training.Succeeded, training.ErrorMessage ?? "Training failed.");
        }

        await using var freezeScope = factory.Services.CreateAsyncScope();
        var freeze = await freezeScope.ServiceProvider.GetRequiredService<IValidationLabService>()
            .FreezeAsync(id);
        Assert.True(freeze.Succeeded, freeze.ErrorMessage ?? "Freeze failed.");
    }

    private static async Task AddDeniedForeignAuditAsync(IServiceProvider sp, long experimentId, string suffix)
    {
        var denied = E2BAuditFixtures.NewAudit(
            experimentId, Guid.NewGuid(), Guid.NewGuid(), 1, suffix, wasDenied: true);
        denied.DenialCode = "ValidationDataLeakageDetected";
        denied.DenialReason = "foreign denied attempt";
        await sp.GetRequiredService<IValidationCandleAccessAuditRepository>()
            .AddRangeIdempotentByAccessEventIdAsync([denied]);
    }

    private static void AssertBlockedUnrevealed(ValidationExperiment experiment)
    {
        Assert.NotEqual(ValidationRevealStatus.Revealed, experiment.ValidationRevealStatus);
        Assert.Null(experiment.ValidationRevealedAtUtc);
        Assert.NotEqual(ValidationExperimentStatus.Completed, experiment.Status);
    }

    private static async Task<ValidationExperiment> ReloadExperimentAsync(IServiceScope scope, long id)
    {
        var experiment = await scope.ServiceProvider.GetRequiredService<IValidationExperimentRepository>()
            .GetByIdAsync(id);
        Assert.NotNull(experiment);
        return experiment!;
    }
}
