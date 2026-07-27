using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Strategies.Optimization;
using MomoQuant.Application.StrategyLab;
using MomoQuant.Application.ValidationLab;
using MomoQuant.Application.ValidationLab.Dtos;
using MomoQuant.Application.ValidationLab.Synthetic;
using MomoQuant.Domain.Constants;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.ValidationLab;
using MomoQuant.Persistence;

namespace MomoQuant.IntegrationTests;

/// <summary>
/// Milestone 23.0E2C1D — completed-trial revalidation on experiment resume must not skip
/// or trust prior Completed status without verifier proof.
/// </summary>
[Collection("Integration")]
public sealed class Milestone230E2C1DOrchestrationTests
{
    [Fact]
    public async Task ResumeExperiment_WithAlreadyCompletedTrialAndCorruptCompletedAudit_FailsClosed()
    {
        await using var factory = new E2C1DOrchestrationFactory();
        long? experimentId = null;
        Guid auditExecutionId = Guid.Empty;

        try
        {
            var (id, combo) = await CreatePreparedSingleTrialExperimentAsync(factory, "completed-corrupt");
            experimentId = id;

            await using (var seedScope = factory.Services.CreateAsyncScope())
            {
                var sp = seedScope.ServiceProvider;
                var db = sp.GetRequiredService<MomoQuantDbContext>();
                var trials = sp.GetRequiredService<IValidationParameterTrialRepository>();
                var experiments = sp.GetRequiredService<IValidationExperimentRepository>();

                var fingerprint = ValidationLabService.ParameterFingerprint(combo);
                await trials.AddAsync(new ValidationParameterTrial
                {
                    ValidationExperimentId = id,
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
                    CompletedAtUtc = DateTime.UtcNow
                });

                var (_, trial, execution, _) = await SeedCompletedAuditAsync(
                    sp, db, id, fingerprint, "completed-corrupt");
                auditExecutionId = execution.AuditExecutionId;

                trial.Status = ValidationTrialStatus.Completed;
                trial.Rank = 1;
                trial.TrialRankEligibility = ValidationTrialRankEligibility.Eligible;
                trial.AuditCompletionStatus = ValidationAuditCompletionStatus.Complete;
                await trials.UpdateAsync(trial);

                var rows = await sp.GetRequiredService<IValidationCandleAccessAuditRepository>()
                    .GetByExperimentIdAsync(id);
                var row = rows.Single(r => r.ScopeExecutionId == execution.ScopeExecutionId);
                await db.ValidationCandleAccessAudits
                    .Where(a => a.AccessEventId == row.AccessEventId)
                    .ExecuteDeleteAsync();

                var experimentEntity = await experiments.GetByIdAsync(id)
                    ?? throw new InvalidOperationException("Experiment missing.");
                experimentEntity.Status = ValidationExperimentStatus.TrainingInterrupted;
                experimentEntity.CurrentStage = "TrainingInterrupted";
                experimentEntity.SelectedTrialId = trial.Id;
                experimentEntity.SelectedTrialNumber = trial.TrialNumber;
                experimentEntity.UpdatedAtUtc = DateTime.UtcNow;
                await experiments.UpdateAsync(experimentEntity);
            }

            await using (var resumeScope = factory.Services.CreateAsyncScope())
            {
                var lab = resumeScope.ServiceProvider.GetRequiredService<IValidationLabService>();
                var result = await lab.ResumeTrainingAsync(id);
                Assert.False(result.Succeeded);
                Assert.False(string.IsNullOrWhiteSpace(result.ErrorMessage));
                Assert.DoesNotContain("StackTrace", result.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
            }

            await using (var assertScope = factory.Services.CreateAsyncScope())
            {
                var sp = assertScope.ServiceProvider;
                var runner = sp.GetRequiredService<Milestone230E2C1COrchestrationTests.TrackingStrategyLabRunner>();
                Assert.Equal(0, runner.InvocationCount);

                var trial = (await sp.GetRequiredService<IValidationParameterTrialRepository>()
                    .GetByExperimentIdAsync(id)).Single();
                Assert.NotEqual(ValidationTrialStatus.Completed, trial.Status);
                Assert.Null(trial.Rank);
                Assert.Equal(ValidationTrialRankEligibility.Ineligible, trial.TrialRankEligibility);
                Assert.NotEqual(ValidationAuditCompletionStatus.Complete, trial.AuditCompletionStatus);

                var execution = await sp.GetRequiredService<IValidationAuditExecutionRepository>()
                    .GetByAuditExecutionIdAsync(auditExecutionId);
                var batches = await sp.GetRequiredService<IValidationAuditBatchRepository>()
                    .GetByAuditExecutionIdAsync(auditExecutionId);
                var rows = await sp.GetRequiredService<IValidationCandleAccessAuditRepository>()
                    .GetByExperimentIdAsync(id);
                var completeness = sp.GetRequiredService<IValidationAuditCompletenessVerifier>()
                    .Verify(trial, execution, batches, rows);
                Assert.Equal(ValidationAuditCompletenessCode.EventMissing, completeness.CompletionCode);

                var experiment = await sp.GetRequiredService<IValidationExperimentRepository>().GetByIdAsync(id);
                Assert.NotEqual(ValidationExperimentStatus.TrainingCompleted, experiment!.Status);
                Assert.Null(experiment.SelectedTrialId);

                var trialList = await sp.GetRequiredService<IValidationParameterTrialRepository>()
                    .GetByExperimentIdAsync(id);
                var selection = sp.GetRequiredService<IValidationTrainingSelectionService>()
                    .FinalizeTrainingSelection(experiment, trialList);
                Assert.Null(selection.SelectedTrial);
                Assert.Equal(0, selection.Population.EligibleTrialCount);
            }
        }
        finally
        {
            if (experimentId is long eid)
            {
                await CleanupExperimentAsync(factory, eid);
            }
        }
    }

    private static async Task<(long ExperimentId, IReadOnlyDictionary<string, string> Combo)> CreatePreparedSingleTrialExperimentAsync(
        MomoQuantWebApplicationFactory factory,
        string suffix)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var lab = sp.GetRequiredService<IValidationLabService>();
        var expRepo = sp.GetRequiredService<IValidationExperimentRepository>();
        var symbolsRepo = sp.GetRequiredService<ISymbolRepository>();
        var definitions = sp.GetRequiredService<IStrategyParameterDefinitionProvider>();

        long exchangeId;
        long symbolId;
        var reference = await expRepo.GetByIdAsync(23) ?? (await expRepo.GetRecentAsync(1)).FirstOrDefault();
        if (reference is not null)
        {
            exchangeId = reference.ExchangeId;
            symbolId = reference.SymbolId;
        }
        else
        {
            var (symbols, _) = await symbolsRepo.GetPagedAsync(
                new MomoQuant.Shared.Contracts.PagedRequest { Page = 1, PageSize = 20 }, null);
            var symbol = symbols.First();
            exchangeId = symbol.ExchangeId;
            symbolId = symbol.Id;
        }

        var end = DateTime.UtcNow.Date.AddDays(-1);
        var start = end.AddDays(-14);
        var create = await lab.CreateExperimentAsync(new CreateValidationExperimentRequest
        {
            Name = $"VL-E2C1D {suffix} {Guid.NewGuid():N}",
            ExperimentType = ValidationExperimentType.TrainingSearchHoldoutValidation,
            StrategyCode = StrategyCodes.PriceStructureBreakoutRetest,
            StrategyVersion = "1.0.0",
            ExchangeId = exchangeId,
            SymbolId = symbolId,
            Timeframe = "15m",
            RequestedStartUtc = start,
            RequestedEndUtc = end,
            SplitRatio = 0.70m,
            RequiredWarmupCandles = 20,
            MaximumTrials = 1,
            DeterministicSeed = 23031,
            AutoImportMissingCandles = true,
            ParameterSearchSpaceOverrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["swingLeftBarsMin"] = "1",
                ["swingLeftBarsMax"] = "1",
                ["swingLeftBarsStep"] = "1",
                ["swingRightBarsMin"] = "1",
                ["swingRightBarsMax"] = "1",
                ["swingRightBarsStep"] = "1",
                ["retestTolerancePercentMin"] = "0.3",
                ["retestTolerancePercentMax"] = "0.3",
                ["retestTolerancePercentStep"] = "0.1",
                ["maxRetestBarsMin"] = "10",
                ["maxRetestBarsMax"] = "10",
                ["maxRetestBarsStep"] = "1",
                ["fixedRewardRiskMin"] = "2",
                ["fixedRewardRiskMax"] = "2",
                ["fixedRewardRiskStep"] = "0.5",
                ["stopBufferPercentMin"] = "0.05",
                ["stopBufferPercentMax"] = "0.05",
                ["stopBufferPercentStep"] = "0.05"
            },
            QualificationProfile = new ValidationQualificationProfileDto
            {
                MinimumTrainingClosedTrades = 0,
                MinimumTrainingProfitFactor = 0m,
                MinimumTrainingNetExpectancyR = -999m,
                MaximumTrainingDrawdownPercent = 100m
            }
        });
        if (!create.Succeeded || create.Data is null)
        {
            throw new InvalidOperationException(create.ErrorMessage ?? "Create failed.");
        }

