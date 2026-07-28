using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.ValidationLab;
using MomoQuant.Application.ValidationLab.Dtos;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.ValidationLab;
using MomoQuant.Persistence;

namespace MomoQuant.IntegrationTests;

/// <summary>
/// Milestone 23.0E2C3A — negative-evidence gates, verdict recalculation gating,
/// and safe authoritative-evaluator failure handling on production paths.
/// </summary>
[Collection("Integration")]
public sealed class Milestone230E2C3AOrchestrationTests
{
    [Fact]
    public async Task TrainingFinalize_DeniedOldAttempt_ReturnsBoundaryFailure()
    {
        await using var factory = new E2C2OrchestrationFactory();
        factory.Controls.RunnerMode = E2C2RunnerMode.AllowedComplete;
        long? experimentId = null;

        try
        {
            var (id, _) = await E2C2ExperimentFactory.CreatePreparedSingleTrialExperimentAsync(
                factory, "e2c3a-denied-finalize");
            experimentId = id;

            await using (var seedScope = factory.Services.CreateAsyncScope())
            {
                await AddDeniedForeignAuditAsync(seedScope.ServiceProvider, id, "E2C3A-DeniedFinalize");
            }

            await using var scope = factory.Services.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<IValidationLabService>()
                .RunTrainingAsync(id);
            Assert.False(result.Succeeded);

            var experiment = await ReloadExperimentAsync(scope, id);
            Assert.NotEqual(ValidationExperimentStatus.TrainingCompleted, experiment.Status);
            Assert.False(experiment.IsQualificationCapable);
            Assert.Equal(ValidationLeakageAuditStatus.Failed, experiment.LeakageAuditStatus);
            AssertStructuredBoundaryRecords(experiment);
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
    public async Task TrainingFinalize_FailedLeakageAudit_CannotReachTrainingCompleted()
    {
        await using var factory = new E2C2OrchestrationFactory();
        factory.Controls.RunnerMode = E2C2RunnerMode.AllowedComplete;
        long? experimentId = null;

        try
        {
            var (id, _) = await E2C2ExperimentFactory.CreatePreparedSingleTrialExperimentAsync(
                factory, "e2c3a-leakage-no-complete");
            experimentId = id;

            await using (var seedScope = factory.Services.CreateAsyncScope())
            {
                await AddDeniedForeignAuditAsync(seedScope.ServiceProvider, id, "E2C3A-LeakBlock");
            }

            await using var scope = factory.Services.CreateAsyncScope();
            _ = await scope.ServiceProvider.GetRequiredService<IValidationLabService>().RunTrainingAsync(id);

            var experiment = await ReloadExperimentAsync(scope, id);
            Assert.NotEqual(ValidationExperimentStatus.TrainingCompleted, experiment.Status);
            Assert.False(experiment.IsQualificationCapable);
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
    public async Task TrainingFinalize_NegativeEvidence_NeverRemainsQualificationCapable()
    {
        await using var factory = new E2C2OrchestrationFactory();
        factory.Controls.RunnerMode = E2C2RunnerMode.AllowedComplete;
        long? experimentId = null;

        try
        {
            var (id, _) = await E2C2ExperimentFactory.CreatePreparedSingleTrialExperimentAsync(
                factory, "e2c3a-never-capable");
            experimentId = id;

            await using (var seedScope = factory.Services.CreateAsyncScope())
            {
                await AddDeniedForeignAuditAsync(seedScope.ServiceProvider, id, "E2C3A-NeverCap");
            }

            await using var scope = factory.Services.CreateAsyncScope();
            _ = await scope.ServiceProvider.GetRequiredService<IValidationLabService>().RunTrainingAsync(id);

            var experiment = await ReloadExperimentAsync(scope, id);
            Assert.False(experiment.IsQualificationCapable);
            Assert.Equal(ValidationLeakageAuditStatus.Failed, experiment.LeakageAuditStatus);
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
    public async Task Freeze_PreexistingFailedLeakage_ClearsQualificationCapability()
    {
        await using var factory = new E2C2OrchestrationFactory();
        long? experimentId = null;

        try
        {
            var (id, combo) = await E2C2ExperimentFactory.CreatePreparedSingleTrialExperimentAsync(
                factory, "e2c3a-preexisting-leak");
            experimentId = id;

            await using (var seedScope = factory.Services.CreateAsyncScope())
            {
                await SeedCompletedAuditBundleAsync(seedScope.ServiceProvider, id, combo, "pre-leak");
                var experiments = seedScope.ServiceProvider.GetRequiredService<IValidationExperimentRepository>();
                var trial = (await seedScope.ServiceProvider.GetRequiredService<IValidationParameterTrialRepository>()
                    .GetByExperimentIdAsync(id)).Single();
                var experiment = await experiments.GetByIdAsync(id)
                    ?? throw new InvalidOperationException("Experiment missing.");
                experiment.Status = ValidationExperimentStatus.TrainingCompleted;
                experiment.CurrentStage = "TrainingCompleted";
                experiment.SelectedTrialId = trial.Id;
                experiment.SelectedTrialParameterSnapshotJson = trial.ParameterSnapshotJson;
                experiment.SelectedTrialParameterFingerprint = trial.ParameterFingerprint;
                experiment.IsQualificationCapable = true;
                experiment.LeakageAuditStatus = ValidationLeakageAuditStatus.Failed;
                await experiments.UpdateAsync(experiment);
            }

            await using var scope = factory.Services.CreateAsyncScope();
            var freeze = await scope.ServiceProvider.GetRequiredService<IValidationLabService>().FreezeAsync(id);
            Assert.False(freeze.Succeeded);

            var reloaded = await ReloadExperimentAsync(scope, id);
            Assert.False(reloaded.IsQualificationCapable);
            Assert.Null(reloaded.FrozenAtUtc);
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
    public async Task Freeze_PreexistingFailedLeakage_PersistsBoundaryWithoutDuplication()
    {
        await using var factory = new E2C2OrchestrationFactory();
        long? experimentId = null;

        try
        {
            var (id, combo) = await E2C2ExperimentFactory.CreatePreparedSingleTrialExperimentAsync(
                factory, "e2c3a-pre-leak-dedupe");
            experimentId = id;

            await using (var seedScope = factory.Services.CreateAsyncScope())
            {
                await SeedCompletedAuditBundleAsync(seedScope.ServiceProvider, id, combo, "pre-dedupe");
                var experiments = seedScope.ServiceProvider.GetRequiredService<IValidationExperimentRepository>();
                var trial = (await seedScope.ServiceProvider.GetRequiredService<IValidationParameterTrialRepository>()
                    .GetByExperimentIdAsync(id)).Single();
                var experiment = await experiments.GetByIdAsync(id)
                    ?? throw new InvalidOperationException("Experiment missing.");
                experiment.Status = ValidationExperimentStatus.TrainingCompleted;
                experiment.SelectedTrialId = trial.Id;
                experiment.SelectedTrialParameterSnapshotJson = trial.ParameterSnapshotJson;
                experiment.SelectedTrialParameterFingerprint = trial.ParameterFingerprint;
                experiment.IsQualificationCapable = true;
                experiment.LeakageAuditStatus = ValidationLeakageAuditStatus.Failed;
                await experiments.UpdateAsync(experiment);
            }

            await using var scope = factory.Services.CreateAsyncScope();
            var lab = scope.ServiceProvider.GetRequiredService<IValidationLabService>();
            _ = await lab.FreezeAsync(id);
            _ = await lab.FreezeAsync(id);

            var reloaded = await ReloadExperimentAsync(scope, id);
            var records = E2C2FailureReasonHelpers.ParseRecords(reloaded.FailureReasonsJson);
            Assert.Single(records);
            Assert.Equal(ValidationTrainingFailureCategory.Boundary, records[0].Category);
            Assert.Equal(ValidationTrainingFailureCodes.ValidationDataLeakage, records[0].Code);
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
    public async Task ValidationStart_DeniedEvidenceAddedAfterFreeze_DoesNotInvokeRunner()
    {
        await using var factory = new E2C2OrchestrationFactory();
        long? experimentId = null;

        try
        {
            var (id, combo) = await E2C2ExperimentFactory.CreatePreparedSingleTrialExperimentAsync(
                factory, "e2c3a-val-start-denied");
            experimentId = id;
            await RunSuccessfulTrainingAndFreezeAsync(factory, id, combo);

            await using (var seedScope = factory.Services.CreateAsyncScope())
            {
                await AddDeniedForeignAuditAsync(seedScope.ServiceProvider, id, "E2C3A-AfterFreeze");
            }

            var runnerBefore = factory.Controls.RunnerInvocationCount;

            await using var scope = factory.Services.CreateAsyncScope();
            var validation = await scope.ServiceProvider.GetRequiredService<IValidationLabService>()
                .RunValidationAsync(id);
            Assert.False(validation.Succeeded);
            Assert.Equal(runnerBefore, factory.Controls.RunnerInvocationCount);

            var experiment = await ReloadExperimentAsync(scope, id);
            Assert.False(experiment.IsQualificationCapable);
            AssertStructuredBoundaryRecords(experiment);
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
    public async Task ValidationStart_DeniedSupersededAttemptAfterFreeze_IsBlocking()
    {
        await using var factory = new E2C2OrchestrationFactory();
        long? experimentId = null;

        try
        {
            var (id, combo) = await E2C2ExperimentFactory.CreatePreparedSingleTrialExperimentAsync(
                factory, "e2c3a-val-sup-denied");
            experimentId = id;
            await RunSuccessfulTrainingAndFreezeAsync(factory, id, combo);

            await using (var seedScope = factory.Services.CreateAsyncScope())
            {
                var sp = seedScope.ServiceProvider;
                var denied = E2BAuditFixtures.NewAudit(
                    id, Guid.NewGuid(), Guid.NewGuid(), 1, "E2C3A-SupDenied", wasDenied: true);
                denied.DenialCode = "ValidationDataLeakageDetected";
                await sp.GetRequiredService<IValidationCandleAccessAuditRepository>()
                    .AddRangeIdempotentByAccessEventIdAsync([denied]);
            }

            await using var scope = factory.Services.CreateAsyncScope();
            var validation = await scope.ServiceProvider.GetRequiredService<IValidationLabService>()
                .RunValidationAsync(id);
            Assert.False(validation.Succeeded);

            var experiment = await ReloadExperimentAsync(scope, id);
            Assert.Equal(ValidationLeakageAuditStatus.Failed, experiment.LeakageAuditStatus);
            Assert.False(experiment.IsQualificationCapable);
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
    public async Task Verdict_DeniedEvidenceAddedDuringValidation_CannotPassOrReveal()
    {
        await using var factory = new E2C2OrchestrationFactory();
        factory.Controls.AllowNonTrainingRuns = true;
        long? experimentId = null;

        try
        {
            var (id, combo) = await E2C2ExperimentFactory.CreatePreparedSingleTrialExperimentAsync(
                factory, "e2c3a-verdict-denied");
            experimentId = id;
            await RunSuccessfulTrainingAndFreezeAsync(factory, id, combo);

            await using (var confirmScope = factory.Services.CreateAsyncScope())
            {
                var audits = await confirmScope.ServiceProvider
                    .GetRequiredService<IValidationCandleAccessAuditRepository>()
                    .GetByExperimentIdAsync(id);
                Assert.Empty(ValidationLeakageEvidenceSelector.CollectNegativeBlockingEvidence(audits));
            }

            factory.Controls.InjectDeniedEvidenceAfterNonTrainingRunForExperimentId = id;
            var runnerBefore = factory.Controls.RunnerInvocationCount;

            await using var scope = factory.Services.CreateAsyncScope();
            var validation = await scope.ServiceProvider.GetRequiredService<IValidationLabService>()
                .RunValidationAsync(id);
            Assert.False(validation.Succeeded);
            Assert.Equal(runnerBefore + 1, factory.Controls.RunnerInvocationCount);

            var experiment = await ReloadExperimentAsync(scope, id);
            AssertStructuredBoundaryRecords(experiment);
            Assert.False(experiment.IsQualificationCapable);
            Assert.NotEqual(ValidationRevealStatus.Revealed, experiment.ValidationRevealStatus);
            Assert.Null(experiment.ValidationRevealedAtUtc);
            Assert.NotEqual(StrategyRobustnessDecision.Passed, experiment.StrategyRobustnessDecision);
            Assert.NotEqual(ValidationExperimentStatus.Completed, experiment.Status);
            E2C2FailureReasonHelpers.AssertNoSensitiveMessages(experiment, validation.ErrorMessage);
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
    public async Task Verdict_NegativeEvidence_PreservesBoundaryPrimary()
    {
        await using var factory = new E2C2OrchestrationFactory();
        factory.Controls.AllowNonTrainingRuns = true;
        long? experimentId = null;

        try
        {
            var (id, combo) = await E2C2ExperimentFactory.CreatePreparedSingleTrialExperimentAsync(
                factory, "e2c3a-verdict-boundary-primary");
            experimentId = id;
            await RunSuccessfulTrainingAndFreezeAsync(factory, id, combo);

            await using (var seedScope = factory.Services.CreateAsyncScope())
            {
                var experiments = seedScope.ServiceProvider.GetRequiredService<IValidationExperimentRepository>();
                var seeded = await experiments.GetByIdAsync(id)
                    ?? throw new InvalidOperationException("Experiment missing.");
                var prior = ValidationTrainingFailurePersistence.MergeExisting(seeded.FailureReasonsJson);
                prior.Observe(new ValidationTrainingFailureRecord
                {
                    Code = ValidationTrainingFailureCodes.ValidationDataLeakage,
                    Category = ValidationTrainingFailureCategory.Boundary,
                    Precedence = ValidationTrainingFailurePrecedence.Boundary,
                    Phase = ValidationTrainingFailurePhase.TrialBody,
                    UserSafeMessage = ValidationTrainingFailureHandler.UserSafeLeakageMessage,
                    OccurredAtUtc = DateTime.UtcNow.AddMinutes(-10),
                    IsQualificationBlocking = true
                });
                ValidationTrainingFailurePersistence.ApplyToExperiment(seeded, prior);
                seeded.IsQualificationCapable = true;
                await experiments.UpdateAsync(seeded);

                await AddDeniedForeignAuditAsync(seedScope.ServiceProvider, id, "E2C3A-VerdictBoundary");
            }

            await using var scope = factory.Services.CreateAsyncScope();
            _ = await scope.ServiceProvider.GetRequiredService<IValidationLabService>().RunValidationAsync(id);

            var experiment = await ReloadExperimentAsync(scope, id);
            var records = E2C2FailureReasonHelpers.ParseRecords(experiment.FailureReasonsJson);
            Assert.NotEmpty(records);
            Assert.Equal(ValidationTrainingFailureCategory.Boundary, records[0].Category);
            Assert.Equal(ValidationTrainingFailureCodes.ValidationDataLeakage, experiment.PrimaryFailureReason);
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
    public async Task RecalculateVerdict_AuditCorrupted_CannotWritePassed()
    {
        await using var factory = new E2C2OrchestrationFactory();
        factory.Controls.AllowNonTrainingRuns = true;
        long? experimentId = null;

        try
        {
            var (id, combo) = await E2C2ExperimentFactory.CreatePreparedSingleTrialExperimentAsync(
                factory, "e2c3a-recalc-audit");
            experimentId = id;
            await RunSuccessfulTrainingAndFreezeAsync(factory, id, combo);

            await using (var runScope = factory.Services.CreateAsyncScope())
            {
                _ = await runScope.ServiceProvider.GetRequiredService<IValidationLabService>().RunValidationAsync(id);
            }

            await using (var corruptScope = factory.Services.CreateAsyncScope())
            {
                var db = corruptScope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
                await db.ValidationCandleAccessAudits
                    .Where(a => a.ValidationExperimentId == id)
                    .ExecuteDeleteAsync();
            }

            await using var scope = factory.Services.CreateAsyncScope();
            var recalc = await scope.ServiceProvider.GetRequiredService<IValidationLabService>()
                .RecalculateVerdictAsync(id);
            Assert.False(recalc.Succeeded);

            var experiment = await ReloadExperimentAsync(scope, id);
            Assert.NotEqual(StrategyRobustnessDecision.Passed, experiment.StrategyRobustnessDecision);
            Assert.False(experiment.IsQualificationCapable);
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
    public async Task RecalculateVerdict_NonQualificationCapable_CannotRestorePassed()
    {
        await using var factory = new E2C2OrchestrationFactory();
        factory.Controls.AllowNonTrainingRuns = true;
        long? experimentId = null;

        try
        {
            var (id, combo) = await E2C2ExperimentFactory.CreatePreparedSingleTrialExperimentAsync(
                factory, "e2c3a-recalc-noncap");
            experimentId = id;
            await RunSuccessfulTrainingAndFreezeAsync(factory, id, combo);

            await using (var runScope = factory.Services.CreateAsyncScope())
            {
                _ = await runScope.ServiceProvider.GetRequiredService<IValidationLabService>().RunValidationAsync(id);
            }

            await using (var seedScope = factory.Services.CreateAsyncScope())
            {
                var experiments = seedScope.ServiceProvider.GetRequiredService<IValidationExperimentRepository>();
                var seeded = await experiments.GetByIdAsync(id)
                    ?? throw new InvalidOperationException("Experiment missing.");
                seeded.IsQualificationCapable = false;
                seeded.StrategyRobustnessDecision = StrategyRobustnessDecision.FailedPerformanceCollapse;
                await experiments.UpdateAsync(seeded);
            }

            await using var scope = factory.Services.CreateAsyncScope();
            var recalc = await scope.ServiceProvider.GetRequiredService<IValidationLabService>()
                .RecalculateVerdictAsync(id);
            Assert.False(recalc.Succeeded);

            var experiment = await ReloadExperimentAsync(scope, id);
            Assert.NotEqual(StrategyRobustnessDecision.Passed, experiment.StrategyRobustnessDecision);
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
    public async Task RecalculateVerdict_DeniedForeignAttempt_IsBlocking()
    {
        await using var factory = new E2C2OrchestrationFactory();
        factory.Controls.AllowNonTrainingRuns = true;
        long? experimentId = null;

        try
        {
            var (id, combo) = await E2C2ExperimentFactory.CreatePreparedSingleTrialExperimentAsync(
                factory, "e2c3a-recalc-denied");
            experimentId = id;
            await RunSuccessfulTrainingAndFreezeAsync(factory, id, combo);

            await using (var runScope = factory.Services.CreateAsyncScope())
            {
                _ = await runScope.ServiceProvider.GetRequiredService<IValidationLabService>().RunValidationAsync(id);
            }

            await using (var seedScope = factory.Services.CreateAsyncScope())
            {
                await AddDeniedForeignAuditAsync(seedScope.ServiceProvider, id, "E2C3A-RecalcDenied");
                var experiments = seedScope.ServiceProvider.GetRequiredService<IValidationExperimentRepository>();
                var seeded = await experiments.GetByIdAsync(id)
                    ?? throw new InvalidOperationException("Experiment missing.");
                seeded.IsQualificationCapable = true;
                await experiments.UpdateAsync(seeded);
            }

            await using var scope = factory.Services.CreateAsyncScope();
            var recalc = await scope.ServiceProvider.GetRequiredService<IValidationLabService>()
                .RecalculateVerdictAsync(id);
            Assert.False(recalc.Succeeded);

            var experiment = await ReloadExperimentAsync(scope, id);
            AssertStructuredBoundaryRecords(experiment);
            Assert.NotEqual(StrategyRobustnessDecision.Passed, experiment.StrategyRobustnessDecision);
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
    public async Task TrainingFinalize_AuthoritativeEvaluatorThrows_ReturnsSafeAuditFailure()
    {
        await using var factory = new E2C2OrchestrationFactory();
        factory.Controls.RunnerMode = E2C2RunnerMode.AllowedComplete;
        factory.Controls.ThrowOnCompletenessVerifier = true;
        long? experimentId = null;

        try
        {
            var (id, _) = await E2C2ExperimentFactory.CreatePreparedSingleTrialExperimentAsync(
                factory, "e2c3a-finalize-throw");
            experimentId = id;

            await using var scope = factory.Services.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<IValidationLabService>()
                .RunTrainingAsync(id);
            Assert.False(result.Succeeded);

            var experiment = await ReloadExperimentAsync(scope, id);
            Assert.False(experiment.IsQualificationCapable);
            AssertStructuredAuditDurabilityRecords(experiment);
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
    public async Task Freeze_AuthoritativeEvaluatorThrows_ReturnsSafeAuditFailure()
    {
        await using var factory = new E2C2OrchestrationFactory();
        long? experimentId = null;

        try
        {
            var (id, combo) = await E2C2ExperimentFactory.CreatePreparedSingleTrialExperimentAsync(
                factory, "e2c3a-freeze-throw");
            experimentId = id;
            await RunSuccessfulTrainingAsync(factory, id, combo);

            factory.Controls.ThrowOnCompletenessVerifier = true;

            await using var scope = factory.Services.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<IValidationLabService>().FreezeAsync(id);
            Assert.False(result.Succeeded);

            var experiment = await ReloadExperimentAsync(scope, id);
            Assert.False(experiment.IsQualificationCapable);
            Assert.Null(experiment.FrozenAtUtc);
            AssertStructuredAuditDurabilityRecords(experiment);
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
    public async Task ValidationStart_AuthoritativeEvaluatorThrows_ReturnsSafeAuditFailure()
    {
        await using var factory = new E2C2OrchestrationFactory();
        long? experimentId = null;

        try
        {
            var (id, combo) = await E2C2ExperimentFactory.CreatePreparedSingleTrialExperimentAsync(
                factory, "e2c3a-val-start-throw");
            experimentId = id;
            await RunSuccessfulTrainingAndFreezeAsync(factory, id, combo);

            factory.Controls.ThrowOnCompletenessVerifier = true;
            var runnerBefore = factory.Controls.RunnerInvocationCount;

            await using var scope = factory.Services.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<IValidationLabService>()
                .RunValidationAsync(id);
            Assert.False(result.Succeeded);
            Assert.Equal(runnerBefore, factory.Controls.RunnerInvocationCount);

            var experiment = await ReloadExperimentAsync(scope, id);
            AssertStructuredAuditDurabilityRecords(experiment);
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
    public async Task Verdict_AuthoritativeEvaluatorThrows_ReturnsSafeAuditFailure()
    {
        await using var factory = new E2C2OrchestrationFactory();
        factory.Controls.AllowNonTrainingRuns = true;
        long? experimentId = null;

        try
        {
            var (id, combo) = await E2C2ExperimentFactory.CreatePreparedSingleTrialExperimentAsync(
                factory, "e2c3a-verdict-throw");
            experimentId = id;
            await RunSuccessfulTrainingAndFreezeAsync(factory, id, combo);

            factory.Controls.CorruptAuthoritativeAuditAfterNonTrainingRunForExperimentId = id;
            factory.Controls.ThrowOnCompletenessVerifier = true;

            await using var scope = factory.Services.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<IValidationLabService>()
                .RunValidationAsync(id);
            Assert.False(result.Succeeded);

            var experiment = await ReloadExperimentAsync(scope, id);
            Assert.NotEqual(ValidationRevealStatus.Revealed, experiment.ValidationRevealStatus);
            AssertStructuredAuditDurabilityRecords(experiment);
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
    public async Task RecalculateVerdict_AuthoritativeEvaluatorThrows_ReturnsSafeAuditFailure()
    {
        await using var factory = new E2C2OrchestrationFactory();
        factory.Controls.AllowNonTrainingRuns = true;
        long? experimentId = null;

        try
        {
            var (id, combo) = await E2C2ExperimentFactory.CreatePreparedSingleTrialExperimentAsync(
                factory, "e2c3a-recalc-throw");
            experimentId = id;
            await RunSuccessfulTrainingAndFreezeAsync(factory, id, combo);

            await using (var runScope = factory.Services.CreateAsyncScope())
            {
                _ = await runScope.ServiceProvider.GetRequiredService<IValidationLabService>().RunValidationAsync(id);
            }

            factory.Controls.ThrowOnCompletenessVerifier = true;

            await using var scope = factory.Services.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<IValidationLabService>()
                .RecalculateVerdictAsync(id);
            Assert.False(result.Succeeded);

            var experiment = await ReloadExperimentAsync(scope, id);
            AssertStructuredAuditDurabilityRecords(experiment);
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

    private static async Task RunSuccessfulTrainingAsync(
        E2C2OrchestrationFactory factory,
        long id,
        IReadOnlyDictionary<string, string> combo)
    {
        _ = combo;
        factory.Controls.RunnerMode = E2C2RunnerMode.AllowedComplete;
        await using var scope = factory.Services.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<IValidationLabService>().RunTrainingAsync(id);
        Assert.True(result.Succeeded, result.ErrorMessage ?? "Training failed.");
    }

    private static async Task RunSuccessfulTrainingAndFreezeAsync(
        E2C2OrchestrationFactory factory,
        long id,
        IReadOnlyDictionary<string, string> combo)
    {
        await RunSuccessfulTrainingAsync(factory, id, combo);
        await using var scope = factory.Services.CreateAsyncScope();
        var freeze = await scope.ServiceProvider.GetRequiredService<IValidationLabService>().FreezeAsync(id);
        Assert.True(freeze.Succeeded, freeze.ErrorMessage ?? "Freeze failed.");
    }

    private static async Task AddDeniedForeignAuditAsync(
        IServiceProvider sp,
        long experimentId,
        string suffix)
    {
        var denied = E2BAuditFixtures.NewAudit(
            experimentId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            1,
            suffix,
            wasDenied: true);
        denied.DenialCode = "ValidationDataLeakageDetected";
        denied.DenialReason = "foreign denied attempt";
        await sp.GetRequiredService<IValidationCandleAccessAuditRepository>()
            .AddRangeIdempotentByAccessEventIdAsync([denied]);
    }

    private static async Task<(ValidationParameterTrial Trial, ValidationAuditExecution Execution)> SeedCompletedAuditBundleAsync(
        IServiceProvider sp,
        long experimentId,
        IReadOnlyDictionary<string, string> combo,
        string suffix)
    {
        var trials = sp.GetRequiredService<IValidationParameterTrialRepository>();
        var experiments = sp.GetRequiredService<IValidationExperimentRepository>();
        var fingerprint = ValidationLabService.ParameterFingerprint(combo);

        var existing = await trials.GetByExperimentIdAsync(experimentId);
        ValidationParameterTrial trial;
        if (existing.Count == 0)
        {
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
            trial = await trials.GetByExperimentAndFingerprintAsync(experimentId, fingerprint)
                ?? throw new InvalidOperationException("Trial missing.");
        }
        else
        {
            trial = existing.Single();
        }

        var experiment = await experiments.GetByIdAsync(experimentId)
            ?? throw new InvalidOperationException("Experiment missing.");

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

        return (trial, execution);
    }

    private static void AssertStructuredBoundaryRecords(ValidationExperiment experiment)
    {
        var records = E2C2FailureReasonHelpers.ParseRecords(experiment.FailureReasonsJson);
        Assert.NotEmpty(records);
        Assert.Equal(ValidationTrainingFailureCategory.Boundary, records[0].Category);
        Assert.Equal(ValidationTrainingFailureCodes.ValidationDataLeakage, records[0].Code);
        E2C2FailureReasonHelpers.AssertNoMirroredDiagnosticDuplicates(experiment);
    }

    private static void AssertStructuredAuditDurabilityRecords(ValidationExperiment experiment)
    {
        var records = E2C2FailureReasonHelpers.ParseRecords(experiment.FailureReasonsJson);
        Assert.NotEmpty(records);
        Assert.All(records, r => Assert.Equal(ValidationTrainingFailureCategory.AuditDurability, r.Category));
        E2C2FailureReasonHelpers.AssertNoMirroredDiagnosticDuplicates(experiment);
    }

    private static async Task<ValidationExperiment> ReloadExperimentAsync(IServiceScope scope, long id)
    {
        var experiment = await scope.ServiceProvider.GetRequiredService<IValidationExperimentRepository>()
            .GetByIdAsync(id);
        Assert.NotNull(experiment);
        return experiment!;
    }
}
