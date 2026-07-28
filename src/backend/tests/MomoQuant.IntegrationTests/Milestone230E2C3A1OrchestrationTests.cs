using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.ValidationLab;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.ValidationLab;
using MomoQuant.Persistence;

namespace MomoQuant.IntegrationTests;

/// <summary>
/// Milestone 23.0E2C3A1 — repository-read safety, negative-evidence-first ordering,
/// and post-runner verdict-time denial on production paths.
/// </summary>
[Collection("Integration")]
public sealed class Milestone230E2C3A1OrchestrationTests
{
    [Fact]
    public async Task TrainingFinalize_TrialPopulationRepositoryThrows_ReturnsSafeAuditDurability()
    {
        await using var factory = new E2C2OrchestrationFactory();
        factory.Controls.RunnerMode = E2C2RunnerMode.AllowedComplete;
        factory.Controls.FailTrialPopulationGetOnNthAfterFinalizer = 3;
        long? experimentId = null;

        try
        {
            var (id, _) = await E2C2ExperimentFactory.CreatePreparedSingleTrialExperimentAsync(
                factory, "e2c3a1-pop-throw");
            experimentId = id;

            await using var scope = factory.Services.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<IValidationLabService>()
                .RunTrainingAsync(id);
            Assert.False(result.Succeeded);

            var experiment = await ReloadExperimentAsync(scope, id);
            AssertAuditDurability(experiment);
            Assert.NotEqual(ValidationExperimentStatus.TrainingCompleted, experiment.Status);
            E2C2FailureReasonHelpers.AssertNoSensitiveMessages(experiment, result.ErrorMessage);
            Assert.DoesNotContain("repository", result.ErrorMessage ?? string.Empty, StringComparison.OrdinalIgnoreCase);
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
    public async Task TrainingFinalize_SelectedTrialReloadRepositoryThrows_ReturnsSafeAuditDurability()
    {
        await using var factory = new E2C2OrchestrationFactory();
        factory.Controls.RunnerMode = E2C2RunnerMode.AllowedComplete;
        factory.Controls.ArmTrialFingerprintGetFailureAfterFinalizer = true;
        long? experimentId = null;

        try
        {
            var (id, _) = await E2C2ExperimentFactory.CreatePreparedSingleTrialExperimentAsync(
                factory, "e2c3a1-fp-throw");
            experimentId = id;

            await using var scope = factory.Services.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<IValidationLabService>()
                .RunTrainingAsync(id);
            Assert.False(result.Succeeded);

            var experiment = await ReloadExperimentAsync(scope, id);
            AssertAuditDurability(experiment);
            Assert.NotEqual(ValidationExperimentStatus.TrainingCompleted, experiment.Status);
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
    public async Task TrainingFinalize_RepositoryFailure_ReleasesLeaseExactlyOnce()
    {
        await using var factory = new E2C2OrchestrationFactory();
        factory.Controls.RunnerMode = E2C2RunnerMode.AllowedComplete;
        factory.Controls.FailTrialPopulationGetOnNthAfterFinalizer = 3;
        long? experimentId = null;

        try
        {
            var (id, _) = await E2C2ExperimentFactory.CreatePreparedSingleTrialExperimentAsync(
                factory, "e2c3a1-lease-once");
            experimentId = id;

            await using var scope = factory.Services.CreateAsyncScope();
            _ = await scope.ServiceProvider.GetRequiredService<IValidationLabService>().RunTrainingAsync(id);

            Assert.Equal(1, factory.Controls.LeaseReleaseInvocationCount);

            var lease = scope.ServiceProvider.GetRequiredService<IValidationTrainingExecutionLeaseService>();
            Assert.False(await lease.IsActiveAsync(id));
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
    public async Task Freeze_TrialPopulationRepositoryThrows_ReturnsSafeAuditDurability()
    {
        await using var factory = new E2C2OrchestrationFactory();
        long? experimentId = null;

        try
        {
            var (id, combo) = await E2C2ExperimentFactory.CreatePreparedSingleTrialExperimentAsync(
                factory, "e2c3a1-freeze-pop");
            experimentId = id;
            await RunSuccessfulTrainingAsync(factory, id, combo);

            factory.Controls.ArmTrialPopulationGetFailure = true;

            await using var scope = factory.Services.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<IValidationLabService>().FreezeAsync(id);
            Assert.False(result.Succeeded);

            await using var assertScope = factory.Services.CreateAsyncScope();
            var experiment = await ReloadExperimentAsync(assertScope, id);
            AssertAuditDurability(experiment);
            Assert.NotEqual(ValidationExperimentStatus.ConfigurationFrozen, experiment.Status);
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
    public async Task Freeze_TrialPopulationRepositoryThrows_LeavesFrozenFieldsUnchanged()
    {
        await using var factory = new E2C2OrchestrationFactory();
        long? experimentId = null;

        try
        {
            var (id, combo) = await E2C2ExperimentFactory.CreatePreparedSingleTrialExperimentAsync(
                factory, "e2c3a1-freeze-fields");
            experimentId = id;
            await RunSuccessfulTrainingAsync(factory, id, combo);

            string? snap;
            string? fp;
            string? conf;
            string? risk;
            string? cost;
            string? stratFp;
            DateTime? frozenAt;
            string? freezeSource;
            ValidationRevealStatus reveal;
            await using (var beforeScope = factory.Services.CreateAsyncScope())
            {
                var before = await ReloadExperimentAsync(beforeScope, id);
                snap = before.FrozenStrategyParameterSnapshotJson;
                fp = before.FrozenParameterFingerprint;
                conf = before.FrozenConfidenceSnapshotJson;
                risk = before.FrozenRiskSnapshotJson;
                cost = before.FrozenCostModelSnapshotJson;
                stratFp = before.FrozenStrategyFingerprint;
                frozenAt = before.FrozenAtUtc;
                freezeSource = before.FreezeSource;
                reveal = before.ValidationRevealStatus;
            }

            factory.Controls.ArmTrialPopulationGetFailure = true;

            await using var scope = factory.Services.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<IValidationLabService>().FreezeAsync(id);
            Assert.False(result.Succeeded);

            await using var assertScope = factory.Services.CreateAsyncScope();
            var after = await ReloadExperimentAsync(assertScope, id);
            Assert.Equal(snap, after.FrozenStrategyParameterSnapshotJson);
            Assert.Equal(fp, after.FrozenParameterFingerprint);
            Assert.Equal(conf, after.FrozenConfidenceSnapshotJson);
            Assert.Equal(risk, after.FrozenRiskSnapshotJson);
            Assert.Equal(cost, after.FrozenCostModelSnapshotJson);
            Assert.Equal(stratFp, after.FrozenStrategyFingerprint);
            Assert.Equal(frozenAt, after.FrozenAtUtc);
            Assert.Equal(freezeSource, after.FreezeSource);
            Assert.Equal(reveal, after.ValidationRevealStatus);
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
    public async Task ValidationStart_TrialPopulationRepositoryThrows_DoesNotInvokeRunner()
    {
        await using var factory = new E2C2OrchestrationFactory();
        long? experimentId = null;

        try
        {
            var (id, combo) = await E2C2ExperimentFactory.CreatePreparedSingleTrialExperimentAsync(
                factory, "e2c3a1-val-pop");
            experimentId = id;
            await RunSuccessfulTrainingAndFreezeAsync(factory, id, combo);

            factory.Controls.ArmTrialPopulationGetFailure = true;
            var runnerBefore = factory.Controls.RunnerInvocationCount;

            await using var scope = factory.Services.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<IValidationLabService>()
                .RunValidationAsync(id);
            Assert.False(result.Succeeded);
            Assert.Equal(runnerBefore, factory.Controls.RunnerInvocationCount);

            await using var assertScope = factory.Services.CreateAsyncScope();
            var experiment = await ReloadExperimentAsync(assertScope, id);
            AssertAuditDurability(experiment);
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
    public async Task ValidationStart_TrialPopulationRepositoryThrows_RemainsUnrevealed()
    {
        await using var factory = new E2C2OrchestrationFactory();
        long? experimentId = null;

        try
        {
            var (id, combo) = await E2C2ExperimentFactory.CreatePreparedSingleTrialExperimentAsync(
                factory, "e2c3a1-val-unreveal");
            experimentId = id;
            await RunSuccessfulTrainingAndFreezeAsync(factory, id, combo);

            factory.Controls.ArmTrialPopulationGetFailure = true;

            await using var scope = factory.Services.CreateAsyncScope();
            _ = await scope.ServiceProvider.GetRequiredService<IValidationLabService>().RunValidationAsync(id);

            await using var assertScope = factory.Services.CreateAsyncScope();
            var experiment = await ReloadExperimentAsync(assertScope, id);
            Assert.NotEqual(ValidationRevealStatus.Revealed, experiment.ValidationRevealStatus);
            Assert.Null(experiment.ValidationRevealedAtUtc);
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
    public async Task Verdict_SelectedTrialRepositoryThrows_CannotPassOrReveal()
    {
        await using var factory = new E2C2OrchestrationFactory();
        factory.Controls.AllowNonTrainingRuns = true;
        long? experimentId = null;

        try
        {
            var (id, combo) = await E2C2ExperimentFactory.CreatePreparedSingleTrialExperimentAsync(
                factory, "e2c3a1-verdict-pop");
            experimentId = id;
            await RunSuccessfulTrainingAndFreezeAsync(factory, id, combo);

            factory.Controls.ArmTrialPopulationGetFailureAfterNonTrainingRun = true;
            var runnerBefore = factory.Controls.RunnerInvocationCount;

            await using var scope = factory.Services.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<IValidationLabService>()
                .RunValidationAsync(id);
            Assert.False(result.Succeeded);
            Assert.Equal(runnerBefore + 1, factory.Controls.RunnerInvocationCount);

            await using var assertScope = factory.Services.CreateAsyncScope();
            var experiment = await ReloadExperimentAsync(assertScope, id);
            AssertAuditDurability(experiment);
            Assert.NotEqual(ValidationRevealStatus.Revealed, experiment.ValidationRevealStatus);
            Assert.Null(experiment.ValidationRevealedAtUtc);
            Assert.NotEqual(StrategyRobustnessDecision.Passed, experiment.StrategyRobustnessDecision);
            Assert.NotEqual(ValidationExperimentStatus.Completed, experiment.Status);
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
    public async Task RecalculateVerdict_TrialPopulationRepositoryThrows_CannotRestorePassed()
    {
        await using var factory = new E2C2OrchestrationFactory();
        factory.Controls.AllowNonTrainingRuns = true;
        long? experimentId = null;

        try
        {
            var (id, combo) = await E2C2ExperimentFactory.CreatePreparedSingleTrialExperimentAsync(
                factory, "e2c3a1-recalc-pop");
            experimentId = id;
            await RunSuccessfulTrainingAndFreezeAsync(factory, id, combo);

            await using (var runScope = factory.Services.CreateAsyncScope())
            {
                var validation = await runScope.ServiceProvider.GetRequiredService<IValidationLabService>()
                    .RunValidationAsync(id);
                Assert.True(validation.Succeeded, validation.ErrorMessage ?? "Validation failed.");
            }

            await using (var seedScope = factory.Services.CreateAsyncScope())
            {
                var experiments = seedScope.ServiceProvider.GetRequiredService<IValidationExperimentRepository>();
                var seeded = await experiments.GetByIdAsync(id)
                    ?? throw new InvalidOperationException("Experiment missing.");
                seeded.StrategyRobustnessDecision = StrategyRobustnessDecision.FailedPerformanceCollapse;
                seeded.IsQualificationCapable = true;
                await experiments.UpdateAsync(seeded);
            }

            factory.Controls.ArmTrialPopulationGetFailure = true;

            await using var scope = factory.Services.CreateAsyncScope();
            var recalc = await scope.ServiceProvider.GetRequiredService<IValidationLabService>()
                .RecalculateVerdictAsync(id);
            Assert.False(recalc.Succeeded);

            await using var assertScope = factory.Services.CreateAsyncScope();
            var experiment = await ReloadExperimentAsync(assertScope, id);
            AssertAuditDurability(experiment);
            Assert.NotEqual(StrategyRobustnessDecision.Passed, experiment.StrategyRobustnessDecision);
            E2C2FailureReasonHelpers.AssertNoSensitiveMessages(experiment, recalc.ErrorMessage);
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
    public async Task TrainingFinalize_NegativeEvidenceWithPopulationFailure_BoundaryRemainsPrimary()
    {
        await using var factory = new E2C2OrchestrationFactory();
        factory.Controls.RunnerMode = E2C2RunnerMode.AllowedComplete;
        factory.Controls.FailTrialPopulationGetOnNthAfterFinalizer = 3;
        long? experimentId = null;

        try
        {
            var (id, _) = await E2C2ExperimentFactory.CreatePreparedSingleTrialExperimentAsync(
                factory, "e2c3a1-neg-pop");
            experimentId = id;

            await using (var seedScope = factory.Services.CreateAsyncScope())
            {
                await AddDeniedForeignAuditAsync(seedScope.ServiceProvider, id, "E2C3A1-NegPop");
            }

            await using var scope = factory.Services.CreateAsyncScope();
            _ = await scope.ServiceProvider.GetRequiredService<IValidationLabService>().RunTrainingAsync(id);

            var experiment = await ReloadExperimentAsync(scope, id);
            AssertBoundaryPrimary(experiment);
            Assert.NotEqual(ValidationExperimentStatus.TrainingCompleted, experiment.Status);
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
    public async Task ValidationStart_NegativeEvidenceWithSelectionFailure_BoundaryRemainsPrimary()
    {
        await using var factory = new E2C2OrchestrationFactory();
        long? experimentId = null;

        try
        {
            var (id, combo) = await E2C2ExperimentFactory.CreatePreparedSingleTrialExperimentAsync(
                factory, "e2c3a1-neg-sel");
            experimentId = id;
            await RunSuccessfulTrainingAndFreezeAsync(factory, id, combo);

            await using (var seedScope = factory.Services.CreateAsyncScope())
            {
                var experiments = seedScope.ServiceProvider.GetRequiredService<IValidationExperimentRepository>();
                var seeded = await experiments.GetByIdAsync(id)
                    ?? throw new InvalidOperationException("Experiment missing.");
                seeded.SelectedTrialId = null;
                seeded.SelectedTrialParameterFingerprint = null;
                await experiments.UpdateAsync(seeded);
                await AddDeniedForeignAuditAsync(seedScope.ServiceProvider, id, "E2C3A1-NegSel");
            }

            await using var scope = factory.Services.CreateAsyncScope();
            _ = await scope.ServiceProvider.GetRequiredService<IValidationLabService>().RunValidationAsync(id);

            await using var assertScope = factory.Services.CreateAsyncScope();
            var experiment = await ReloadExperimentAsync(assertScope, id);
            AssertBoundaryPrimary(experiment);
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
    public async Task ValidationStart_NegativeEvidenceWhenNonQualificationCapable_BoundaryRemainsPrimary()
    {
        await using var factory = new E2C2OrchestrationFactory();
        long? experimentId = null;

        try
        {
            var (id, combo) = await E2C2ExperimentFactory.CreatePreparedSingleTrialExperimentAsync(
                factory, "e2c3a1-neg-noncap");
            experimentId = id;
            await RunSuccessfulTrainingAndFreezeAsync(factory, id, combo);

            await using (var seedScope = factory.Services.CreateAsyncScope())
            {
                var experiments = seedScope.ServiceProvider.GetRequiredService<IValidationExperimentRepository>();
                var seeded = await experiments.GetByIdAsync(id)
                    ?? throw new InvalidOperationException("Experiment missing.");
                seeded.IsQualificationCapable = false;
                await experiments.UpdateAsync(seeded);
                await AddDeniedForeignAuditAsync(seedScope.ServiceProvider, id, "E2C3A1-NegNonCap");
            }

            await using var scope = factory.Services.CreateAsyncScope();
            _ = await scope.ServiceProvider.GetRequiredService<IValidationLabService>().RunValidationAsync(id);

            await using var assertScope = factory.Services.CreateAsyncScope();
            var experiment = await ReloadExperimentAsync(assertScope, id);
            AssertBoundaryPrimary(experiment);
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
    public async Task RecalculateVerdict_NegativeEvidenceWithExistingAuditFailure_BoundaryRemainsPrimary()
    {
        await using var factory = new E2C2OrchestrationFactory();
        factory.Controls.AllowNonTrainingRuns = true;
        long? experimentId = null;

        try
        {
            var (id, combo) = await E2C2ExperimentFactory.CreatePreparedSingleTrialExperimentAsync(
                factory, "e2c3a1-recalc-neg-audit");
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
                var prior = ValidationTrainingFailurePersistence.MergeExisting(seeded.FailureReasonsJson);
                prior.Observe(new ValidationTrainingFailureRecord
                {
                    Code = ValidationTrainingFailureCodes.ValidationAccessAuditPersistenceFailed,
                    Category = ValidationTrainingFailureCategory.AuditDurability,
                    Precedence = ValidationTrainingFailurePrecedence.AuditDurability,
                    Phase = ValidationTrainingFailurePhase.AuditFinalization,
                    UserSafeMessage = ValidationTrainingFailureHandler.UserSafeAuditPersistenceMessage,
                    OccurredAtUtc = DateTime.UtcNow.AddMinutes(-5),
                    IsQualificationBlocking = true
                });
                ValidationTrainingFailurePersistence.ApplyToExperiment(seeded, prior);
                seeded.IsQualificationCapable = true;
                await experiments.UpdateAsync(seeded);
                await AddDeniedForeignAuditAsync(seedScope.ServiceProvider, id, "E2C3A1-RecalcNegAudit");
            }

            await using var scope = factory.Services.CreateAsyncScope();
            _ = await scope.ServiceProvider.GetRequiredService<IValidationLabService>().RecalculateVerdictAsync(id);

            await using var assertScope = factory.Services.CreateAsyncScope();
            var experiment = await ReloadExperimentAsync(assertScope, id);
            var records = E2C2FailureReasonHelpers.ParseRecords(experiment.FailureReasonsJson);
            Assert.True(records.Count >= 2);
            Assert.Equal(ValidationTrainingFailureCategory.Boundary, records[0].Category);
            Assert.Equal(ValidationTrainingFailureCodes.ValidationDataLeakage, records[0].Code);
            Assert.Contains(records, r =>
                r.Code == ValidationTrainingFailureCodes.ValidationAccessAuditPersistenceFailed);
            Assert.Equal(
                records.Count,
                records.Select(r => r.LogicalIdentity).Distinct(StringComparer.Ordinal).Count());
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
    public async Task RecalculateVerdict_NegativeEvidenceWhenNonQualificationCapable_BoundaryRemainsPrimary()
    {
        await using var factory = new E2C2OrchestrationFactory();
        factory.Controls.AllowNonTrainingRuns = true;
        long? experimentId = null;

        try
        {
            var (id, combo) = await E2C2ExperimentFactory.CreatePreparedSingleTrialExperimentAsync(
                factory, "e2c3a1-recalc-neg-noncap");
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
                await AddDeniedForeignAuditAsync(seedScope.ServiceProvider, id, "E2C3A1-RecalcNegNonCap");
            }

            await using var scope = factory.Services.CreateAsyncScope();
            _ = await scope.ServiceProvider.GetRequiredService<IValidationLabService>().RecalculateVerdictAsync(id);

            await using var assertScope = factory.Services.CreateAsyncScope();
            var experiment = await ReloadExperimentAsync(assertScope, id);
            AssertBoundaryPrimary(experiment);
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
    public async Task Verdict_DeniedEvidenceAddedDuringValidation_CannotPassOrReveal()
    {
        await using var factory = new E2C2OrchestrationFactory();
        factory.Controls.AllowNonTrainingRuns = true;
        long? experimentId = null;

        try
        {
            var (id, combo) = await E2C2ExperimentFactory.CreatePreparedSingleTrialExperimentAsync(
                factory, "e2c3a1-post-runner-denied");
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

            await using var assertScope = factory.Services.CreateAsyncScope();
            var experiment = await ReloadExperimentAsync(assertScope, id);
            AssertBoundaryPrimary(experiment);
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
    public async Task ValidateExistingFrozenConfiguration_GenuineNoTrainingArtifacts_CompletesAndReveals()
    {
        await using var factory = new E2C2OrchestrationFactory();
        factory.Controls.AllowNonTrainingRuns = true;
        long? experimentId = null;

        try
        {
            var id = await E2C2ExperimentFactory.CreateGenuineExistingFrozenExperimentAsync(
                factory, "e2c3a1-genuine-frozen");
            experimentId = id;

            await using (var preloadScope = factory.Services.CreateAsyncScope())
            {
                var experiment = await ReloadExperimentAsync(preloadScope, id);
                Assert.Equal(ValidationExperimentType.ValidateExistingFrozenConfiguration, experiment.ExperimentType);
                Assert.Equal(ValidationExperimentStatus.ConfigurationFrozen, experiment.Status);
                Assert.Null(experiment.SelectedTrialId);
                Assert.Equal(ValidationSelectionIntegrityStatus.NotEvaluated, experiment.SelectionIntegrityStatus);
                Assert.False(experiment.IsQualificationCapable);
                Assert.Empty(await preloadScope.ServiceProvider
                    .GetRequiredService<IValidationParameterTrialRepository>()
                    .GetByExperimentIdAsync(id));
            }

            factory.Controls.ArmTrialPopulationGetFailure = true;
            var runnerBefore = factory.Controls.RunnerInvocationCount;
            var trialGetsBefore = factory.Controls.TrialPopulationGetInvocationCount;
            var auditEvalBefore = factory.Controls.AuthoritativeAuditEvaluateTrialInvocationCount;

            await using (var runScope = factory.Services.CreateAsyncScope())
            {
                var validation = await runScope.ServiceProvider.GetRequiredService<IValidationLabService>()
                    .RunValidationAsync(id);
                Assert.True(validation.Succeeded, validation.ErrorMessage);
            }

            Assert.Equal(runnerBefore + 1, factory.Controls.RunnerInvocationCount);
            Assert.Equal(trialGetsBefore, factory.Controls.TrialPopulationGetInvocationCount);
            Assert.Equal(auditEvalBefore, factory.Controls.AuthoritativeAuditEvaluateTrialInvocationCount);

            await using var assertScope = factory.Services.CreateAsyncScope();
            var after = await ReloadExperimentAsync(assertScope, id);
            Assert.Equal(ValidationExperimentStatus.Completed, after.Status);
            Assert.Equal(ValidationRevealStatus.Revealed, after.ValidationRevealStatus);
            Assert.NotNull(after.ValidationRevealedAtUtc);
            Assert.False(after.IsQualificationCapable);
            Assert.Null(after.SelectedTrialId);
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

    private static async Task AddDeniedForeignAuditAsync(IServiceProvider sp, long experimentId, string suffix)
    {
        var denied = E2BAuditFixtures.NewAudit(
            experimentId, Guid.NewGuid(), Guid.NewGuid(), 1, suffix, wasDenied: true);
        denied.DenialCode = "ValidationDataLeakageDetected";
        denied.DenialReason = "foreign denied attempt";
        await sp.GetRequiredService<IValidationCandleAccessAuditRepository>()
            .AddRangeIdempotentByAccessEventIdAsync([denied]);
    }

    private static void AssertAuditDurability(ValidationExperiment experiment)
    {
        Assert.False(experiment.IsQualificationCapable);
        var records = E2C2FailureReasonHelpers.ParseRecords(experiment.FailureReasonsJson);
        Assert.NotEmpty(records);
        Assert.Equal(ValidationTrainingFailureCategory.AuditDurability, records[0].Category);
        Assert.Equal(ValidationTrainingFailureCodes.ValidationAccessAuditPersistenceFailed, records[0].Code);
        Assert.Equal(ValidationTrainingFailurePhase.AuditFinalization, records[0].Phase);
        E2C2FailureReasonHelpers.AssertNoMirroredDiagnosticDuplicates(experiment);
    }

    private static void AssertBoundaryPrimary(ValidationExperiment experiment)
    {
        Assert.False(experiment.IsQualificationCapable);
        var records = E2C2FailureReasonHelpers.ParseRecords(experiment.FailureReasonsJson);
        Assert.NotEmpty(records);
        Assert.Equal(ValidationTrainingFailureCategory.Boundary, records[0].Category);
        Assert.Equal(ValidationTrainingFailureCodes.ValidationDataLeakage, records[0].Code);
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