        var prepare = await lab.PrepareDataAsync(create.Data.Id);
        if (!prepare.Succeeded)
        {
            throw new InvalidOperationException(prepare.ErrorMessage ?? "Prepare failed.");
        }

        var combos = ValidationLab224AIntegrityOrchestrationFixture.BuildThreeTrialGrid(definitions);
        return (create.Data.Id, combos[0]);
    }

    private static async Task<(
        ValidationExperiment Experiment,
        ValidationParameterTrial Trial,
        ValidationAuditExecution Execution,
        ValidationAuditBatch Batch)> SeedCompletedAuditAsync(
        IServiceProvider sp,
        MomoQuantDbContext db,
        long experimentId,
        string fingerprint,
        string suffix)
    {
        var hasher = new ValidationAuditPayloadSetHasher();
        var experiment = await sp.GetRequiredService<IValidationExperimentRepository>().GetByIdAsync(experimentId)
            ?? throw new InvalidOperationException("Experiment missing.");
        var trial = await sp.GetRequiredService<IValidationParameterTrialRepository>()
            .GetByExperimentAndFingerprintAsync(experimentId, fingerprint)
            ?? throw new InvalidOperationException("Trial missing.");

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
        trial.AuditCompletionStatus = ValidationAuditCompletionStatus.Complete;
        await sp.GetRequiredService<IValidationAuditExecutionRepository>().UpdateAsync(execution);
        await sp.GetRequiredService<IValidationParameterTrialRepository>().UpdateAsync(trial);
        _ = db;
        return (experiment, trial, execution, batch);
    }

    private static async Task CleanupExperimentAsync(MomoQuantWebApplicationFactory factory, long experimentId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();

        var auditIds = await db.ValidationAuditExecutions
            .Where(e => e.ValidationExperimentId == experimentId)
            .Select(e => e.AuditExecutionId)
            .ToListAsync();
        if (auditIds.Count > 0)
        {
            await db.ValidationAuditBatches
                .Where(b => auditIds.Contains(b.AuditExecutionId))
                .ExecuteDeleteAsync();
        }

        await db.ValidationAuditExecutions
            .Where(e => e.ValidationExperimentId == experimentId)
            .ExecuteDeleteAsync();
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
        await db.ResearchOperationStatuses
            .Where(o => o.EntityId == experimentId.ToString())
            .ExecuteDeleteAsync();
        await db.ValidationExperiments
            .Where(e => e.Id == experimentId)
            .ExecuteDeleteAsync();
    }

    private sealed class E2C1DOrchestrationFactory : MomoQuantWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IStrategyLabRunner>();
                services.AddSingleton<Milestone230E2C1COrchestrationTests.TrackingStrategyLabRunner>();
                services.AddScoped<IStrategyLabRunner>(sp =>
                    sp.GetRequiredService<Milestone230E2C1COrchestrationTests.TrackingStrategyLabRunner>());
            });
        }
    }
}
