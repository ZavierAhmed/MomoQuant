using Microsoft.Extensions.DependencyInjection;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Common;
using MomoQuant.Application.ValidationLab;
using MomoQuant.Application.ValidationLab.Dtos;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.ValidationLab;

namespace MomoQuant.IntegrationTests;

/// <summary>
/// Milestone 23.0E2C2 — production <see cref="IValidationLabService.RunTrainingAsync"/> failure aggregation
/// with simultaneous boundary/audit/cleanup faults.
/// </summary>
[Collection("Integration")]
public sealed class Milestone230E2C2OrchestrationTests
{
    [Fact]
    public async Task RunnerBoundaryFailure_AndRecorderFlushFailure_PersistsBothWithBoundaryPrimary()
    {
        await using var factory = new E2C2OrchestrationFactory();
        factory.Controls.RunnerMode = E2C2RunnerMode.AdversarialBoundary;
        factory.Controls.FailOnFlushNumbers.Add(1);
        long? experimentId = null;

        try
        {
            var (id, _) = await E2C2ExperimentFactory.CreatePreparedSingleTrialExperimentAsync(factory, "boundary-flush");
            experimentId = id;

            await using var scope = factory.Services.CreateAsyncScope();
            var lab = scope.ServiceProvider.GetRequiredService<IValidationLabService>();
            var result = await lab.RunTrainingAsync(id);

            Assert.False(result.Succeeded);
            Assert.Equal(ValidationTrainingFailureCodes.ValidationDataLeakage, result.ErrorField);

            var experiment = await scope.ServiceProvider.GetRequiredService<IValidationExperimentRepository>()
                .GetByIdAsync(id);
            Assert.NotNull(experiment);
            E2C2FailureReasonHelpers.AssertExactFailureReasons(
                experiment!,
                ValidationTrainingFailureCodes.ValidationDataLeakage,
                ValidationTrainingFailureCodes.ValidationAccessAuditPersistenceFailed);
            E2C2FailureReasonHelpers.AssertNoMirroredDiagnosticDuplicates(experiment!);
            E2C2FailureReasonHelpers.AssertNoSensitiveMessages(experiment!, result.ErrorMessage);
            Assert.Equal(ValidationExperimentStatus.Failed, experiment!.Status);
            Assert.Equal("LeakageDetected", experiment.CurrentStage);
            Assert.False(experiment.IsQualificationCapable);

            var trial = (await scope.ServiceProvider.GetRequiredService<IValidationParameterTrialRepository>()
                .GetByExperimentIdAsync(id)).Single();
            E2C2FailureReasonHelpers.AssertTrialFailureState(
                trial,
                ValidationTrialStatus.LeakageFailed,
                rankIneligible: true,
                ValidationTrainingFailureCodes.ValidationDataLeakage,
                ValidationTrainingFailureCodes.ValidationAccessAuditPersistenceFailed);
            Assert.NotEqual(ValidationAuditCompletionStatus.Complete, trial.AuditCompletionStatus);
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
    public async Task RunnerFailure_AndRecorderFlushFailure_PersistsBothWithAuditPrimary()
    {
        await using var factory = new E2C2OrchestrationFactory();
        factory.Controls.RunnerMode = E2C2RunnerMode.ThrowingTrialFailure;
        factory.Controls.FailOnFlushNumbers.Add(1);
        long? experimentId = null;

        try
        {
            var (id, _) = await E2C2ExperimentFactory.CreatePreparedSingleTrialExperimentAsync(factory, "trial-flush");
            experimentId = id;

            await using var scope = factory.Services.CreateAsyncScope();
            var lab = scope.ServiceProvider.GetRequiredService<IValidationLabService>();
            var result = await lab.RunTrainingAsync(id);

            Assert.False(result.Succeeded);
            Assert.Equal(ValidationTrainingFailureCodes.ValidationAccessAuditPersistenceFailed, result.ErrorField);

            var experiment = await scope.ServiceProvider.GetRequiredService<IValidationExperimentRepository>()
                .GetByIdAsync(id);
            Assert.NotNull(experiment);
            E2C2FailureReasonHelpers.AssertExactFailureReasons(
                experiment!,
                ValidationTrainingFailureCodes.ValidationAccessAuditPersistenceFailed,
                ValidationTrainingFailureCodes.TrialExecutionFailed);
            E2C2FailureReasonHelpers.AssertNoMirroredDiagnosticDuplicates(experiment!);
            E2C2FailureReasonHelpers.AssertNoSensitiveMessages(experiment!, result.ErrorMessage);
            Assert.Equal(ValidationExperimentStatus.Failed, experiment!.Status);
            Assert.False(experiment.IsQualificationCapable);

            var trial = (await scope.ServiceProvider.GetRequiredService<IValidationParameterTrialRepository>()
                .GetByExperimentIdAsync(id)).Single();
            E2C2FailureReasonHelpers.AssertTrialFailureState(
                trial,
                ValidationTrialStatus.AuditPersistenceFailed,
                rankIneligible: true,
                ValidationTrainingFailureCodes.ValidationAccessAuditPersistenceFailed);
            Assert.Equal(ValidationAuditCompletionStatus.RecoveryRequired, trial.AuditCompletionStatus);
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
    public async Task SuccessfulRunner_AndRecorderFlushFailure_FailsAuditAndBlocksQualification()
    {
        await using var factory = new E2C2OrchestrationFactory();
        factory.Controls.RunnerMode = E2C2RunnerMode.AllowedComplete;
        factory.Controls.FailOnFlushNumbers.Add(2);
        long? experimentId = null;

        try
        {
            var (id, _) = await E2C2ExperimentFactory.CreatePreparedSingleTrialExperimentAsync(factory, "success-flush");
            experimentId = id;

            await using var scope = factory.Services.CreateAsyncScope();
            var lab = scope.ServiceProvider.GetRequiredService<IValidationLabService>();
            var result = await lab.RunTrainingAsync(id);

            Assert.False(result.Succeeded);
            Assert.Equal(ValidationTrainingFailureCodes.ValidationAccessAuditPersistenceFailed, result.ErrorField);

            var experiment = await scope.ServiceProvider.GetRequiredService<IValidationExperimentRepository>()
                .GetByIdAsync(id);
            Assert.NotNull(experiment);
            E2C2FailureReasonHelpers.AssertPrimaryAndOrderedCodes(
                experiment!,
                ValidationTrainingFailureCodes.ValidationAccessAuditPersistenceFailed);
            E2C2FailureReasonHelpers.AssertNoMirroredDiagnosticDuplicates(experiment!);
            E2C2FailureReasonHelpers.AssertNoSensitiveMessages(experiment!, result.ErrorMessage);
            Assert.Equal(ValidationExperimentStatus.Failed, experiment!.Status);
            Assert.False(experiment.IsQualificationCapable);
            Assert.NotEqual(ValidationExperimentStatus.TrainingCompleted, experiment.Status);

            var trial = (await scope.ServiceProvider.GetRequiredService<IValidationParameterTrialRepository>()
                .GetByExperimentIdAsync(id)).Single();
            Assert.Equal(ValidationTrialStatus.AuditPersistenceFailed, trial.Status);
            Assert.Equal(ValidationTrialRankEligibility.Ineligible, trial.TrialRankEligibility);
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
    public async Task BoundaryFailure_AndOperationStatusFailure_DoesNotLoseBoundaryReason()
    {
        await using var factory = new E2C2OrchestrationFactory();
        factory.Controls.RunnerMode = E2C2RunnerMode.AdversarialBoundary;
        factory.Controls.FailOperationStatusSync = true;
        long? experimentId = null;

        try
        {
            var (id, _) = await E2C2ExperimentFactory.CreatePreparedSingleTrialExperimentAsync(factory, "boundary-opstatus");
            experimentId = id;

            await using var scope = factory.Services.CreateAsyncScope();
            var lab = scope.ServiceProvider.GetRequiredService<IValidationLabService>();

            var result = await lab.RunTrainingAsync(id);
            Assert.False(result.Succeeded);

            var experiment = await scope.ServiceProvider.GetRequiredService<IValidationExperimentRepository>()
                .GetByIdAsync(id);
            Assert.NotNull(experiment);
            E2C2FailureReasonHelpers.AssertExactFailureReasons(
                experiment!,
                ValidationTrainingFailureCodes.ValidationDataLeakage,
                ValidationTrainingFailureCodes.TrainingCleanupFailed);
            E2C2FailureReasonHelpers.AssertNoMirroredDiagnosticDuplicates(experiment!);
            E2C2FailureReasonHelpers.AssertNoSensitiveMessages(experiment!, result.ErrorMessage);
            Assert.Equal(ValidationExperimentStatus.Failed, experiment!.Status);
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
    public async Task AuditFailure_AndLeaseReleaseFailure_DoesNotLoseAuditReason()
    {
        await using var factory = new E2C2OrchestrationFactory();
        factory.Controls.RunnerMode = E2C2RunnerMode.AllowedComplete;
        factory.Controls.FailOnFlushNumbers.Add(1);
        factory.Controls.FailLeaseRelease = true;
        long? experimentId = null;

        try
        {
            var (id, _) = await E2C2ExperimentFactory.CreatePreparedSingleTrialExperimentAsync(factory, "audit-lease");
            experimentId = id;

            await using var scope = factory.Services.CreateAsyncScope();
            var lab = scope.ServiceProvider.GetRequiredService<IValidationLabService>();

            var result = await lab.RunTrainingAsync(id);
            Assert.False(result.Succeeded);

            var experiment = await scope.ServiceProvider.GetRequiredService<IValidationExperimentRepository>()
                .GetByIdAsync(id);
            Assert.NotNull(experiment);
            E2C2FailureReasonHelpers.AssertExactFailureReasons(
                experiment!,
                ValidationTrainingFailureCodes.ValidationAccessAuditPersistenceFailed,
                ValidationTrainingFailureCodes.TrainingCleanupFailed);
            E2C2FailureReasonHelpers.AssertNoMirroredDiagnosticDuplicates(experiment!);
            E2C2FailureReasonHelpers.AssertNoSensitiveMessages(experiment!, result.ErrorMessage);
            Assert.Equal(ValidationExperimentStatus.Failed, experiment!.Status);
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
    public async Task TrialFailure_AndCleanupFailure_DoesNotLoseTrialReason()
    {
        await using var factory = new E2C2OrchestrationFactory();
        factory.Controls.RunnerMode = E2C2RunnerMode.ThrowingTrialFailure;
        factory.Controls.FailLeaseRelease = true;
        long? experimentId = null;

        try
        {
            var (id, _) = await E2C2ExperimentFactory.CreatePreparedSingleTrialExperimentAsync(factory, "trial-cleanup");
            experimentId = id;

            await using var scope = factory.Services.CreateAsyncScope();
            var lab = scope.ServiceProvider.GetRequiredService<IValidationLabService>();

            var result = await lab.RunTrainingAsync(id);

            var experiment = await scope.ServiceProvider.GetRequiredService<IValidationExperimentRepository>()
                .GetByIdAsync(id);
            Assert.NotNull(experiment);
            E2C2FailureReasonHelpers.AssertExactFailureReasons(
                experiment!,
                ValidationTrainingFailureCodes.TrialExecutionFailed,
                ValidationTrainingFailureCodes.TrainingCleanupFailed);
            E2C2FailureReasonHelpers.AssertNoMirroredDiagnosticDuplicates(experiment!);
            E2C2FailureReasonHelpers.AssertNoSensitiveMessages(experiment!, result.ErrorMessage);
            Assert.False(experiment!.IsQualificationCapable);

            var trial = (await scope.ServiceProvider.GetRequiredService<IValidationParameterTrialRepository>()
                .GetByExperimentIdAsync(id)).FirstOrDefault();
            Assert.NotNull(trial);
            Assert.Equal(ValidationTrialStatus.Failed, trial!.Status);
            Assert.False(string.IsNullOrWhiteSpace(trial.ErrorMessage));
            E2C2FailureReasonHelpers.AssertNoSensitiveMessages(experiment, trial.ErrorMessage);
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
    public async Task CleanupOnly_LeaseReleaseFailure_BlocksQualification()
    {
        await using var factory = new E2C2OrchestrationFactory();
        factory.Controls.RunnerMode = E2C2RunnerMode.AllowedComplete;
        factory.Controls.FailLeaseRelease = true;
        long? experimentId = null;

        try
        {
            var (id, _) = await E2C2ExperimentFactory.CreatePreparedSingleTrialExperimentAsync(factory, "cleanup-only");
            experimentId = id;

            await using var scope = factory.Services.CreateAsyncScope();
            var lab = scope.ServiceProvider.GetRequiredService<IValidationLabService>();

            var result = await lab.RunTrainingAsync(id);

            var experiment = await scope.ServiceProvider.GetRequiredService<IValidationExperimentRepository>()
                .GetByIdAsync(id);
            Assert.NotNull(experiment);
            E2C2FailureReasonHelpers.AssertExactFailureReasons(
                experiment!,
                ValidationTrainingFailureCodes.TrainingCleanupFailed);
            E2C2FailureReasonHelpers.AssertNoMirroredDiagnosticDuplicates(experiment!);
            E2C2FailureReasonHelpers.AssertNoSensitiveMessages(experiment!, result.ErrorMessage);
            Assert.False(experiment!.IsQualificationCapable);
            Assert.Equal(ValidationTrainingFailureCodes.TrainingCleanupFailed, experiment.PrimaryFailureReason);
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
    public async Task ExistingFailureReasons_AreAppendedNotOverwritten()
    {
        await using var factory = new E2C2OrchestrationFactory();
        factory.Controls.RunnerMode = E2C2RunnerMode.AdversarialBoundary;
        long? experimentId = null;

        try
        {
            var (id, _) = await E2C2ExperimentFactory.CreatePreparedSingleTrialExperimentAsync(factory, "append");
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
                        Code = ValidationTrainingFailureCodes.InsufficientWarmup,
                        Category = ValidationTrainingFailureCategory.TrialExecution,
                        Precedence = ValidationTrainingFailurePrecedence.TrialExecution,
                        Phase = ValidationTrainingFailurePhase.TrialBody,
                        UserSafeMessage = "Prior warmup failure.",
                        OccurredAtUtc = DateTime.UtcNow.AddHours(-1),
                        IsQualificationBlocking = true
                    }
                ]);
                seeded.PrimaryFailureReason = ValidationTrainingFailureCodes.InsufficientWarmup;
                seeded.IsQualificationCapable = false;
                await experiments.UpdateAsync(seeded);
            }

            await using var scope = factory.Services.CreateAsyncScope();
            var lab = scope.ServiceProvider.GetRequiredService<IValidationLabService>();
            _ = await lab.RunTrainingAsync(id);

            var experiment = await scope.ServiceProvider.GetRequiredService<IValidationExperimentRepository>()
                .GetByIdAsync(id);
            Assert.NotNull(experiment);
            E2C2FailureReasonHelpers.AssertPrimaryAndOrderedCodes(
                experiment!,
                ValidationTrainingFailureCodes.ValidationDataLeakage,
                ValidationTrainingFailureCodes.InsufficientWarmup);
            E2C2FailureReasonHelpers.AssertNoMirroredDiagnosticDuplicates(experiment!);
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
    public async Task RepeatedResume_DoesNotDuplicateFailureReasons()
    {
        await using var factory = new E2C2OrchestrationFactory();
        factory.Controls.RunnerMode = E2C2RunnerMode.AdversarialBoundary;
        long? experimentId = null;

        try
        {
            var (id, _) = await E2C2ExperimentFactory.CreatePreparedSingleTrialExperimentAsync(factory, "resume-dedupe");
            experimentId = id;

            await using var scope = factory.Services.CreateAsyncScope();
            var lab = scope.ServiceProvider.GetRequiredService<IValidationLabService>();
            _ = await lab.RunTrainingAsync(id);

            var experiment = await scope.ServiceProvider.GetRequiredService<IValidationExperimentRepository>()
                .GetByIdAsync(id);
            Assert.NotNull(experiment);
            experiment!.Status = ValidationExperimentStatus.Failed;
            await scope.ServiceProvider.GetRequiredService<IValidationExperimentRepository>().UpdateAsync(experiment);

            var firstReasons = E2C2FailureReasonHelpers.ParseRecords(experiment.FailureReasonsJson);
            Assert.NotEmpty(firstReasons);
            var firstIdentities = firstReasons.Select(r => r.LogicalIdentity).ToArray();
            var firstCodes = firstReasons.Select(r => r.Code).ToArray();

            _ = await lab.ResumeTrainingAsync(id);

            experiment = await scope.ServiceProvider.GetRequiredService<IValidationExperimentRepository>()
                .GetByIdAsync(id);
            Assert.NotNull(experiment);
            var secondReasons = E2C2FailureReasonHelpers.ParseRecords(experiment!.FailureReasonsJson);
            Assert.Equal(firstReasons.Count, secondReasons.Count);
            Assert.Equal(firstIdentities, secondReasons.Select(r => r.LogicalIdentity).ToArray());
            Assert.Equal(firstCodes, secondReasons.Select(r => r.Code).ToArray());
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
    public async Task IncompleteFinalization_PersistsAuditDurabilityAndBlocksQualification()
    {
        await using var factory = new E2C2OrchestrationFactory();
        factory.Controls.RunnerMode = E2C2RunnerMode.AllowedComplete;
        factory.Controls.FailAuditFinalizationIncomplete = true;
        long? experimentId = null;

        try
        {
            var (id, _) = await E2C2ExperimentFactory.CreatePreparedSingleTrialExperimentAsync(factory, "incomplete-finalize");
            experimentId = id;

            await using var scope = factory.Services.CreateAsyncScope();
            var lab = scope.ServiceProvider.GetRequiredService<IValidationLabService>();
            var result = await lab.RunTrainingAsync(id);

            Assert.False(result.Succeeded);
            Assert.Equal(ValidationAuditCompletenessCode.FinalSequenceMissing.ToString(), result.ErrorField);

            var experiment = await scope.ServiceProvider.GetRequiredService<IValidationExperimentRepository>()
                .GetByIdAsync(id);
            Assert.NotNull(experiment);
            Assert.Equal(ValidationAuditCompletenessCode.FinalSequenceMissing.ToString(), experiment!.PrimaryFailureReason);
            Assert.Single(E2C2FailureReasonHelpers.ParseRecords(experiment.FailureReasonsJson));
            E2C2FailureReasonHelpers.AssertNoMirroredDiagnosticDuplicates(experiment);
            E2C2FailureReasonHelpers.AssertNoSensitiveMessages(experiment, result.ErrorMessage);
            Assert.False(experiment.IsQualificationCapable);
            Assert.Equal(ValidationExperimentStatus.Failed, experiment.Status);
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
    public async Task IncompleteCompletenessVerification_PersistsAuditDurabilityAndBlocksQualification()
    {
        await using var factory = new E2C2OrchestrationFactory();
        factory.Controls.RunnerMode = E2C2RunnerMode.AllowedComplete;
        factory.Controls.FailCompletenessVerification = true;
        long? experimentId = null;

        try
        {
            var (id, _) = await E2C2ExperimentFactory.CreatePreparedSingleTrialExperimentAsync(factory, "incomplete-completeness");
            experimentId = id;

            await using var scope = factory.Services.CreateAsyncScope();
            var lab = scope.ServiceProvider.GetRequiredService<IValidationLabService>();
            var result = await lab.RunTrainingAsync(id);

            Assert.False(result.Succeeded);
            Assert.Equal(ValidationAuditCompletenessCode.SequenceGap.ToString(), result.ErrorField);

            var experiment = await scope.ServiceProvider.GetRequiredService<IValidationExperimentRepository>()
                .GetByIdAsync(id);
            Assert.NotNull(experiment);
            Assert.Equal(ValidationAuditCompletenessCode.SequenceGap.ToString(), experiment!.PrimaryFailureReason);
            Assert.Single(E2C2FailureReasonHelpers.ParseRecords(experiment.FailureReasonsJson));
            E2C2FailureReasonHelpers.AssertNoMirroredDiagnosticDuplicates(experiment);
            E2C2FailureReasonHelpers.AssertNoSensitiveMessages(experiment, result.ErrorMessage);
            Assert.False(experiment.IsQualificationCapable);
            Assert.Equal(ValidationExperimentStatus.Failed, experiment.Status);
        }
        finally
        {
            if (experimentId is long eid)
            {
                await E2C2ExperimentFactory.CleanupExperimentAsync(factory, eid);
            }
        }
    }
}
