using Microsoft.Extensions.DependencyInjection;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.ValidationLab;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.ValidationLab;

namespace MomoQuant.IntegrationTests;

/// <summary>
/// Milestone 23.0E2C2A — aggregated failure state survives reload/resume and remains qualification-blocked.
/// </summary>
[Collection("Integration")]
public sealed class Milestone230E2C2DurableReloadTests
{
    [Fact]
    public async Task AggregatedFailureState_ReloadsDurablyAndRemainsQualificationBlocked()
    {
        long? experimentId = null;
        string[]? expectedCodes = null;
        string[]? expectedIdentities = null;

        // 1) Trigger simultaneous boundary + flush failures.
        await using (var factory = new E2C2OrchestrationFactory())
        {
            factory.Controls.RunnerMode = E2C2RunnerMode.AdversarialBoundary;
            factory.Controls.FailOnFlushNumbers.Add(1);

            var (id, _) = await E2C2ExperimentFactory.CreatePreparedSingleTrialExperimentAsync(factory, "reload");
            experimentId = id;

            await using var scope = factory.Services.CreateAsyncScope();
            var lab = scope.ServiceProvider.GetRequiredService<IValidationLabService>();
            var result = await lab.RunTrainingAsync(id);
            Assert.False(result.Succeeded);

            var experiment = await scope.ServiceProvider.GetRequiredService<IValidationExperimentRepository>()
                .GetByIdAsync(id);
            Assert.NotNull(experiment);
            expectedCodes =
            [
                ValidationTrainingFailureCodes.ValidationDataLeakage,
                ValidationTrainingFailureCodes.ValidationAccessAuditPersistenceFailed
            ];
            expectedIdentities = E2C2FailureReasonHelpers.ParseRecords(experiment!.FailureReasonsJson)
                .Select(r => r.LogicalIdentity)
                .ToArray();
            E2C2FailureReasonHelpers.AssertExactFailureReasons(experiment, expectedCodes);
        }

        Assert.NotNull(experimentId);
        Assert.NotNull(expectedCodes);
        Assert.NotNull(expectedIdentities);

        // 2) Dispose original host; 3) reload from a new scope.
        try
        {
            await using var reloadFactory = new E2C2OrchestrationFactory();
            reloadFactory.Controls.RunnerMode = E2C2RunnerMode.AdversarialBoundary;
            await using (var reloadScope = reloadFactory.Services.CreateAsyncScope())
            {
                var sp = reloadScope.ServiceProvider;
                var experiment = await sp.GetRequiredService<IValidationExperimentRepository>()
                    .GetByIdAsync(experimentId.Value);
                Assert.NotNull(experiment);
                E2C2FailureReasonHelpers.AssertExactFailureReasons(experiment!, expectedCodes);
                Assert.Equal(
                    expectedIdentities,
                    E2C2FailureReasonHelpers.ParseRecords(experiment!.FailureReasonsJson)
                        .Select(r => r.LogicalIdentity)
                        .ToArray());
                Assert.Equal(ValidationExperimentStatus.Failed, experiment.Status);
                Assert.False(experiment.IsQualificationCapable);
                Assert.Equal(ValidationLeakageAuditStatus.Failed, experiment.LeakageAuditStatus);

                var trial = (await sp.GetRequiredService<IValidationParameterTrialRepository>()
                    .GetByExperimentIdAsync(experimentId.Value)).Single();
                Assert.Equal(ValidationTrialStatus.LeakageFailed, trial.Status);
                Assert.Equal(ValidationTrialRankEligibility.Ineligible, trial.TrialRankEligibility);
                Assert.NotEqual(ValidationAuditCompletionStatus.Complete, trial.AuditCompletionStatus);

                var freeze = await sp.GetRequiredService<IValidationLabService>().FreezeAsync(experimentId.Value);
                Assert.False(freeze.Succeeded);

                // 4) Resume again — reasons must not duplicate or reorder.
                experiment.Status = ValidationExperimentStatus.Failed;
                await sp.GetRequiredService<IValidationExperimentRepository>().UpdateAsync(experiment);
                _ = await sp.GetRequiredService<IValidationLabService>().ResumeTrainingAsync(experimentId.Value);

                experiment = await sp.GetRequiredService<IValidationExperimentRepository>()
                    .GetByIdAsync(experimentId.Value);
                Assert.NotNull(experiment);
                E2C2FailureReasonHelpers.AssertExactFailureReasons(experiment!, expectedCodes);
                Assert.Equal(
                    expectedIdentities,
                    E2C2FailureReasonHelpers.ParseRecords(experiment!.FailureReasonsJson)
                        .Select(r => r.LogicalIdentity)
                        .ToArray());
            }
        }
        finally
        {
            // First reload/resume host disposed.
        }

        // 5) Dispose again; 6) reload again; 7) assert exact reason list unchanged.
        try
        {
            await using var secondReloadFactory = new E2C2OrchestrationFactory();
            await using var secondReloadScope = secondReloadFactory.Services.CreateAsyncScope();
            var sp = secondReloadScope.ServiceProvider;

            var experiment = await sp.GetRequiredService<IValidationExperimentRepository>()
                .GetByIdAsync(experimentId.Value);
            Assert.NotNull(experiment);
            E2C2FailureReasonHelpers.AssertExactFailureReasons(experiment!, expectedCodes);
            Assert.Equal(
                expectedIdentities,
                E2C2FailureReasonHelpers.ParseRecords(experiment!.FailureReasonsJson)
                    .Select(r => r.LogicalIdentity)
                    .ToArray());
            Assert.False(experiment.IsQualificationCapable);
            Assert.Equal(ValidationLeakageAuditStatus.Failed, experiment.LeakageAuditStatus);
        }
        finally
        {
            await using var cleanupFactory = new E2C2OrchestrationFactory();
            await E2C2ExperimentFactory.CleanupExperimentAsync(cleanupFactory, experimentId.Value);
        }
    }
}
