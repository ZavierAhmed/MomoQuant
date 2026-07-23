using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.ValidationLab;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.ValidationLab;
using MomoQuant.Persistence;
using MomoQuant.Shared.Contracts;

namespace MomoQuant.IntegrationTests;

/// <summary>
/// Milestone 23.0B Part A — orchestrated candle-access leakage (adversarial + allowed)
/// via production <see cref="IValidationTrainingScopeExecution"/> + recorder flush,
/// then real freeze / validation APIs. Does not manually construct audits.
/// </summary>
[Collection("Integration")]
public sealed class Milestone230BOrchestrationTests : IClassFixture<MomoQuantWebApplicationFactory>
{
    private readonly MomoQuantWebApplicationFactory _factory;
    private readonly ValidationLab224AOrchestrationHarness _harness;

    public Milestone230BOrchestrationTests(MomoQuantWebApplicationFactory factory)
    {
        _factory = factory;
        _harness = new ValidationLab224AOrchestrationHarness(
            factory.Services.GetRequiredService<IServiceScopeFactory>());
    }

    [Fact]
    public async Task Adversarial_TrainingScope_FlushesDeniedAudit_AndBlocksFreezeValidation()
    {
        var correlationId = Guid.NewGuid().ToString("N");
        long? experimentId = null;
        long? authUserId = null;
        try
        {
            var (id, _) = await _harness.CreatePreparedExperimentAsync($"m230b-adv-{correlationId}");
            experimentId = id;
            var entity = await _harness.GetExperimentEntityAsync(id);
            Assert.NotNull(entity);
            Assert.NotNull(entity!.ValidationStartUtc);
            Assert.NotNull(entity.TrainingStartUtc);
            Assert.NotNull(entity.TrainingEndUtc);
            var validationStart = DateTime.SpecifyKind(entity.ValidationStartUtc.Value, DateTimeKind.Utc);

            await using (var scope = _factory.Services.CreateAsyncScope())
            {
                var sp = scope.ServiceProvider;
                var experiments = sp.GetRequiredService<IValidationExperimentRepository>();
                var execution = sp.GetRequiredService<IValidationTrainingScopeExecution>();
                var audits = sp.GetRequiredService<IValidationCandleAccessAuditRepository>();
                var lab = sp.GetRequiredService<IValidationLabService>();

                var experiment = await experiments.GetByIdAsync(id);
                Assert.NotNull(experiment);

                var leakage = await Assert.ThrowsAsync<ValidationDataLeakageException>(() =>
                    execution.ExecuteWithScopeAsync(
                        experiment!,
                        async trainingScope =>
                        {
                            await execution.ExecuteTrialAsync(
                                trainingScope,
                                trialNumber: 1,
                                trialId: null,
                                trialBody: () =>
                                {
                                    // Ambient/scoped candle source — adversarial request at boundary.
                                    _ = trainingScope.GetByOpenTimeUtc(
                                        validationStart,
                                        $"AdversarialTrainer:{correlationId}");
                                    return Task.CompletedTask;
                                });
                        }));

                Assert.Contains("ValidationDataLeakageDetected", leakage.Message, StringComparison.Ordinal);

                var persisted = await audits.GetByExperimentIdAsync(id);
                var denied = Assert.Single(persisted.Where(a =>
                    a.WasDenied
                    && a.RequestedStartUtc == validationStart
                    && (a.CallerComponent ?? string.Empty).Contains(correlationId, StringComparison.Ordinal)));
                Assert.True(denied.Id > 0);
                Assert.Equal(1, denied.TrialNumber);
                Assert.Equal(0, denied.ReturnedCandleCount);
                Assert.Contains("BoundaryCrossed", denied.DenialReason ?? string.Empty, StringComparison.Ordinal);

                // Mirror production training catch: status Failed from persisted denial evidence.
                experiment = await experiments.GetByIdAsync(id);
                Assert.NotNull(experiment);
                experiment!.Status = ValidationExperimentStatus.TrainingCompleted;
                experiment.LeakageAuditStatus = ValidationLeakageAuditStatus.Failed;
                experiment.CurrentStage = "LeakageDetected";
                experiment.UpdatedAtUtc = DateTime.UtcNow;
                await experiments.UpdateAsync(experiment);

                var freeze = await lab.FreezeAsync(id);
                Assert.False(freeze.Succeeded);
                Assert.Contains("Leakage", freeze.ErrorMessage ?? string.Empty, StringComparison.OrdinalIgnoreCase);
                Assert.Contains(
                    "ValidationDataLeakageDetected",
                    freeze.ErrorMessage ?? string.Empty,
                    StringComparison.OrdinalIgnoreCase);

                var validation = await lab.RunValidationAsync(id);
                Assert.False(validation.Succeeded);
                Assert.Contains(
                    "ConfigurationFrozen",
                    validation.ErrorMessage ?? string.Empty,
                    StringComparison.OrdinalIgnoreCase);

                // Evidence for parent summary
                Assert.True(experimentId > 0);
                Assert.True(denied.Id > 0);
                Assert.False(freeze.Succeeded);
                Assert.False(validation.Succeeded);
            }

            // HTTP freeze also blocked (JWT via disposable admin — not admin@momoquant.local)
            var (client, userId) = await IntegrationDisposableAuth.CreateAuthorizedAdminClientAsync(_factory);
            authUserId = userId;
            var httpFreeze = await client.PostAsync($"/api/v1/validation-lab/experiments/{id}/freeze", null);
            Assert.Equal(HttpStatusCode.BadRequest, httpFreeze.StatusCode);
        }
        finally
        {
            if (experimentId is long eid)
            {
                await CleanupExperimentAsync(eid);
            }

            if (authUserId is long uid)
            {
                await IntegrationDisposableAuth.DeleteUsersAsync(_factory, uid);
            }
        }
    }

