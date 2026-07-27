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

/// <summary>
/// Milestone 23.0E2C3 — authoritative audit qualification gates on training finalize,
/// freeze, validation start, and verdict with production-path MySQL orchestration.
/// </summary>
[Collection("Integration")]
public sealed class Milestone230E2C3OrchestrationTests
{
    [Fact]
    public async Task TrainingFinalize_AllGuardrailPassedButAuditIncomplete_ReturnsAuditFailure()
    {
        await using var factory = new E2C2OrchestrationFactory();
        factory.Controls.RunnerMode = E2C2RunnerMode.AllowedComplete;
        factory.Controls.FailCompletenessVerification = true;
        long? experimentId = null;

        try
        {
            var (id, _) = await E2C2ExperimentFactory.CreatePreparedSingleTrialExperimentAsync(
                factory, "e2c3-finalize-incomplete");
            experimentId = id;

            await using var scope = factory.Services.CreateAsyncScope();
            var lab = scope.ServiceProvider.GetRequiredService<IValidationLabService>();
            var result = await lab.RunTrainingAsync(id);

            Assert.False(result.Succeeded);

            var experiment = await ReloadExperimentAsync(scope, id);
            Assert.Equal(ValidationExperimentStatus.Failed, experiment.Status);
            Assert.False(experiment.IsQualificationCapable);
            Assert.NotEqual(ValidationExperimentStatus.TrainingCompleted, experiment.Status);

            var records = E2C2FailureReasonHelpers.ParseRecords(experiment.FailureReasonsJson);
            Assert.NotEmpty(records);
            Assert.Equal(ValidationTrainingFailureCategory.AuditDurability, records[0].Category);
            Assert.Equal(ValidationTrainingFailurePhase.CompletenessVerification, records[0].Phase);
            E2C2FailureReasonHelpers.AssertNoMirroredDiagnosticDuplicates(experiment);
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
    public async Task TrainingFinalize_TamperedAuditCompletionStatus_CannotSelectOrComplete()
    {
        await using var factory = new E2C2OrchestrationFactory();
        long? experimentId = null;

        try
        {
            var (id, combo) = await E2C2ExperimentFactory.CreatePreparedSingleTrialExperimentAsync(
                factory, "e2c3-tampered-complete");
            experimentId = id;

            Guid auditExecutionId;
            await using (var seedScope = factory.Services.CreateAsyncScope())
            {
                var (_, execution) = await SeedCompletedAuditBundleAsync(
                    seedScope.ServiceProvider, id, combo, "tampered");
                auditExecutionId = execution.AuditExecutionId;

                var db = seedScope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
                await db.ValidationCandleAccessAudits
                    .Where(a => a.ScopeExecutionId == execution.ScopeExecutionId)
                    .ExecuteDeleteAsync();

                var trials = seedScope.ServiceProvider.GetRequiredService<IValidationParameterTrialRepository>();
                var trial = (await trials.GetByExperimentIdAsync(id)).Single();
                trial.AuthoritativeAuditExecutionId = auditExecutionId;
                trial.AuditCompletionStatus = ValidationAuditCompletionStatus.Complete;
                trial.Status = ValidationTrialStatus.Completed;
                trial.TrialRankEligibility = ValidationTrialRankEligibility.Eligible;
                await trials.UpdateAsync(trial);

                var experiments = seedScope.ServiceProvider.GetRequiredService<IValidationExperimentRepository>();
                var experiment = await experiments.GetByIdAsync(id)
                    ?? throw new InvalidOperationException("Experiment missing.");
                experiment.Status = ValidationExperimentStatus.TrainingInterrupted;
                experiment.CurrentStage = "TrainingInterrupted";
                experiment.SelectedTrialId = trial.Id;
                await experiments.UpdateAsync(experiment);
            }

            await using (var resumeScope = factory.Services.CreateAsyncScope())
            {
                var result = await resumeScope.ServiceProvider.GetRequiredService<IValidationLabService>()
                    .ResumeTrainingAsync(id);
                Assert.False(result.Succeeded);
            }

            Assert.Equal(0, factory.Controls.RunnerInvocationCount);

            await using var assertScope = factory.Services.CreateAsyncScope();
            var reloaded = await ReloadExperimentAsync(assertScope, id);
            Assert.NotEqual(ValidationExperimentStatus.TrainingCompleted, reloaded.Status);
            Assert.False(reloaded.IsQualificationCapable);
            Assert.Null(reloaded.SelectedTrialId);
            var records = E2C2FailureReasonHelpers.ParseRecords(reloaded.FailureReasonsJson);
            Assert.NotEmpty(records);
            Assert.Equal(ValidationTrainingFailureCategory.AuditDurability, records[0].Category);
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
    public async Task TrainingFinalize_SelectedExecutionSuperseded_CannotReachTrainingCompleted()
    {
        await using var factory = new E2C2OrchestrationFactory();
        long? experimentId = null;

        try
        {
            var (id, combo) = await E2C2ExperimentFactory.CreatePreparedSingleTrialExperimentAsync(
                factory, "e2c3-superseded-resume");
            experimentId = id;

            Guid executionId;
            await using (var seedScope = factory.Services.CreateAsyncScope())
            {
                var (_, execution) = await SeedCompletedAuditBundleAsync(
                    seedScope.ServiceProvider, id, combo, "sup-resume");
                executionId = execution.AuditExecutionId;
                await SupersedeExecutionInDatabaseAsync(seedScope.ServiceProvider, executionId);

                var experiments = seedScope.ServiceProvider.GetRequiredService<IValidationExperimentRepository>();
                var experiment = await experiments.GetByIdAsync(id)
                    ?? throw new InvalidOperationException("Experiment missing.");
                experiment.Status = ValidationExperimentStatus.TrainingInterrupted;
                experiment.CurrentStage = "TrainingInterrupted";
                experiment.SelectedTrialId = (await seedScope.ServiceProvider
                    .GetRequiredService<IValidationParameterTrialRepository>()
                    .GetByExperimentIdAsync(id)).Single().Id;
                await experiments.UpdateAsync(experiment);
            }

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
            var reloaded = await ReloadExperimentAsync(assertScope, id);
            Assert.NotEqual(ValidationExperimentStatus.TrainingCompleted, reloaded.Status);
            Assert.False(reloaded.IsQualificationCapable);
            var records = E2C2FailureReasonHelpers.ParseRecords(reloaded.FailureReasonsJson);
            Assert.NotEmpty(records);
            Assert.Equal(ValidationTrainingFailureCategory.AuditDurability, records[0].Category);
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
    public async Task Freeze_SelectedTrialExecutionMissing_IsBlockedBeforeMutation()
    {
        await using var factory = new E2C2OrchestrationFactory();
        long? experimentId = null;

        try
        {
            var (id, _) = await E2C2ExperimentFactory.CreatePreparedSingleTrialExperimentAsync(
                factory, "e2c3-freeze-missing-exec");
            experimentId = id;
            await RunSuccessfulTrainingAsync(factory, id);

            await using (var corruptScope = factory.Services.CreateAsyncScope())
            {
                var trial = (await corruptScope.ServiceProvider
                    .GetRequiredService<IValidationParameterTrialRepository>()
                    .GetByExperimentIdAsync(id)).Single();
                Assert.NotNull(trial.AuthoritativeAuditExecutionId);

                var db = corruptScope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
                var auditId = trial.AuthoritativeAuditExecutionId!.Value;
                await db.ValidationAuditBatches
                    .Where(b => b.AuditExecutionId == auditId)
                    .ExecuteDeleteAsync();
                await db.ValidationAuditExecutions
                    .Where(e => e.AuditExecutionId == auditId)
                    .ExecuteDeleteAsync();
            }

            await using var scope = factory.Services.CreateAsyncScope();
            var lab = scope.ServiceProvider.GetRequiredService<IValidationLabService>();
            var freeze = await lab.FreezeAsync(id);
            Assert.False(freeze.Succeeded);

            var experiment = await ReloadExperimentAsync(scope, id);
            Assert.Null(experiment.FrozenAtUtc);
            Assert.Null(experiment.FrozenParameterFingerprint);
            Assert.Null(experiment.FrozenStrategyParameterSnapshotJson);
            Assert.False(experiment.IsQualificationCapable);
            AssertStructuredAuditDurabilityRecords(experiment);
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
    public async Task Freeze_SelectedTrialManifestCorrupt_IsBlockedAndNonQualified()
    {
        await using var factory = new E2C2OrchestrationFactory();
        long? experimentId = null;

        try
        {
            var (id, _) = await E2C2ExperimentFactory.CreatePreparedSingleTrialExperimentAsync(
                factory, "e2c3-freeze-corrupt-manifest");
            experimentId = id;
            await RunSuccessfulTrainingAsync(factory, id);

            await using (var corruptScope = factory.Services.CreateAsyncScope())
            {
                var trial = (await corruptScope.ServiceProvider
                    .GetRequiredService<IValidationParameterTrialRepository>()
                    .GetByExperimentIdAsync(id)).Single();
                var db = corruptScope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
                await db.ValidationCandleAccessAudits
                    .Where(a => a.ValidationExperimentId == id)
                    .ExecuteDeleteAsync();
                if (trial.AuthoritativeAuditExecutionId is Guid auditId)
                {
                    await db.ValidationAuditBatches
                        .Where(b => b.AuditExecutionId == auditId)
                        .ExecuteDeleteAsync();
                }
            }

            await using var scope = factory.Services.CreateAsyncScope();
            var freeze = await scope.ServiceProvider.GetRequiredService<IValidationLabService>().FreezeAsync(id);
            Assert.False(freeze.Succeeded);

            var experiment = await ReloadExperimentAsync(scope, id);
            Assert.False(experiment.IsQualificationCapable);
            Assert.Null(experiment.FrozenAtUtc);
            AssertStructuredAuditDurabilityRecords(experiment);
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
    public async Task Freeze_RepeatedAuditFailure_DoesNotDuplicateFailureReasons()
    {
        await using var factory = new E2C2OrchestrationFactory();
        long? experimentId = null;

        try
        {
            var (id, _) = await E2C2ExperimentFactory.CreatePreparedSingleTrialExperimentAsync(
                factory, "e2c3-freeze-dedupe");
            experimentId = id;
            await RunSuccessfulTrainingAsync(factory, id);

            await using (var corruptScope = factory.Services.CreateAsyncScope())
            {
                var db = corruptScope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
                await db.ValidationCandleAccessAudits
                    .Where(a => a.ValidationExperimentId == id)
                    .ExecuteDeleteAsync();
            }

            await using var scope = factory.Services.CreateAsyncScope();
            var lab = scope.ServiceProvider.GetRequiredService<IValidationLabService>();
            _ = await lab.FreezeAsync(id);
            var afterFirst = await ReloadExperimentAsync(scope, id);
            var firstRecords = E2C2FailureReasonHelpers.ParseRecords(afterFirst.FailureReasonsJson);
            var firstIdentities = firstRecords.Select(r => r.LogicalIdentity).ToArray();
            Assert.NotEmpty(firstIdentities);

            _ = await lab.FreezeAsync(id);
            var afterSecond = await ReloadExperimentAsync(scope, id);
            var secondRecords = E2C2FailureReasonHelpers.ParseRecords(afterSecond.FailureReasonsJson);
            Assert.Equal(firstRecords.Count, secondRecords.Count);
            Assert.Equal(firstIdentities, secondRecords.Select(r => r.LogicalIdentity).ToArray());
            Assert.Equal(
                firstIdentities.Length,
                firstIdentities.Distinct(StringComparer.Ordinal).Count());
            E2C2FailureReasonHelpers.AssertNoMirroredDiagnosticDuplicates(afterSecond);
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
    public async Task ValidationStart_AuditCorruptedAfterFreeze_DoesNotInvokeRunner()
    {
        await using var factory = new E2C2OrchestrationFactory();
        factory.Controls.AllowNonTrainingRuns = true;
        long? experimentId = null;

        try
        {
            var (id, _) = await E2C2ExperimentFactory.CreatePreparedSingleTrialExperimentAsync(
                factory, "e2c3-val-start-corrupt");
            experimentId = id;
            await RunSuccessfulTrainingAsync(factory, id);

            await using (var freezeScope = factory.Services.CreateAsyncScope())
            {
                var freeze = await freezeScope.ServiceProvider.GetRequiredService<IValidationLabService>()
                    .FreezeAsync(id);
                Assert.True(freeze.Succeeded, freeze.ErrorMessage ?? "Freeze failed.");
            }

            var runnerCountBeforeValidation = factory.Controls.RunnerInvocationCount;

            await using (var corruptScope = factory.Services.CreateAsyncScope())
            {
                var db = corruptScope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
                await db.ValidationCandleAccessAudits
                    .Where(a => a.ValidationExperimentId == id)
                    .ExecuteDeleteAsync();
            }

            await using var scope = factory.Services.CreateAsyncScope();
            var validation = await scope.ServiceProvider.GetRequiredService<IValidationLabService>()
                .RunValidationAsync(id);
            Assert.False(validation.Succeeded);
            Assert.Equal(runnerCountBeforeValidation, factory.Controls.RunnerInvocationCount);

            var experiment = await ReloadExperimentAsync(scope, id);
            Assert.NotEqual(ValidationRevealStatus.Revealed, experiment.ValidationRevealStatus);
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
    public async Task ValidationStart_SupersededAfterFreeze_RemainsUnrevealed()
    {
        await using var factory = new E2C2OrchestrationFactory();
        factory.Controls.AllowNonTrainingRuns = true;
        long? experimentId = null;

        try
        {
            var (id, _) = await E2C2ExperimentFactory.CreatePreparedSingleTrialExperimentAsync(
                factory, "e2c3-val-start-superseded");
            experimentId = id;
            await RunSuccessfulTrainingAsync(factory, id);

            Guid executionId;
            await using (var freezeScope = factory.Services.CreateAsyncScope())
            {
                var sp = freezeScope.ServiceProvider;
                var freeze = await sp.GetRequiredService<IValidationLabService>().FreezeAsync(id);
                Assert.True(freeze.Succeeded, freeze.ErrorMessage ?? "Freeze failed.");

                var trial = (await sp.GetRequiredService<IValidationParameterTrialRepository>()
                    .GetByExperimentIdAsync(id)).Single();
                executionId = trial.AuthoritativeAuditExecutionId!.Value;
                await SupersedeExecutionInDatabaseAsync(sp, executionId);
            }

            var runnerCountBeforeValidation = factory.Controls.RunnerInvocationCount;

            await using var scope = factory.Services.CreateAsyncScope();
            var validation = await scope.ServiceProvider.GetRequiredService<IValidationLabService>()
                .RunValidationAsync(id);
            Assert.False(validation.Succeeded);
            Assert.Equal(runnerCountBeforeValidation, factory.Controls.RunnerInvocationCount);

            var experiment = await ReloadExperimentAsync(scope, id);
            Assert.NotEqual(ValidationRevealStatus.Revealed, experiment.ValidationRevealStatus);
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
    public async Task ValidationStart_HistoricalNotEvaluated_IsBlocked()
    {
        await using var factory = new E2C2OrchestrationFactory();
        factory.Controls.AllowNonTrainingRuns = true;
        long? experimentId = null;

        try
        {
            var (id, combo) = await E2C2ExperimentFactory.CreatePreparedSingleTrialExperimentAsync(
                factory, "e2c3-hist-not-eval");
            experimentId = id;

            await using (var seedScope = factory.Services.CreateAsyncScope())
            {
                await SeedTrainingCompletedHistoricalNotEvaluatedAsync(seedScope.ServiceProvider, id, combo);
            }

            await using var scope = factory.Services.CreateAsyncScope();
            var lab = scope.ServiceProvider.GetRequiredService<IValidationLabService>();
            var freeze = await lab.FreezeAsync(id);
            Assert.False(freeze.Succeeded);

            var validation = await lab.RunValidationAsync(id);
            Assert.False(validation.Succeeded);
            Assert.Equal(0, factory.Controls.RunnerInvocationCount);

            var experiment = await ReloadExperimentAsync(scope, id);
            Assert.False(experiment.IsQualificationCapable);
            Assert.NotEqual(ValidationRevealStatus.Revealed, experiment.ValidationRevealStatus);
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
    public async Task Verdict_AuditCorruptedDuringValidation_CannotPassOrReveal()
    {
        await using var factory = new E2C2OrchestrationFactory();
        factory.Controls.AllowNonTrainingRuns = true;
        factory.Controls.NonTrainingAuditCorruption = E2C2SeamControls.AuditCorruptionMode.DeleteAccessRows;
        long? experimentId = null;

        try
        {
            var (id, _) = await E2C2ExperimentFactory.CreatePreparedSingleTrialExperimentAsync(
                factory, "e2c3-verdict-corrupt");
            experimentId = id;
            factory.Controls.CorruptAuthoritativeAuditAfterNonTrainingRunForExperimentId = id;

            await RunSuccessfulTrainingAsync(factory, id);

            await using (var freezeScope = factory.Services.CreateAsyncScope())
            {
                var freeze = await freezeScope.ServiceProvider.GetRequiredService<IValidationLabService>()
                    .FreezeAsync(id);
                Assert.True(freeze.Succeeded, freeze.ErrorMessage ?? "Freeze failed.");
            }

            var runnerCountBeforeValidation = factory.Controls.RunnerInvocationCount;

            await using var scope = factory.Services.CreateAsyncScope();
            var validation = await scope.ServiceProvider.GetRequiredService<IValidationLabService>()
                .RunValidationAsync(id);
            Assert.False(validation.Succeeded);
            Assert.True(factory.Controls.RunnerInvocationCount > runnerCountBeforeValidation);

            var experiment = await ReloadExperimentAsync(scope, id);
            Assert.NotEqual(StrategyRobustnessDecision.Passed, experiment.StrategyRobustnessDecision);
            Assert.NotEqual(ValidationRevealStatus.Revealed, experiment.ValidationRevealStatus);
            Assert.False(experiment.IsQualificationCapable);
            AssertStructuredAuditDurabilityRecords(experiment);
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
    public async Task Verdict_CannotOverwriteCanonicalFailureAggregate()
    {
        await using var factory = new E2C2OrchestrationFactory();
        factory.Controls.AllowNonTrainingRuns = true;
        long? experimentId = null;

        try
        {
            var (id, _) = await E2C2ExperimentFactory.CreatePreparedSingleTrialExperimentAsync(
                factory, "e2c3-verdict-aggregate");
            experimentId = id;

            await RunSuccessfulTrainingAsync(factory, id);

            await using (var freezeScope = factory.Services.CreateAsyncScope())
            {
                var freeze = await freezeScope.ServiceProvider.GetRequiredService<IValidationLabService>()
                    .FreezeAsync(id);
                Assert.True(freeze.Succeeded, freeze.ErrorMessage ?? "Freeze failed.");
            }

            const string seededCode = ValidationTrainingFailureCodes.ValidationAccessAuditPersistenceFailed;
            await using (var seedScope = factory.Services.CreateAsyncScope())
            {
                var experiments = seedScope.ServiceProvider.GetRequiredService<IValidationExperimentRepository>();
                var seeded = await experiments.GetByIdAsync(id)
                    ?? throw new InvalidOperationException("Experiment missing.");
                var prior = ValidationTrainingFailurePersistence.MergeExisting(seeded.FailureReasonsJson);
                prior.Observe(new ValidationTrainingFailureRecord
                {
                    Code = seededCode,
                    Category = ValidationTrainingFailureCategory.AuditDurability,
                    Precedence = ValidationTrainingFailurePrecedence.AuditDurability,
                    Phase = ValidationTrainingFailurePhase.CompletenessVerification,
                    UserSafeMessage = ValidationTrainingFailureHandler.UserSafeAuditPersistenceMessage,
                    OccurredAtUtc = DateTime.UtcNow.AddMinutes(-5),
                    IsQualificationBlocking = true
                });
                ValidationTrainingFailurePersistence.ApplyToExperiment(seeded, prior);
                // Keep frozen + qualification-capable so validation can enter the runner, then corrupt.
                seeded.IsQualificationCapable = true;
                await experiments.UpdateAsync(seeded);
            }

            factory.Controls.CorruptAuthoritativeAuditAfterNonTrainingRunForExperimentId = id;

            await using var scope = factory.Services.CreateAsyncScope();
            _ = await scope.ServiceProvider.GetRequiredService<IValidationLabService>().RunValidationAsync(id);

            var experiment = await ReloadExperimentAsync(scope, id);
            var records = E2C2FailureReasonHelpers.ParseRecords(experiment.FailureReasonsJson);
            Assert.All(records, r => Assert.False(string.IsNullOrWhiteSpace(r.Code)));
            Assert.All(records, r => Assert.NotEqual(default, r.Category));
            Assert.Contains(records, r => r.Code == seededCode);
            Assert.DoesNotContain(
                experiment.FailureReasonsJson ?? string.Empty,
                "FailedPerformanceCollapse",
                StringComparison.Ordinal);
            E2C2FailureReasonHelpers.AssertNoMirroredDiagnosticDuplicates(experiment);
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
    public async Task QualificationFailureReasons_DoNotReplaceInfrastructureReasons()
    {
        await using var factory = new E2C2OrchestrationFactory();
        factory.Controls.AllowNonTrainingRuns = true;
        long? experimentId = null;

        try
        {
            var (id, _) = await E2C2ExperimentFactory.CreatePreparedSingleTrialExperimentAsync(
                factory, "e2c3-qual-infra-reasons");
            experimentId = id;
            factory.Controls.CorruptAuthoritativeAuditAfterNonTrainingRunForExperimentId = id;

            await RunSuccessfulTrainingAsync(factory, id);

            await using (var freezeScope = factory.Services.CreateAsyncScope())
            {
                var freeze = await freezeScope.ServiceProvider.GetRequiredService<IValidationLabService>()
                    .FreezeAsync(id);
                Assert.True(freeze.Succeeded, freeze.ErrorMessage ?? "Freeze failed.");
            }

            await using var scope = factory.Services.CreateAsyncScope();
            _ = await scope.ServiceProvider.GetRequiredService<IValidationLabService>().RunValidationAsync(id);

            var experiment = await ReloadExperimentAsync(scope, id);
            var records = E2C2FailureReasonHelpers.ParseRecords(experiment.FailureReasonsJson);
            Assert.NotEmpty(records);
            Assert.All(records, r =>
            {
                Assert.False(string.IsNullOrWhiteSpace(r.Code));
                Assert.NotEqual(default(ValidationTrainingFailureCategory), r.Category);
                Assert.NotEqual(default(ValidationTrainingFailurePhase), r.Phase);
            });
            Assert.Contains(
                records,
                r => r.Category == ValidationTrainingFailureCategory.AuditDurability
                     && r.Code == ValidationTrainingFailureCodes.ValidationAccessAuditPersistenceFailed);
            Assert.NotEqual(StrategyRobustnessDecision.Passed, experiment.StrategyRobustnessDecision);
            Assert.NotEqual(ValidationRevealStatus.Revealed, experiment.ValidationRevealStatus);
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
    public async Task MixedAuthoritativeAndSupersededRows_UsesOnlyAuthoritativePositiveEvidence()
    {
        await using var factory = new E2C2OrchestrationFactory();
        long? experimentId = null;

        try
        {
            var (id, _) = await E2C2ExperimentFactory.CreatePreparedSingleTrialExperimentAsync(
                factory, "e2c3-mixed-rows");
            experimentId = id;
            await RunSuccessfulTrainingAsync(factory, id);

            await using (var seedScope = factory.Services.CreateAsyncScope())
            {
                var sp = seedScope.ServiceProvider;
                var trial = (await sp.GetRequiredService<IValidationParameterTrialRepository>()
                    .GetByExperimentIdAsync(id)).Single();
                var experiment = await sp.GetRequiredService<IValidationExperimentRepository>().GetByIdAsync(id)
                    ?? throw new InvalidOperationException("Experiment missing.");
                var execRepo = sp.GetRequiredService<IValidationAuditExecutionRepository>();
                var execution = await execRepo.GetByAuditExecutionIdAsync(
                    trial.AuthoritativeAuditExecutionId!.Value)
                    ?? throw new InvalidOperationException("Execution missing.");

                var foreignScope = Guid.NewGuid();
                var foreignExecution = E2C1AuditFixtures.NewExecution(experiment, trial, attempt: 2);
                foreignExecution.ScopeExecutionId = foreignScope;
                foreignExecution.Status = ValidationAuditExecutionStatus.Superseded;
                foreignExecution.SupersededAtUtc = DateTime.UtcNow;
                foreignExecution.SupersededByAuditExecutionId = execution.AuditExecutionId;
                await execRepo.AddAsync(foreignExecution);

                var foreignAudit = E2BAuditFixtures.NewAudit(
                    id, Guid.NewGuid(), foreignScope, 1, "E2C3-Foreign");
                foreignAudit.WasDenied = false;
                await sp.GetRequiredService<IValidationCandleAccessAuditRepository>()
                    .AddRangeIdempotentByAccessEventIdAsync([foreignAudit]);
            }

            await using var scope = factory.Services.CreateAsyncScope();
            var freeze = await scope.ServiceProvider.GetRequiredService<IValidationLabService>().FreezeAsync(id);
            Assert.True(freeze.Succeeded, freeze.ErrorMessage ?? "Freeze should succeed with authoritative evidence only.");

            var reloaded = await ReloadExperimentAsync(scope, id);
            Assert.True(reloaded.IsQualificationCapable);
            Assert.NotNull(reloaded.FrozenAtUtc);
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
    public async Task DeniedEvidenceFromOldAttempt_RemainsQualificationBlocking()
    {
        await using var factory = new E2C2OrchestrationFactory();
        long? experimentId = null;

        try
        {
            var (id, combo) = await E2C2ExperimentFactory.CreatePreparedSingleTrialExperimentAsync(
                factory, "e2c3-denied-old");
            experimentId = id;

            await using (var seedScope = factory.Services.CreateAsyncScope())
            {
                await SeedCompletedAuditBundleAsync(seedScope.ServiceProvider, id, combo, "denied-old");

                var sp = seedScope.ServiceProvider;
                var oldScope = Guid.NewGuid();
                var denied = E2BAuditFixtures.NewAudit(id, Guid.NewGuid(), oldScope, 1, "E2C3-DeniedOld", wasDenied: true);
                denied.DenialCode = "ValidationDataLeakageDetected";
                denied.DenialReason = "boundary denied on old attempt";
                await sp.GetRequiredService<IValidationCandleAccessAuditRepository>()
                    .AddRangeIdempotentByAccessEventIdAsync([denied]);

                var experiments = sp.GetRequiredService<IValidationExperimentRepository>();
                var seededExperiment = await experiments.GetByIdAsync(id)
                    ?? throw new InvalidOperationException("Experiment missing.");
                var trial = (await sp.GetRequiredService<IValidationParameterTrialRepository>()
                    .GetByExperimentIdAsync(id)).Single();
                seededExperiment.Status = ValidationExperimentStatus.TrainingCompleted;
                seededExperiment.CurrentStage = "TrainingCompleted";
                seededExperiment.SelectedTrialId = trial.Id;
                seededExperiment.SelectedTrialNumber = trial.TrialNumber;
                seededExperiment.SelectedTrialParameterSnapshotJson = trial.ParameterSnapshotJson;
                seededExperiment.SelectedTrialParameterFingerprint = trial.ParameterFingerprint;
                seededExperiment.IsQualificationCapable = true;
                seededExperiment.LeakageAuditStatus = ValidationLeakageAuditStatus.NotAvailable;
                await experiments.UpdateAsync(seededExperiment);
            }

            await using var scope = factory.Services.CreateAsyncScope();
            var freeze = await scope.ServiceProvider.GetRequiredService<IValidationLabService>().FreezeAsync(id);
            Assert.False(freeze.Succeeded);

            var reloaded = await ReloadExperimentAsync(scope, id);
            Assert.False(reloaded.IsQualificationCapable);
            Assert.Equal(ValidationLeakageAuditStatus.Failed, reloaded.LeakageAuditStatus);
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
    public async Task DurableReload_FreezeAndValidationRemainBlocked()
    {
        long? experimentId = null;

        await using (var factory = new E2C2OrchestrationFactory())
        {
            var (id, _) = await E2C2ExperimentFactory.CreatePreparedSingleTrialExperimentAsync(
                factory, "e2c3-durable-reload");
            experimentId = id;
            await RunSuccessfulTrainingAsync(factory, id);

            await using (var corruptScope = factory.Services.CreateAsyncScope())
            {
                var db = corruptScope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
                await db.ValidationCandleAccessAudits
                    .Where(a => a.ValidationExperimentId == id)
                    .ExecuteDeleteAsync();
            }
        }

        Assert.NotNull(experimentId);

        try
        {
            await using var reloadFactory = new E2C2OrchestrationFactory();
            reloadFactory.Controls.AllowNonTrainingRuns = true;

            await using (var freezeScope = reloadFactory.Services.CreateAsyncScope())
            {
                var freeze = await freezeScope.ServiceProvider.GetRequiredService<IValidationLabService>()
                    .FreezeAsync(experimentId.Value);
                Assert.False(freeze.Succeeded);
            }

            await using (var assertScope = reloadFactory.Services.CreateAsyncScope())
            {
                var experiment = await ReloadExperimentAsync(assertScope, experimentId.Value);
                Assert.False(experiment.IsQualificationCapable);
                AssertStructuredAuditDurabilityRecords(experiment);

                var validation = await assertScope.ServiceProvider.GetRequiredService<IValidationLabService>()
                    .RunValidationAsync(experimentId.Value);
                Assert.False(validation.Succeeded);
                Assert.Equal(0, reloadFactory.Controls.RunnerInvocationCount);
            }
        }
        finally
        {
            await using var cleanupFactory = new E2C2OrchestrationFactory();
            await E2C2ExperimentFactory.CleanupExperimentAsync(cleanupFactory, experimentId.Value);
        }
    }

    [Fact]
    public async Task ValidEndToEnd_AuthoritativeCompleteAudit_CanFreezeValidateAndQualify()
    {
        await using var factory = new E2C2OrchestrationFactory();
        factory.Controls.AllowNonTrainingRuns = true;
        long? experimentId = null;

        try
        {
            var (id, _) = await E2C2ExperimentFactory.CreatePreparedSingleTrialExperimentAsync(
                factory, "e2c3-e2e");
            experimentId = id;

            var trained = await RunSuccessfulTrainingAsync(factory, id);
            Assert.True(trained.IsQualificationCapable);

            await using var scope = factory.Services.CreateAsyncScope();
            var lab = scope.ServiceProvider.GetRequiredService<IValidationLabService>();

            var freeze = await lab.FreezeAsync(id);
            Assert.True(freeze.Succeeded, freeze.ErrorMessage ?? "Freeze failed.");

            var afterFreeze = await ReloadExperimentAsync(scope, id);
            Assert.Equal(ValidationExperimentStatus.ConfigurationFrozen, afterFreeze.Status);
            Assert.Equal(ValidationRevealStatus.Frozen, afterFreeze.ValidationRevealStatus);
            Assert.NotNull(afterFreeze.FrozenAtUtc);

            var validation = await lab.RunValidationAsync(id);
            Assert.True(validation.Succeeded, validation.ErrorMessage ?? "Validation failed.");
            Assert.True(factory.Controls.RunnerInvocationCount >= 2);

            var completed = await ReloadExperimentAsync(scope, id);
            Assert.True(completed.IsQualificationCapable);
            Assert.Equal(ValidationRevealStatus.Revealed, completed.ValidationRevealStatus);
            Assert.Equal(ValidationExperimentStatus.Completed, completed.Status);
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
    public async Task InfrastructureFallback_NeverBecomesQualificationCapable()
    {
        await using var factory = new E2C2OrchestrationFactory();
        factory.Controls.RunnerMode = E2C2RunnerMode.AllowedComplete;
        long? experimentId = null;

        try
        {
            var (id, _) = await E2C2ExperimentFactory.CreatePreparedSingleTrialExperimentAsync(
                factory, "e2c3-infra-fallback");
            experimentId = id;

            await using (var seedScope = factory.Services.CreateAsyncScope())
            {
                var experiments = seedScope.ServiceProvider.GetRequiredService<IValidationExperimentRepository>();
                var seededExperiment = await experiments.GetByIdAsync(id)
                    ?? throw new InvalidOperationException("Experiment missing.");
                seededExperiment.AllowInfrastructureOnlyRejectedTrialFallback = true;
                var strictProfile = new ValidationQualificationProfileDto
                {
                    MinimumTrainingClosedTrades = 100,
                    MinimumTrainingProfitFactor = 0m,
                    MinimumTrainingNetExpectancyR = -999m,
                    MaximumTrainingDrawdownPercent = 100m
                };
                seededExperiment.QualificationProfileSnapshotJson = JsonSerializer.Serialize(strictProfile);
                using var draftDoc = JsonDocument.Parse(seededExperiment.DraftConfigurationJson ?? "{}");
                var draftMap = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
                    seededExperiment.DraftConfigurationJson ?? "{}")
                    ?? new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
                draftMap["qualificationProfile"] = JsonSerializer.SerializeToElement(strictProfile);
                seededExperiment.DraftConfigurationJson = JsonSerializer.Serialize(draftMap);
                await experiments.UpdateAsync(seededExperiment);
            }

            await using var scope = factory.Services.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<IValidationLabService>()
                .RunTrainingAsync(id);
            Assert.True(result.Succeeded, result.ErrorMessage ?? "Training with fallback failed.");

            var reloaded = await ReloadExperimentAsync(scope, id);
            Assert.Equal(ValidationSelectionIntegrityStatus.InfrastructureOnlyFallback,
                reloaded.SelectionIntegrityStatus);
            Assert.False(reloaded.IsQualificationCapable);
            Assert.NotEqual(ValidationExperimentStatus.Failed, reloaded.Status);

            var freeze = await scope.ServiceProvider.GetRequiredService<IValidationLabService>().FreezeAsync(id);
            Assert.False(freeze.Succeeded);
        }
        finally
        {
            if (experimentId is long eid)
            {
                await E2C2ExperimentFactory.CleanupExperimentAsync(factory, eid);
            }
        }
    }

    private static async Task<ValidationExperiment> RunSuccessfulTrainingAsync(
        E2C2OrchestrationFactory factory,
        long id)
    {
        factory.Controls.RunnerMode = E2C2RunnerMode.AllowedComplete;
        await using var scope = factory.Services.CreateAsyncScope();
        var lab = scope.ServiceProvider.GetRequiredService<IValidationLabService>();
        var result = await lab.RunTrainingAsync(id);
        Assert.True(result.Succeeded, result.ErrorMessage ?? "Training failed.");
        var experiment = await ReloadExperimentAsync(scope, id);
        Assert.Equal(ValidationExperimentStatus.TrainingCompleted, experiment.Status);
        return experiment;
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
            trial.Status = ValidationTrialStatus.Completed;
            trial.GuardrailDecision = "Passed";
            trial.TrialRankEligibility = ValidationTrialRankEligibility.Eligible;
            trial.Rank = 1;
            trial.AuditCompletionStatus = ValidationAuditCompletionStatus.Complete;
            await trials.UpdateAsync(trial);
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

    private static async Task SeedTrainingCompletedHistoricalNotEvaluatedAsync(
        IServiceProvider sp,
        long experimentId,
        IReadOnlyDictionary<string, string> combo)
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
            Status = ValidationTrialStatus.Completed,
            GuardrailDecision = "Passed",
            TrialRankEligibility = ValidationTrialRankEligibility.Eligible,
            Rank = 1,
            TrainingScore = 1.25m,
            StrategyLabRunId = 1,
            StartedAtUtc = DateTime.UtcNow.AddHours(-1),
            CompletedAtUtc = DateTime.UtcNow,
            AuthoritativeAuditExecutionId = null,
            AuditCompletionStatus = ValidationAuditCompletionStatus.NotEvaluated
        });

        var trial = await trials.GetByExperimentAndFingerprintAsync(experimentId, fingerprint)
            ?? throw new InvalidOperationException("Trial missing.");
        var experiment = await experiments.GetByIdAsync(experimentId)
            ?? throw new InvalidOperationException("Experiment missing.");

        experiment.Status = ValidationExperimentStatus.TrainingCompleted;
        experiment.CurrentStage = "TrainingCompleted";
        experiment.SelectedTrialId = trial.Id;
        experiment.SelectedTrialNumber = trial.TrialNumber;
        experiment.SelectedTrialParameterSnapshotJson = trial.ParameterSnapshotJson;
        experiment.SelectedTrialParameterFingerprint = trial.ParameterFingerprint;
        experiment.IsQualificationCapable = true;
        await experiments.UpdateAsync(experiment);
    }

    private static async Task SupersedeExecutionInDatabaseAsync(IServiceProvider sp, Guid auditExecutionId)
    {
        var db = sp.GetRequiredService<MomoQuantDbContext>();
        var execution = await db.ValidationAuditExecutions
            .FirstAsync(e => e.AuditExecutionId == auditExecutionId);
        execution.Status = ValidationAuditExecutionStatus.Superseded;
        execution.SupersededAtUtc = DateTime.UtcNow;
        execution.SupersededByAuditExecutionId = Guid.NewGuid();
        execution.RecoveryStatus = ValidationAuditRecoveryStatus.SupersededForRerun;
        execution.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    private static void AssertStructuredAuditDurabilityRecords(ValidationExperiment experiment)
    {
        var records = E2C2FailureReasonHelpers.ParseRecords(experiment.FailureReasonsJson);
        Assert.NotEmpty(records);
        Assert.All(records, r =>
        {
            Assert.False(string.IsNullOrWhiteSpace(r.Code));
            Assert.Equal(ValidationTrainingFailureCategory.AuditDurability, r.Category);
        });
        E2C2FailureReasonHelpers.AssertNoMirroredDiagnosticDuplicates(experiment);
    }

    private static async Task<ValidationExperiment> ReloadExperimentAsync(IServiceScope scope, long id)
    {
        var experiment = await scope.ServiceProvider.GetRequiredService<IValidationExperimentRepository>()
            .GetByIdAsync(id);
        Assert.NotNull(experiment);
        return experiment!;
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
