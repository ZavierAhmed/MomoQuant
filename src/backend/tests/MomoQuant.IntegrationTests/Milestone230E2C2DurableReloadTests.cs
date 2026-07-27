using Microsoft.Extensions.DependencyInjection;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.ValidationLab;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.ValidationLab;

namespace MomoQuant.IntegrationTests;

/// <summary>
/// Milestone 23.0E2C2 — aggregated failure state survives reload and remains qualification-blocked.
/// </summary>
[Collection("Integration")]
public sealed class Milestone230E2C2DurableReloadTests
{
    [Fact]
    public async Task AggregatedFailureState_ReloadsDurablyAndRemainsQualificationBlocked()
    {
        long? experimentId = null;

        await using (var factory = new E2C2OrchestrationFactory())
        {
            factory.Controls.RunnerMode = E2C2RunnerMode.AdversarialBoundary;
            factory.Controls.FailOnFlushNumbers.Add(1);

            try
            {
                var (id, _) = await E2C2ExperimentFactory.CreatePreparedSingleTrialExperimentAsync(factory, "reload");
                experimentId = id;

                await using var scope = factory.Services.CreateAsyncScope();
                var lab = scope.ServiceProvider.GetRequiredService<IValidationLabService>();
                var result = await lab.RunTrainingAsync(id);
                Assert.False(result.Succeeded);
            }
            finally
            {
                // Keep rows for reload assertion in a separate host below.
            }
        }

        Assert.NotNull(experimentId);

        try
        {
            await using var reloadFactory = new E2C2OrchestrationFactory();
            await using var reloadScope = reloadFactory.Services.CreateAsyncScope();
            var sp = reloadScope.ServiceProvider;

            var experiment = await sp.GetRequiredService<IValidationExperimentRepository>()
                .GetByIdAsync(experimentId.Value);
            Assert.NotNull(experiment);
            E2C2FailureReasonHelpers.AssertPrimaryAndOrderedCodes(
                experiment!,
                ValidationTrainingFailureCodes.ValidationDataLeakage,
                ValidationTrainingFailureCodes.ValidationAccessAuditPersistenceFailed);
            Assert.Equal(ValidationExperimentStatus.Failed, experiment!.Status);
            Assert.False(experiment.IsQualificationCapable);
            Assert.Equal(ValidationLeakageAuditStatus.Failed, experiment.LeakageAuditStatus);

            var trial = (await sp.GetRequiredService<IValidationParameterTrialRepository>()
                .GetByExperimentIdAsync(experimentId.Value)).Single();
            Assert.Equal(ValidationTrialStatus.LeakageFailed, trial.Status);
            Assert.Equal(ValidationTrialRankEligibility.Ineligible, trial.TrialRankEligibility);
            Assert.NotEqual(ValidationAuditCompletionStatus.Complete, trial.AuditCompletionStatus);

            var freeze = await sp.GetRequiredService<IValidationLabService>().FreezeAsync(experimentId.Value);
            Assert.False(freeze.Succeeded);
            Assert.Contains("Failed", freeze.ErrorMessage ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await using var cleanupFactory = new E2C2OrchestrationFactory();
            await E2C2ExperimentFactory.CleanupExperimentAsync(cleanupFactory, experimentId.Value);
        }
    }
}