    [Fact]
    public async Task Allowed_TrainingScope_PersistsMinMaxFingerprint_AndLeakageAuditPasses()
    {
        var correlationId = Guid.NewGuid().ToString("N");
        long? experimentId = null;
        try
        {
            var (id, _) = await _harness.CreatePreparedExperimentAsync($"m230b-ok-{correlationId}");
            experimentId = id;
            var entity = await _harness.GetExperimentEntityAsync(id);
            Assert.NotNull(entity);
            Assert.NotNull(entity!.ValidationStartUtc);
            Assert.NotNull(entity.TrainingStartUtc);
            Assert.NotNull(entity.TrainingEndUtc);
            var validationStart = DateTime.SpecifyKind(entity.ValidationStartUtc.Value, DateTimeKind.Utc);
            var trainingStart = DateTime.SpecifyKind(entity.TrainingStartUtc.Value, DateTimeKind.Utc);
            var trainingEnd = DateTime.SpecifyKind(entity.TrainingEndUtc.Value, DateTimeKind.Utc);

            await using var scope = _factory.Services.CreateAsyncScope();
            var sp = scope.ServiceProvider;
            var experiments = sp.GetRequiredService<IValidationExperimentRepository>();
            var execution = sp.GetRequiredService<IValidationTrainingScopeExecution>();
            var audits = sp.GetRequiredService<IValidationCandleAccessAuditRepository>();
            var leakageAuditor = sp.GetRequiredService<IValidationLeakageAuditor>();

            var experiment = await experiments.GetByIdAsync(id);
            Assert.NotNull(experiment);

            DateTime? allowedOpen = null;
            await execution.ExecuteWithScopeAsync(
                experiment!,
                async trainingScope =>
                {
                    await execution.ExecuteTrialAsync(
                        trainingScope,
                        trialNumber: 2,
                        trialId: null,
                        trialBody: () =>
                        {
                            Assert.True(trainingScope.Count > 0, "Prepared experiment must have training candles.");
                            var candle = trainingScope[trainingScope.Count - 1];
                            Assert.True(candle.OpenTimeUtc < validationStart);
                            allowedOpen = candle.OpenTimeUtc;
                            var hit = trainingScope.GetByOpenTimeUtc(
                                candle.OpenTimeUtc,
                                $"AllowedTrainer:{correlationId}");
                            Assert.NotNull(hit);
                            return Task.CompletedTask;
                        });
                });

            Assert.NotNull(allowedOpen);

            var persisted = await audits.GetByExperimentIdAsync(id);
            var allowed = Assert.Single(persisted.Where(a =>
                !a.WasDenied
                && (a.CallerComponent ?? string.Empty).Contains(correlationId, StringComparison.Ordinal)));
            Assert.True(allowed.Id > 0);
            Assert.Equal(2, allowed.TrialNumber);
            Assert.True(allowed.ReturnedCandleCount >= 1);
            Assert.NotNull(allowed.MinimumReturnedTimestampUtc);
            Assert.NotNull(allowed.MaximumReturnedTimestampUtc);
            Assert.True(allowed.MaximumReturnedTimestampUtc!.Value < validationStart);
            Assert.False(string.IsNullOrWhiteSpace(allowed.CandleContentFingerprint));
            Assert.Equal(allowedOpen, allowed.MinimumReturnedTimestampUtc);
            Assert.Equal(allowedOpen, allowed.MaximumReturnedTimestampUtc);

            // Leakage evaluation from orchestrated DB evidence (not manually constructed audits).
            var report = leakageAuditor.EvaluateFromAccessEvidence(
                persisted,
                validationStart,
                trainingStart,
                trainingEnd,
                optimizerInputFingerprint: $"m230b-ok-{correlationId}");
            Assert.Equal(ValidationLeakageAuditStatus.Passed, report.Status);
            Assert.False(report.BlocksFreezeOrPassed);
            Assert.Equal(0, report.DeniedAccessCount);

            experiment = await experiments.GetByIdAsync(id);
            Assert.NotNull(experiment);
            experiment!.LeakageAuditStatus = report.Status;
            experiment.LeakageAuditJson = leakageAuditor.Serialize(report);
            experiment.Status = ValidationExperimentStatus.TrainingCompleted;
            experiment.UpdatedAtUtc = DateTime.UtcNow;
            await experiments.UpdateAsync(experiment);

            var lab = sp.GetRequiredService<IValidationLabService>();
            var freeze = await lab.FreezeAsync(id);
            // Freeze may fail selection integrity, but must not fail for leakage.
            if (!freeze.Succeeded)
            {
                Assert.DoesNotContain(
                    "Leakage",
                    freeze.ErrorMessage ?? string.Empty,
                    StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(
                    "ValidationDataLeakageDetected",
                    freeze.ErrorMessage ?? string.Empty,
                    StringComparison.OrdinalIgnoreCase);
            }
        }
        finally
        {
            if (experimentId is long eid)
            {
                await CleanupExperimentAsync(eid);
            }
        }
    }

    private async Task CleanupExperimentAsync(long experimentId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();

        await db.ValidationCandleAccessAudits
            .Where(a => a.ValidationExperimentId == experimentId)
            .ExecuteDeleteAsync();
        await db.ValidationSegmentResults
            .Where(s => s.ValidationExperimentId == experimentId)
            .ExecuteDeleteAsync();
        await db.ValidationParameterTrials
            .Where(t => t.ValidationExperimentId == experimentId)
            .ExecuteDeleteAsync();
        await db.ValidationExperimentExecutionLeases
            .Where(l => l.ValidationExperimentId == experimentId)
            .ExecuteDeleteAsync();
        await db.ValidationExperiments
            .Where(e => e.Id == experimentId)
            .ExecuteDeleteAsync();
    }

}
