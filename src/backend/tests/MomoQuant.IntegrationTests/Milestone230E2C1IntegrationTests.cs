using System.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Strategies;
using MomoQuant.Application.StrategyLab;
using MomoQuant.Application.ValidationLab;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Exchanges;
using MomoQuant.Domain.MarketData;
using MomoQuant.Domain.StrategyLab;
using MomoQuant.Domain.ValidationLab;
using MomoQuant.Persistence;
using MomoQuant.Persistence.Repositories;

namespace MomoQuant.IntegrationTests;

/// <summary>Milestone 23.0E2C1 — MySQL durable audit-execution integration coverage.</summary>
[Collection("Integration")]
public sealed class Milestone230E2C1IntegrationTests : IClassFixture<MomoQuantWebApplicationFactory>
{
    private readonly MomoQuantWebApplicationFactory _factory;
    private readonly ValidationAuditPayloadSetHasher _hasher = new();

    public Milestone230E2C1IntegrationTests(MomoQuantWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task CreateExecutionAndAssignTrial_IsAtomic()
    {
        long experimentId = 0;
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
        var executions = scope.ServiceProvider.GetRequiredService<IValidationAuditExecutionRepository>();

        try
        {
            var (experiment, trial) = await E2C1AuditFixtures.CreateExperimentAndTrialAsync(db, "atomic");
            experimentId = experiment.Id;
            var execution = E2C1AuditFixtures.NewExecution(experiment, trial);

            await executions.CreateAndAssignTrialAuthoritativeAsync(execution, trial);

            var reloadedTrial = await db.ValidationParameterTrials.AsNoTracking()
                .SingleAsync(t => t.Id == trial.Id);
            var reloadedExec = await executions.GetByAuditExecutionIdAsync(execution.AuditExecutionId);

            Assert.NotNull(reloadedExec);
            Assert.Equal(execution.AuditExecutionId, reloadedTrial.AuthoritativeAuditExecutionId);
            Assert.Equal(ValidationAuditCompletionStatus.InProgress, reloadedTrial.AuditCompletionStatus);
            Assert.Equal(execution.AttemptNumber, reloadedTrial.AuditAttemptNumber);
        }
        finally
        {
            if (experimentId > 0)
            {
                await E2C1AuditFixtures.CleanupAsync(_factory, experimentId);
            }
        }
    }

    [Fact]
    public async Task ScopeUsesPersistedScopeExecutionId()
    {
        long experimentId = 0;
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
        var executions = scope.ServiceProvider.GetRequiredService<IValidationAuditExecutionRepository>();

        try
        {
            var (experiment, trial) = await E2C1AuditFixtures.CreateExperimentAndTrialAsync(db, "scope");
            experimentId = experiment.Id;
            var scopeId = Guid.NewGuid();
            var execution = E2C1AuditFixtures.NewExecution(experiment, trial, scopeExecutionId: scopeId);
            await executions.CreateAndAssignTrialAuthoritativeAsync(execution, trial);

            var loaded = await executions.GetByAuditExecutionIdAsync(execution.AuditExecutionId);
            Assert.Equal(scopeId, loaded!.ScopeExecutionId);
            Assert.Equal(scopeId, trial.AuthoritativeAuditExecutionId is Guid
                ? loaded.ScopeExecutionId
                : Guid.Empty);
        }
        finally
        {
            if (experimentId > 0)
            {
                await E2C1AuditFixtures.CleanupAsync(_factory, experimentId);
            }
        }
    }

    [Fact]
    public async Task FlushCreatesManifestBeforeEventPersistence()
    {
        long experimentId = 0;
        await using var factory = new E2C1OrderingFactory();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
        var executions = scope.ServiceProvider.GetRequiredService<IValidationAuditExecutionRepository>();
        var recorder = scope.ServiceProvider.GetRequiredService<IValidationCandleAccessRecorder>();
        var batchRepo = (OrderingAuditBatchRepository)scope.ServiceProvider
            .GetRequiredService<IValidationAuditBatchRepository>();

        try
        {
            var (experiment, trial) = await E2C1AuditFixtures.CreateExperimentAndTrialAsync(db, "order");
            experimentId = experiment.Id;
            var execution = E2C1AuditFixtures.NewExecution(experiment, trial);
            await executions.CreateAndAssignTrialAuthoritativeAsync(execution, trial);

            using var ambient = ValidationAuditExecutionAmbient.Enter(new ValidationAuditExecutionAmbientContext
            {
                AuditExecutionId = execution.AuditExecutionId,
                ScopeExecutionId = execution.ScopeExecutionId,
                ExecutionToken = execution.ExecutionToken,
                AttemptNumber = execution.AttemptNumber,
                ValidationExperimentId = experiment.Id,
                ValidationTrialId = trial.Id
            });

            var trainingScope = new FakeTrainingCandleScope(
                experiment.Id,
                execution.ScopeExecutionId,
                trial.Id,
                [
                    new ValidationCandleAccessRecord
                    {
                        AccessEventId = Guid.NewGuid(),
                        ScopeExecutionId = execution.ScopeExecutionId,
                        ScopeSequenceNumber = 1,
                        ValidationExperimentId = experiment.Id,
                        TrialId = trial.Id,
                        TrialNumber = 1,
                        CallerComponent = "E2C1",
                        AccessPurpose = ValidationCandleAccessPurpose.EvaluationRange,
                        DatasetPartition = "Training",
                        CandleContentFingerprint = "ORDR0001",
                        AccessedAtUtc = DateTime.UtcNow,
                        ReturnedCandleCount = 1,
                        RecorderVersion = ValidationCandleAccessRecorder.RecorderVersion
                    }
                ]);

            await recorder.FlushAsync(trainingScope);

            Assert.True(batchRepo.ManifestCreateCalls >= 1);
            Assert.True(batchRepo.EventPersistObservedAfterManifest >= 1);
            var manifestIdx = batchRepo.CallOrder.IndexOf("GetOrCreateManifestAsync");
            var eventIdx = batchRepo.CallOrder.IndexOf("AddRangeIdempotentByAccessEventIdAsync");
            Assert.True(manifestIdx >= 0 && eventIdx > manifestIdx);
        }
        finally
        {
            if (experimentId > 0)
            {
                await E2C1AuditFixtures.CleanupAsync(factory, experimentId);
            }
        }
    }

    [Fact]
    public async Task ConfirmedBatchAdvancesDurableSequence()
    {
        long experimentId = 0;
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
        var sp = scope.ServiceProvider;

        try
        {
            var (experiment, trial, execution, _) = await SeedConfirmedBatchAsync(sp, db, "adv");
            experimentId = experiment.Id;

            var loaded = await sp.GetRequiredService<IValidationAuditExecutionRepository>()
                .GetByAuditExecutionIdAsync(execution.AuditExecutionId);
            Assert.Equal(1, loaded!.LastConfirmedSequence);
            Assert.Equal(ValidationAuditExecutionStatus.EventsConfirmed, loaded.Status);
        }
        finally
        {
            if (experimentId > 0)
            {
                await E2C1AuditFixtures.CleanupAsync(_factory, experimentId);
            }
        }
    }

    [Fact]
    public async Task ConfirmedBatchDoesNotAdvancePastGap()
    {
        long experimentId = 0;
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
        var executions = scope.ServiceProvider.GetRequiredService<IValidationAuditExecutionRepository>();
        var recorder = scope.ServiceProvider.GetRequiredService<IValidationCandleAccessRecorder>();

        try
        {
            var (experiment, trial, execution, _) = await SeedConfirmedBatchAsync(
                scope.ServiceProvider, db, "gap", markCompleted: false);
            experimentId = experiment.Id;
            Assert.Equal(1, execution.LastConfirmedSequence);

            using var ambient = ValidationAuditExecutionAmbient.Enter(new ValidationAuditExecutionAmbientContext
            {
                AuditExecutionId = execution.AuditExecutionId,
                ScopeExecutionId = execution.ScopeExecutionId,
                ExecutionToken = execution.ExecutionToken,
                AttemptNumber = execution.AttemptNumber,
                ValidationExperimentId = experiment.Id,
                ValidationTrialId = trial.Id
            });

            // Pending sequence 3 while durable cursor is 1 — sequence 2 is missing.
            var trainingScope = new FakeTrainingCandleScope(
                experiment.Id,
                execution.ScopeExecutionId,
                trial.Id,
                [
                    new ValidationCandleAccessRecord
                    {
                        AccessEventId = Guid.NewGuid(),
                        ScopeExecutionId = execution.ScopeExecutionId,
                        ScopeSequenceNumber = 3,
                        ValidationExperimentId = experiment.Id,
                        TrialId = trial.Id,
                        TrialNumber = 1,
                        CallerComponent = "E2C1",
                        AccessPurpose = ValidationCandleAccessPurpose.EvaluationRange,
                        DatasetPartition = "Training",
                        CandleContentFingerprint = "GAP00003",
                        AccessedAtUtc = DateTime.UtcNow,
                        ReturnedCandleCount = 1,
                        RecorderVersion = ValidationCandleAccessRecorder.RecorderVersion
                    }
                ]);

            var ex = await Assert.ThrowsAsync<ValidationAuditExecutionException>(() =>
                recorder.FlushAsync(trainingScope));
            Assert.Equal("VALIDATION_AUDIT_SEQUENCE_GAP", ex.ErrorCode);

            var loaded = await executions.GetByAuditExecutionIdAsync(execution.AuditExecutionId);
            Assert.Equal(1, loaded!.LastConfirmedSequence);
            Assert.NotEqual(3, loaded.LastConfirmedSequence);
        }
        finally
        {
            if (experimentId > 0)
            {
                await E2C1AuditFixtures.CleanupAsync(_factory, experimentId);
            }
        }
    }

    [Fact]
    public async Task RestartRecovery_ConfirmedRowsAdvanceSequence()
    {
        long experimentId = 0;
        Guid auditId = Guid.Empty;

        await using (var scope1 = _factory.Services.CreateAsyncScope())
        {
            var db = scope1.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
            var (experiment, trial) = await E2C1AuditFixtures.CreateExperimentAndTrialAsync(db, "restart-adv");
            experimentId = experiment.Id;
            var execution = E2C1AuditFixtures.NewExecution(experiment, trial);
            auditId = execution.AuditExecutionId;

            await scope1.ServiceProvider.GetRequiredService<IValidationAuditExecutionRepository>()
                .CreateAndAssignTrialAuthoritativeAsync(execution, trial);

            var eventId = Guid.NewGuid();
            var access = E2BAuditFixtures.NewAudit(experiment.Id, eventId, execution.ScopeExecutionId, 1, "Restart");
            var canonicalizer = new ValidationAccessPayloadCanonicalizer();
            var hash = canonicalizer.ComputeSha256(access);
            access.AccessPayloadHash = hash;
            access.AccessPayloadContractVersion = ValidationAccessPayloadContractVersions.Current;

            var entries = new[]
            {
                new ValidationAuditPayloadSetEntry(1, eventId, hash, ValidationAccessPayloadContractVersions.Current)
            };
            var setHash = _hasher.ComputeSetHash(entries);
            var (ids, hashes) = _hasher.BuildManifestJsons(entries);

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
                Status = ValidationAuditBatchStatus.Created,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
                AuditBatchContractVersion = ValidationAuditBatch.ContractVersionV1,
                RowVersion = 1
            };
            await scope1.ServiceProvider.GetRequiredService<IValidationAuditBatchRepository>().AddAsync(batch);

            await scope1.ServiceProvider.GetRequiredService<IValidationCandleAccessAuditRepository>()
                .AddRangeIdempotentByAccessEventIdAsync([access]);
            // Cursor intentionally left at 0 — new DI scope recovers.
        }

        // New scope object (in-process DI recreate) — ConditionalWeakTable cannot retain prior state.
        await using var scope2 = _factory.Services.CreateAsyncScope();
        try
        {
            var recovery = scope2.ServiceProvider.GetRequiredService<IValidationAuditExecutionRecoveryService>();
            var result = await recovery.RecoverAsync(auditId);

            Assert.True(result.RecoveredLastConfirmedSequence >= 1);
            Assert.Equal(ValidationAuditRecoveryDecision.SupersedeAndRerun, result.RecoveryDecision);
            Assert.True(result.MustRerunTrial);

            var loaded = await scope2.ServiceProvider.GetRequiredService<IValidationAuditExecutionRepository>()
                .GetByAuditExecutionIdAsync(auditId);
            Assert.True(loaded!.LastConfirmedSequence >= 1);
        }
        finally
        {
            if (experimentId > 0)
            {
                await E2C1AuditFixtures.CleanupAsync(_factory, experimentId);
            }
        }
    }

    [Fact]
    public async Task RestartRecovery_MissingRowsRequiresRerun()
    {
        long experimentId = 0;
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
        var executions = scope.ServiceProvider.GetRequiredService<IValidationAuditExecutionRepository>();
        var recovery = scope.ServiceProvider.GetRequiredService<IValidationAuditExecutionRecoveryService>();

        try
        {
            var (experiment, trial) = await E2C1AuditFixtures.CreateExperimentAndTrialAsync(db, "miss-rows");
            experimentId = experiment.Id;
            var execution = E2C1AuditFixtures.NewExecution(experiment, trial);
            await executions.CreateAndAssignTrialAuthoritativeAsync(execution, trial);

            var eventId = Guid.NewGuid();
            var hash = new string('A', 64);
            var entries = new[]
            {
                new ValidationAuditPayloadSetEntry(1, eventId, hash, ValidationAccessPayloadContractVersions.Current)
            };
            var setHash = _hasher.ComputeSetHash(entries);
            var (ids, hashes) = _hasher.BuildManifestJsons(entries);

            // Manifest exists, but matching access rows were never committed (Rule A).
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
                Status = ValidationAuditBatchStatus.Created,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
                AuditBatchContractVersion = ValidationAuditBatch.ContractVersionV1,
                RowVersion = 1
            };
            await scope.ServiceProvider.GetRequiredService<IValidationAuditBatchRepository>().AddAsync(batch);

            var result = await recovery.RecoverAsync(execution.AuditExecutionId);
            Assert.True(result.MustRerunTrial);
            Assert.False(result.IsComplete);
            Assert.False(result.CanContinueSameExecution);
            Assert.True(result.UnresolvedBatchCount >= 1);
            Assert.Equal(0, result.RecoveredLastConfirmedSequence);

            var loaded = await executions.GetByAuditExecutionIdAsync(execution.AuditExecutionId);
            Assert.Equal(0, loaded!.LastConfirmedSequence);
            Assert.Equal(ValidationAuditExecutionStatus.RecoveryRequired, loaded.Status);
        }
        finally
        {
            if (experimentId > 0)
            {
                await E2C1AuditFixtures.CleanupAsync(_factory, experimentId);
            }
        }
    }

    [Fact]
    public async Task CrashBeforeFirstFlush_ExecutionRemainsIncomplete()
    {
        long experimentId = 0;
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
        var executions = scope.ServiceProvider.GetRequiredService<IValidationAuditExecutionRepository>();
        var recovery = scope.ServiceProvider.GetRequiredService<IValidationAuditExecutionRecoveryService>();
        var verifier = scope.ServiceProvider.GetRequiredService<IValidationAuditCompletenessVerifier>();

        try
        {
            var (experiment, trial) = await E2C1AuditFixtures.CreateExperimentAndTrialAsync(db, "preflush");
            experimentId = experiment.Id;
            var execution = E2C1AuditFixtures.NewExecution(experiment, trial);
            await executions.CreateAndAssignTrialAuthoritativeAsync(execution, trial);

            var result = await recovery.RecoverAsync(execution.AuditExecutionId);
            Assert.False(result.MustRerunTrial);
            Assert.Equal(ValidationAuditRecoveryDecision.NoRecoveryNeeded, result.RecoveryDecision);
            Assert.False(result.IsComplete);

            var loaded = await executions.GetByAuditExecutionIdAsync(execution.AuditExecutionId);
            var completeness = verifier.Verify(trial, loaded, [], []);
            Assert.False(completeness.IsComplete);
        }
        finally
        {
            if (experimentId > 0)
            {
                await E2C1AuditFixtures.CleanupAsync(_factory, experimentId);
            }
        }
    }

    [Fact]
    public async Task CrashAfterCommitBeforeCursor_RecoverySucceeds()
    {
        // In-process durable-state simulation of commit-before-cursor (supplements process harness).
        long experimentId = 0;
        Guid auditId = Guid.Empty;

        await using (var scope1 = _factory.Services.CreateAsyncScope())
        {
            var db = scope1.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
            var (experiment, trial) = await E2C1AuditFixtures.CreateExperimentAndTrialAsync(db, "commit-cur");
            experimentId = experiment.Id;
            var execution = E2C1AuditFixtures.NewExecution(experiment, trial);
            auditId = execution.AuditExecutionId;

            await scope1.ServiceProvider.GetRequiredService<IValidationAuditExecutionRepository>()
                .CreateAndAssignTrialAuthoritativeAsync(execution, trial);

            var eventId = Guid.NewGuid();
            var access = E2BAuditFixtures.NewAudit(experiment.Id, eventId, execution.ScopeExecutionId, 1, "CommitCur");
            var canonicalizer = new ValidationAccessPayloadCanonicalizer();
            var hash = canonicalizer.ComputeSha256(access);
            access.AccessPayloadHash = hash;
            access.AccessPayloadContractVersion = ValidationAccessPayloadContractVersions.Current;

            var entries = new[]
            {
                new ValidationAuditPayloadSetEntry(1, eventId, hash, ValidationAccessPayloadContractVersions.Current)
            };
            var setHash = _hasher.ComputeSetHash(entries);
            var (ids, hashes) = _hasher.BuildManifestJsons(entries);

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
                Status = ValidationAuditBatchStatus.Created,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
                AuditBatchContractVersion = ValidationAuditBatch.ContractVersionV1,
                RowVersion = 1
            };
            await scope1.ServiceProvider.GetRequiredService<IValidationAuditBatchRepository>().AddAsync(batch);
            await scope1.ServiceProvider.GetRequiredService<IValidationCandleAccessAuditRepository>()
                .AddRangeIdempotentByAccessEventIdAsync([access]);
            // Cursor intentionally left at 0 — durable recover must confirm + advance.
        }

        await using var scope2 = _factory.Services.CreateAsyncScope();
        try
        {
            var recovery = scope2.ServiceProvider.GetRequiredService<IValidationAuditExecutionRecoveryService>();
            var result = await recovery.RecoverAsync(auditId);

            Assert.True(result.RecoveredLastConfirmedSequence >= 1);
            Assert.True(result.ConfirmedBatchCount >= 1);
            Assert.False(result.IsComplete);

            var loaded = await scope2.ServiceProvider.GetRequiredService<IValidationAuditExecutionRepository>()
                .GetByAuditExecutionIdAsync(auditId);
            Assert.True(loaded!.LastConfirmedSequence >= 1);
            Assert.Equal(ValidationAuditBatchStatus.Confirmed,
                (await scope2.ServiceProvider.GetRequiredService<IValidationAuditBatchRepository>()
                    .GetByAuditExecutionIdAsync(auditId)).Single().Status);
        }
        finally
        {
            if (experimentId > 0)
            {
                await E2C1AuditFixtures.CleanupAsync(_factory, experimentId);
            }
        }
    }

    [Fact]
    public async Task CrashAfterConfirmBeforeCompletion_RemainsIncomplete()
    {
        long experimentId = 0;
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
        var recovery = scope.ServiceProvider.GetRequiredService<IValidationAuditExecutionRecoveryService>();
        var verifier = scope.ServiceProvider.GetRequiredService<IValidationAuditCompletenessVerifier>();

        try
        {
            var (experiment, trial, execution, batch) = await SeedConfirmedBatchAsync(
                scope.ServiceProvider, db, "pre-term", markCompleted: false);
            experimentId = experiment.Id;

            // Strip terminal declaration so recovery sees confirm-without-completion.
            execution.FinalExpectedSequence = null;
            execution.ExpectedEventCount = null;
            execution.FinalPayloadSetHash = null;
            execution.Status = ValidationAuditExecutionStatus.EventsConfirmed;
            await scope.ServiceProvider.GetRequiredService<IValidationAuditExecutionRepository>()
                .UpdateAsync(execution);

            Assert.Null(execution.FinalExpectedSequence);
            Assert.Equal(ValidationAuditExecutionStatus.EventsConfirmed, execution.Status);

            trial.StrategyLabRunId = 1;
            trial.GuardrailDecision = "Qualified";
            await scope.ServiceProvider.GetRequiredService<IValidationParameterTrialRepository>()
                .UpdateAsync(trial);

            var result = await recovery.RecoverAsync(execution.AuditExecutionId);
            Assert.False(result.IsComplete);
            Assert.False(result.MustRerunTrial);
            Assert.True(result.CanContinueSameExecution);
            Assert.Equal("FINAL_SEQUENCE_NOT_DECLARED", result.FailureCode);

            var loaded = await scope.ServiceProvider.GetRequiredService<IValidationAuditExecutionRepository>()
                .GetByAuditExecutionIdAsync(execution.AuditExecutionId);
            var batches = await scope.ServiceProvider.GetRequiredService<IValidationAuditBatchRepository>()
                .GetByAuditExecutionIdAsync(execution.AuditExecutionId);
            var rows = await scope.ServiceProvider.GetRequiredService<IValidationCandleAccessAuditRepository>()
                .GetByExperimentIdAsync(experiment.Id);
            var completeness = verifier.Verify(trial, loaded, batches, rows);
            Assert.False(completeness.IsComplete);
            Assert.NotEqual(ValidationAuditCompletenessCode.Complete, completeness.CompletionCode);
            Assert.Equal(ValidationAuditBatchStatus.Confirmed, batches.Single().Status);
            _ = batch;
        }
        finally
        {
            if (experimentId > 0)
            {
                await E2C1AuditFixtures.CleanupAsync(_factory, experimentId);
            }
        }
    }

    [Fact]
    public async Task CompletionRequiresAllBatchesConfirmed()
    {
        long experimentId = 0;
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
        var finalizer = scope.ServiceProvider.GetRequiredService<IValidationAuditExecutionFinalizer>();
        var verifier = scope.ServiceProvider.GetRequiredService<IValidationAuditCompletenessVerifier>();

        try
        {
            var (experiment, trial, execution, confirmed) = await SeedConfirmedBatchAsync(
                scope.ServiceProvider, db, "all-batch", markCompleted: false);
            experimentId = experiment.Id;

            execution.FinalExpectedSequence = null;
            execution.ExpectedEventCount = null;
            execution.FinalPayloadSetHash = null;
            await scope.ServiceProvider.GetRequiredService<IValidationAuditExecutionRepository>()
                .UpdateAsync(execution);

            var pendingEventId = Guid.NewGuid();
            var pendingHash = new string('B', 64);
            var pendingEntries = new[]
            {
                new ValidationAuditPayloadSetEntry(
                    2, pendingEventId, pendingHash, ValidationAccessPayloadContractVersions.Current)
            };
            var pendingSetHash = _hasher.ComputeSetHash(pendingEntries);
            var (pendingIds, pendingHashes) = _hasher.BuildManifestJsons(pendingEntries);

            var unconfirmed = new ValidationAuditBatch
            {
                AuditBatchId = Guid.NewGuid(),
                AuditExecutionId = execution.AuditExecutionId,
                BatchNumber = 2,
                FirstSequence = 2,
                LastSequence = 2,
                ExpectedEventCount = 1,
                ExpectedEventIdsJson = pendingIds,
                ExpectedPayloadHashesJson = pendingHashes,
                ExpectedPayloadSetHash = pendingSetHash,
                Status = ValidationAuditBatchStatus.Created,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
                AuditBatchContractVersion = ValidationAuditBatch.ContractVersionV1,
                RowVersion = 1
            };
            await scope.ServiceProvider.GetRequiredService<IValidationAuditBatchRepository>()
                .AddAsync(unconfirmed);

            var completeResult = await finalizer.CompleteAsync(execution.AuditExecutionId, 2);
            Assert.False(completeResult.IsComplete);
            Assert.Equal(ValidationAuditCompletenessCode.ManifestMissing, completeResult.CompletionCode);

            var loaded = await scope.ServiceProvider.GetRequiredService<IValidationAuditExecutionRepository>()
                .GetByAuditExecutionIdAsync(execution.AuditExecutionId);
            var batches = await scope.ServiceProvider.GetRequiredService<IValidationAuditBatchRepository>()
                .GetByAuditExecutionIdAsync(execution.AuditExecutionId);
            Assert.Contains(batches, b => b.AuditBatchId == unconfirmed.AuditBatchId
                                          && b.Status == ValidationAuditBatchStatus.Created);
            Assert.Contains(batches, b => b.AuditBatchId == confirmed.AuditBatchId
                                          && b.Status == ValidationAuditBatchStatus.Confirmed);
            Assert.NotEqual(ValidationAuditExecutionStatus.Completed, loaded!.Status);
            Assert.Equal(2, batches.Count);

            // Pre-finalizer evidence: with EventsConfirmed + FinalExpected declared, verifier lists
            // non-confirmed batches (RecoveryRequired short-circuits after failed CompleteAsync).
            loaded.Status = ValidationAuditExecutionStatus.EventsConfirmed;
            loaded.FinalExpectedSequence = 2;
            loaded.ExpectedEventCount = 2;
            loaded.FinalPayloadSetHash = confirmed.ExpectedPayloadSetHash;
            var rows = await scope.ServiceProvider.GetRequiredService<IValidationCandleAccessAuditRepository>()
                .GetByExperimentIdAsync(experiment.Id);
            var completeness = verifier.Verify(trial, loaded, batches, rows);
            Assert.False(completeness.IsComplete);
            Assert.Equal(ValidationAuditCompletenessCode.ManifestMissing, completeness.CompletionCode);
            Assert.Contains(unconfirmed.AuditBatchId, completeness.NonConfirmedBatchIds);
        }
        finally
        {
            if (experimentId > 0)
            {
                await E2C1AuditFixtures.CleanupAsync(_factory, experimentId);
            }
        }
    }

    [Fact]
    public async Task CompletionRequiresEverySequenceExactlyOnce()
    {
        long experimentId = 0;
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
        var verifier = scope.ServiceProvider.GetRequiredService<IValidationAuditCompletenessVerifier>();

        try
        {
            var (experiment, trial, execution, batch) = await SeedConfirmedBatchAsync(
                scope.ServiceProvider, db, "once", markCompleted: false);
            experimentId = experiment.Id;

            execution.FinalExpectedSequence = 2;
            execution.ExpectedEventCount = 2;
            execution.Status = ValidationAuditExecutionStatus.Completed;
            await scope.ServiceProvider.GetRequiredService<IValidationAuditExecutionRepository>()
                .UpdateAsync(execution);

            var rows = await scope.ServiceProvider.GetRequiredService<IValidationCandleAccessAuditRepository>()
                .GetByExperimentIdAsync(experiment.Id);
            var result = verifier.Verify(trial, execution, [batch], rows);
            Assert.False(result.IsComplete);
            Assert.Equal(ValidationAuditCompletenessCode.SequenceGap, result.CompletionCode);
        }
        finally
        {
            if (experimentId > 0)
            {
                await E2C1AuditFixtures.CleanupAsync(_factory, experimentId);
            }
        }
    }

    [Fact]
    public async Task CompletionWritesFinalPayloadSetHash()
    {
        long experimentId = 0;
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
        var finalizer = scope.ServiceProvider.GetRequiredService<IValidationAuditExecutionFinalizer>();

        try
        {
            var (experiment, trial, execution, batch) = await SeedConfirmedBatchAsync(
                scope.ServiceProvider, db, "final-hash", markCompleted: false);
            experimentId = experiment.Id;

            // Clear any seed-time terminal fields so completion must write them.
            execution.FinalExpectedSequence = null;
            execution.ExpectedEventCount = null;
            execution.FinalPayloadSetHash = null;
            await scope.ServiceProvider.GetRequiredService<IValidationAuditExecutionRepository>()
                .UpdateAsync(execution);

            var completeResult = await finalizer.CompleteAsync(execution.AuditExecutionId, 1);
            Assert.True(completeResult.IsComplete);
            Assert.False(string.IsNullOrWhiteSpace(completeResult.FinalPayloadSetHash));
            Assert.Equal(64, completeResult.FinalPayloadSetHash!.Length);

            var loaded = await scope.ServiceProvider.GetRequiredService<IValidationAuditExecutionRepository>()
                .GetByAuditExecutionIdAsync(execution.AuditExecutionId);
            Assert.Equal(ValidationAuditExecutionStatus.Completed, loaded!.Status);
            Assert.Equal(completeResult.FinalPayloadSetHash, loaded.FinalPayloadSetHash);
            Assert.Equal(1, loaded.FinalExpectedSequence);
            Assert.Equal(batch.ExpectedPayloadSetHash, loaded.FinalPayloadSetHash);
            _ = trial;
        }
        finally
        {
            if (experimentId > 0)
            {
                await E2C1AuditFixtures.CleanupAsync(_factory, experimentId);
            }
        }
    }

    [Fact]
    public async Task TrialStatusCompletedOnlyAfterAuditCompleted()
    {
        long experimentId = 0;
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
        var gate = scope.ServiceProvider.GetRequiredService<IValidationTrialAuditCompletionGate>();
        var finalizer = scope.ServiceProvider.GetRequiredService<IValidationAuditExecutionFinalizer>();

        try
        {
            var (experiment, trial, execution, _) = await SeedConfirmedBatchAsync(
                scope.ServiceProvider, db, "trial-complete", markCompleted: false);
            experimentId = experiment.Id;

            trial.Status = ValidationTrialStatus.Running;
            trial.AuditCompletionStatus = ValidationAuditCompletionStatus.InProgress;
            await scope.ServiceProvider.GetRequiredService<IValidationParameterTrialRepository>()
                .UpdateAsync(trial);

            var incomplete = new ValidationAuditCompletenessResult
            {
                AuditExecutionId = execution.AuditExecutionId,
                IsAuthoritative = true,
                IsComplete = false,
                CompletionCode = ValidationAuditCompletenessCode.ExecutionInProgress
            };
            Assert.False(gate.CanMarkTrialCompleted(trial, execution, incomplete));

            var completeResult = await finalizer.CompleteAsync(execution.AuditExecutionId, 1);
            Assert.True(completeResult.IsComplete);

            var loadedTrial = (await scope.ServiceProvider.GetRequiredService<IValidationParameterTrialRepository>()
                .GetByExperimentIdAsync(experiment.Id)).Single(t => t.Id == trial.Id);
            var loadedExec = await scope.ServiceProvider.GetRequiredService<IValidationAuditExecutionRepository>()
                .GetByAuditExecutionIdAsync(execution.AuditExecutionId);

            Assert.Equal(ValidationAuditCompletionStatus.Complete, loadedTrial.AuditCompletionStatus);
            Assert.Equal(ValidationAuditExecutionStatus.Completed, loadedExec!.Status);

            var complete = new ValidationAuditCompletenessResult
            {
                AuditExecutionId = execution.AuditExecutionId,
                IsAuthoritative = true,
                IsTerminal = true,
                IsComplete = true,
                CompletionCode = ValidationAuditCompletenessCode.Complete
            };
            Assert.True(gate.CanMarkTrialCompleted(loadedTrial, loadedExec, complete));
        }
        finally
        {
            if (experimentId > 0)
            {
                await E2C1AuditFixtures.CleanupAsync(_factory, experimentId);
            }
        }
    }

    [Fact]
    public async Task StrategyLabRecovery_DoesNotCompleteAuditIncompleteTrial()
    {
        // Gate-level proof: incomplete audit blocks Completed (full StrategyLab recovery path covered in unit/WP9).
        long experimentId = 0;
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
        var gate = scope.ServiceProvider.GetRequiredService<IValidationTrialAuditCompletionGate>();

        try
        {
            var (experiment, trial) = await E2C1AuditFixtures.CreateExperimentAndTrialAsync(db, "sl-rec");
            experimentId = experiment.Id;
            trial.Status = ValidationTrialStatus.Completed;
            trial.AuthoritativeAuditExecutionId = Guid.NewGuid();
            trial.AuditCompletionStatus = ValidationAuditCompletionStatus.InProgress;
            await db.SaveChangesAsync();

            var completeness = new ValidationAuditCompletenessResult
            {
                IsAuthoritative = true,
                IsComplete = false,
                CompletionCode = ValidationAuditCompletenessCode.ExecutionInProgress
            };
            Assert.False(gate.CanMarkTrialCompleted(trial, null, completeness));
        }
        finally
        {
            if (experimentId > 0)
            {
                await E2C1AuditFixtures.CleanupAsync(_factory, experimentId);
            }
        }
    }

    [Fact]
    public async Task TwoContexts_OnlyOneAuthoritativeExecution()
    {
        long experimentId = 0;
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
        var executions = scope.ServiceProvider.GetRequiredService<IValidationAuditExecutionRepository>();

        try
        {
            var (experiment, trial) = await E2C1AuditFixtures.CreateExperimentAndTrialAsync(db, "two");
            experimentId = experiment.Id;
            var first = E2C1AuditFixtures.NewExecution(experiment, trial);
            await executions.CreateAndAssignTrialAuthoritativeAsync(first, trial);

            var second = E2C1AuditFixtures.NewExecution(experiment, trial, attempt: 2);
            var ex = await Assert.ThrowsAsync<ValidationAuditExecutionException>(() =>
                executions.CreateAndAssignTrialAuthoritativeAsync(second, trial));
            Assert.Equal("VALIDATION_AUDIT_MULTIPLE_ACTIVE_EXECUTIONS", ex.ErrorCode);
        }
        finally
        {
            if (experimentId > 0)
            {
                await E2C1AuditFixtures.CleanupAsync(_factory, experimentId);
            }
        }
    }

    [Fact]
    public async Task Supersession_IsAtomic()
    {
        long experimentId = 0;
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
        var executions = scope.ServiceProvider.GetRequiredService<IValidationAuditExecutionRepository>();
        var supersession = scope.ServiceProvider.GetRequiredService<IValidationAuditExecutionSupersessionService>();

        try
        {
            var (experiment, trial) = await E2C1AuditFixtures.CreateExperimentAndTrialAsync(db, "super");
            experimentId = experiment.Id;
            var first = E2C1AuditFixtures.NewExecution(experiment, trial);
            await executions.CreateAndAssignTrialAuthoritativeAsync(first, trial);

            var created = await supersession.SupersedeForRerunAsync(
                first.AuditExecutionId,
                Guid.NewGuid().ToString("N"),
                "PROCESS_INTERRUPTED_BEFORE_FLUSH");

            var old = await executions.GetByAuditExecutionIdAsync(first.AuditExecutionId);
            var trialReload = (await scope.ServiceProvider.GetRequiredService<IValidationParameterTrialRepository>()
                .GetByExperimentIdAsync(experiment.Id)).Single(t => t.Id == trial.Id);

            Assert.Equal(ValidationAuditExecutionStatus.Superseded, old!.Status);
            Assert.Equal(created.AuditExecutionId, old.SupersededByAuditExecutionId);
            Assert.Equal(created.AuditExecutionId, trialReload.AuthoritativeAuditExecutionId);
            Assert.Equal(created.AttemptNumber, trialReload.AuditAttemptNumber);
        }
        finally
        {
            if (experimentId > 0)
            {
                await E2C1AuditFixtures.CleanupAsync(_factory, experimentId);
            }
        }
    }

    [Fact]
    public async Task CompletedExecution_CannotBeSuperseded()
    {
        long experimentId = 0;
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
        var supersession = scope.ServiceProvider.GetRequiredService<IValidationAuditExecutionSupersessionService>();
        var finalizer = scope.ServiceProvider.GetRequiredService<IValidationAuditExecutionFinalizer>();

        try
        {
            var (experiment, trial, execution, _) = await SeedConfirmedBatchAsync(
                scope.ServiceProvider, db, "nosuper", markCompleted: false);
            experimentId = experiment.Id;
            Assert.True((await finalizer.CompleteAsync(execution.AuditExecutionId, 1)).IsComplete);

            var ex = await Assert.ThrowsAsync<ValidationAuditExecutionException>(() =>
                supersession.SupersedeForRerunAsync(
                    execution.AuditExecutionId,
                    Guid.NewGuid().ToString("N"),
                    "SHOULD_FAIL"));
            Assert.Equal("VALIDATION_AUDIT_CANNOT_SUPERSEDE_COMPLETED", ex.ErrorCode);
        }
        finally
        {
            if (experimentId > 0)
            {
                await E2C1AuditFixtures.CleanupAsync(_factory, experimentId);
            }
        }
    }

    [Fact]
    public async Task SupersededExecution_CannotAdvance()
    {
        long experimentId = 0;
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
        var executions = scope.ServiceProvider.GetRequiredService<IValidationAuditExecutionRepository>();
        var supersession = scope.ServiceProvider.GetRequiredService<IValidationAuditExecutionSupersessionService>();

        try
        {
            var (experiment, trial) = await E2C1AuditFixtures.CreateExperimentAndTrialAsync(db, "noadv");
            experimentId = experiment.Id;
            var first = E2C1AuditFixtures.NewExecution(experiment, trial);
            await executions.CreateAndAssignTrialAuthoritativeAsync(first, trial);
            await supersession.SupersedeForRerunAsync(
                first.AuditExecutionId, Guid.NewGuid().ToString("N"), "RERUN");

            var old = await executions.GetByAuditExecutionIdAsync(first.AuditExecutionId);
            Assert.False(old!.CanAdvanceSequence(1));
            Assert.Throws<InvalidOperationException>(() =>
                old.AdvanceLastConfirmedSequence(1, DateTime.UtcNow));
        }
        finally
        {
            if (experimentId > 0)
            {
                await E2C1AuditFixtures.CleanupAsync(_factory, experimentId);
            }
        }
    }

    [Fact]
    public async Task OldExecutionRows_ExcludedFromNewCompleteness()
    {
        long experimentId = 0;
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
        var executions = scope.ServiceProvider.GetRequiredService<IValidationAuditExecutionRepository>();
        var supersession = scope.ServiceProvider.GetRequiredService<IValidationAuditExecutionSupersessionService>();
        var verifier = scope.ServiceProvider.GetRequiredService<IValidationAuditCompletenessVerifier>();

        try
        {
            var (experiment, trial) = await E2C1AuditFixtures.CreateExperimentAndTrialAsync(db, "oldrows");
            experimentId = experiment.Id;
            var first = E2C1AuditFixtures.NewExecution(experiment, trial);
            await executions.CreateAndAssignTrialAuthoritativeAsync(first, trial);
            var second = await supersession.SupersedeForRerunAsync(
                first.AuditExecutionId, Guid.NewGuid().ToString("N"), "RERUN");

            trial.AuthoritativeAuditExecutionId = second.AuditExecutionId;
            var oldResult = verifier.Verify(trial, first, [], []);
            Assert.Equal(ValidationAuditCompletenessCode.NotAuthoritative, oldResult.CompletionCode);
            Assert.False(oldResult.IsComplete);
        }
        finally
        {
            if (experimentId > 0)
            {
                await E2C1AuditFixtures.CleanupAsync(_factory, experimentId);
            }
        }
    }

    [Fact]
    public async Task HistoricalRowsRemainUnchanged()
    {
        long experimentId = 0;
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();

        try
        {
            var (experiment, trial) = await E2C1AuditFixtures.CreateExperimentAndTrialAsync(db, "hist");
            experimentId = experiment.Id;
            Assert.Null(trial.AuthoritativeAuditExecutionId);
            Assert.Equal(ValidationAuditCompletionStatus.NotEvaluated, trial.AuditCompletionStatus);
            Assert.Equal(0, trial.AuditAttemptNumber);

            var access = E2BAuditFixtures.NewAudit(experiment.Id, Guid.NewGuid(), Guid.NewGuid(), 1, "Hist");
            access.AccessPayloadHash = null;
            access.AccessPayloadContractVersion = null;
            db.ValidationCandleAccessAudits.Add(access);
            await db.SaveChangesAsync();

            var row = await db.ValidationCandleAccessAudits.AsNoTracking()
                .SingleAsync(a => a.AccessEventId == access.AccessEventId);
            Assert.Null(row.AccessPayloadHash);
            Assert.Null(row.AccessPayloadContractVersion);
        }
        finally
        {
            if (experimentId > 0)
            {
                await E2C1AuditFixtures.CleanupAsync(_factory, experimentId);
            }
        }
    }

    [Fact]
    public async Task MigrationFreshPath()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
        var applied = await db.Database.GetAppliedMigrationsAsync();
        Assert.Contains(applied, m => m.Contains("M230E2C1_DurableAuditExecutions", StringComparison.Ordinal));

        await using var cmd = db.Database.GetDbConnection().CreateCommand();
        if (cmd.Connection!.State != System.Data.ConnectionState.Open)
        {
            await cmd.Connection.OpenAsync();
        }

        cmd.CommandText = """
            SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME IN ('ValidationAuditExecutions', 'ValidationAuditBatches')
            """;
        var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task MigrationUpgradePath()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();

        await using var cmd = db.Database.GetDbConnection().CreateCommand();
        if (cmd.Connection!.State != System.Data.ConnectionState.Open)
        {
            await cmd.Connection.OpenAsync();
        }

        cmd.CommandText = """
            SELECT CONSTRAINT_NAME
            FROM INFORMATION_SCHEMA.KEY_COLUMN_USAGE
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = 'ValidationAuditBatches'
              AND COLUMN_NAME = 'AuditExecutionId'
              AND REFERENCED_TABLE_NAME = 'ValidationAuditExecutions'
            """;
        var fk = await cmd.ExecuteScalarAsync();
        Assert.NotNull(fk);
    }

    [Fact]
    public async Task NoPendingModelChanges()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
    }

    [Fact]
    public async Task Recovery_ConfirmedBatchesWithGap_DoesNotAdvancePastGap()
    {
        long experimentId = 0;
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
        var recovery = scope.ServiceProvider.GetRequiredService<IValidationAuditExecutionRecoveryService>();
        var executions = scope.ServiceProvider.GetRequiredService<IValidationAuditExecutionRepository>();

        try
        {
            var (experiment, trial) = await E2C1AuditFixtures.CreateExperimentAndTrialAsync(db, "gap-rec");
            experimentId = experiment.Id;
            var execution = E2C1AuditFixtures.NewExecution(experiment, trial);
            await executions.CreateAndAssignTrialAuthoritativeAsync(execution, trial);

            await SeedConfirmedSequenceBatchAsync(
                scope.ServiceProvider, experiment, execution, batchNumber: 1, sequence: 1, label: "Gap1");
            await SeedConfirmedSequenceBatchAsync(
                scope.ServiceProvider, experiment, execution, batchNumber: 2, sequence: 3, label: "Gap3");

            execution.LastConfirmedSequence = 3;
            execution.ConfirmedEventCount = 3;
            execution.Status = ValidationAuditExecutionStatus.EventsConfirmed;
            await executions.UpdateAsync(execution);

            var result = await recovery.RecoverAsync(execution.AuditExecutionId);
            Assert.True(result.MustRerunTrial);
            Assert.Equal(2, result.FirstMissingSequence);
            Assert.Equal(1, result.RecoveredLastConfirmedSequence);
            Assert.Equal(1, result.RecoveredConfirmedEventCount);

            var loaded = await executions.GetByAuditExecutionIdAsync(execution.AuditExecutionId);
            Assert.Equal(1, loaded!.LastConfirmedSequence);
            Assert.Equal(1, loaded.ConfirmedEventCount);
            Assert.Equal(ValidationAuditExecutionStatus.RecoveryRequired, loaded.Status);
            _ = trial;
        }
        finally
        {
            if (experimentId > 0)
            {
                await E2C1AuditFixtures.CleanupAsync(_factory, experimentId);
            }
        }
    }

    [Fact]
    public async Task Recovery_LaterConfirmedBatchBeforeEarlierBatch_DoesNotAdvanceCursor()
    {
        long experimentId = 0;
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
        var recovery = scope.ServiceProvider.GetRequiredService<IValidationAuditExecutionRecoveryService>();
        var executions = scope.ServiceProvider.GetRequiredService<IValidationAuditExecutionRepository>();
        var batches = scope.ServiceProvider.GetRequiredService<IValidationAuditBatchRepository>();

        try
        {
            var (experiment, trial) = await E2C1AuditFixtures.CreateExperimentAndTrialAsync(db, "order-rec");
            experimentId = experiment.Id;
            var execution = E2C1AuditFixtures.NewExecution(experiment, trial);
            await executions.CreateAndAssignTrialAuthoritativeAsync(execution, trial);

            await SeedConfirmedSequenceBatchAsync(
                scope.ServiceProvider, experiment, execution, batchNumber: 2, sequence: 2, label: "Later2");

            var eventId1 = Guid.NewGuid();
            var hash1 = new string('A', 64);
            var entries1 = new[]
            {
                new ValidationAuditPayloadSetEntry(1, eventId1, hash1, ValidationAccessPayloadContractVersions.Current)
            };
            var setHash1 = _hasher.ComputeSetHash(entries1);
            var (ids1, hashes1) = _hasher.BuildManifestJsons(entries1);
            var batch1 = new ValidationAuditBatch
            {
                AuditBatchId = Guid.NewGuid(),
                AuditExecutionId = execution.AuditExecutionId,
                BatchNumber = 1,
                FirstSequence = 1,
                LastSequence = 1,
                ExpectedEventCount = 1,
                ExpectedEventIdsJson = ids1,
                ExpectedPayloadHashesJson = hashes1,
                ExpectedPayloadSetHash = setHash1,
                Status = ValidationAuditBatchStatus.Created,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
                AuditBatchContractVersion = ValidationAuditBatch.ContractVersionV1,
                RowVersion = 1
            };
            await batches.AddAsync(batch1);
            // Sequence-1 event row intentionally absent — later batch must not advance cursor.

            var result = await recovery.RecoverAsync(execution.AuditExecutionId);
            Assert.Equal(0, result.RecoveredLastConfirmedSequence);
            Assert.Equal(1, result.FirstMissingSequence);

            var loaded = await executions.GetByAuditExecutionIdAsync(execution.AuditExecutionId);
            Assert.Equal(0, loaded!.LastConfirmedSequence);
            _ = trial;
        }
        finally
        {
            if (experimentId > 0)
            {
                await E2C1AuditFixtures.CleanupAsync(_factory, experimentId);
            }
        }
    }

    [Fact]
    public async Task CompletedExecution_MissingEventRow_RecoveryReturnsIncomplete()
    {
        long experimentId = 0;
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
        var recovery = scope.ServiceProvider.GetRequiredService<IValidationAuditExecutionRecoveryService>();
        var finalizer = scope.ServiceProvider.GetRequiredService<IValidationAuditExecutionFinalizer>();
        var verifier = scope.ServiceProvider.GetRequiredService<IValidationAuditCompletenessVerifier>();
        var audits = scope.ServiceProvider.GetRequiredService<IValidationCandleAccessAuditRepository>();

        try
        {
            var (experiment, trial, execution, batch) = await SeedConfirmedBatchAsync(
                scope.ServiceProvider, db, "miss-ev", markCompleted: false);
            experimentId = experiment.Id;

            var rows = await audits.GetByExperimentIdAsync(experiment.Id);
            var row = rows.Single(r => r.ScopeExecutionId == execution.ScopeExecutionId);
            await db.ValidationCandleAccessAudits
                .Where(a => a.AccessEventId == row.AccessEventId)
                .ExecuteDeleteAsync();

            execution.FinalExpectedSequence = 1;
            execution.ExpectedEventCount = 1;
            execution.FinalPayloadSetHash = batch.ExpectedPayloadSetHash;
            execution.Status = ValidationAuditExecutionStatus.Completed;
            execution.CompletedAtUtc = DateTime.UtcNow;
            await scope.ServiceProvider.GetRequiredService<IValidationAuditExecutionRepository>()
                .UpdateAsync(execution);

            var recoveryResult = await recovery.RecoverAsync(
                execution.AuditExecutionId,
                new ValidationAuditExecutionRecoveryRequest
                {
                    CurrentLeaseOwner = "integration-recover-owner",
                    IsResume = true,
                    TrialStatus = trial.Status
                });
            Assert.False(recoveryResult.IsComplete);
            Assert.Equal(ValidationAuditRecoveryDecision.FailClosed, recoveryResult.RecoveryDecision);
            Assert.Equal("EventMissing", recoveryResult.FailureCode);

            var completeResult = await finalizer.CompleteAsync(execution.AuditExecutionId, 1);
            Assert.False(completeResult.IsComplete);
            Assert.Equal(ValidationAuditCompletenessCode.EventMissing, completeResult.CompletionCode);

            var loadedExec = await scope.ServiceProvider.GetRequiredService<IValidationAuditExecutionRepository>()
                .GetByAuditExecutionIdAsync(execution.AuditExecutionId);
            var batchList = await scope.ServiceProvider.GetRequiredService<IValidationAuditBatchRepository>()
                .GetByAuditExecutionIdAsync(execution.AuditExecutionId);
            var remainingRows = await audits.GetByExperimentIdAsync(experiment.Id);
            var completeness = verifier.Verify(trial, loadedExec, batchList, remainingRows);
            Assert.False(completeness.IsComplete);
            Assert.NotEqual(ValidationAuditCompletionStatus.Complete, trial.AuditCompletionStatus);
        }
        finally
        {
            if (experimentId > 0)
            {
                await E2C1AuditFixtures.CleanupAsync(_factory, experimentId);
            }
        }
    }

    [Fact]
    public async Task CompletedExecution_ValidEvidence_RecoveryRevalidatesAndReturnsComplete()
    {
        long experimentId = 0;
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
        var recovery = scope.ServiceProvider.GetRequiredService<IValidationAuditExecutionRecoveryService>();
        var finalizer = scope.ServiceProvider.GetRequiredService<IValidationAuditExecutionFinalizer>();
        var verifier = scope.ServiceProvider.GetRequiredService<IValidationAuditCompletenessVerifier>();

        try
        {
            var (experiment, trial, execution, batch) = await SeedConfirmedBatchAsync(
                scope.ServiceProvider, db, "valid-ev", markCompleted: true);
            experimentId = experiment.Id;

            var recoveryResult = await recovery.RecoverAsync(
                execution.AuditExecutionId,
                new ValidationAuditExecutionRecoveryRequest
                {
                    CurrentLeaseOwner = "integration-recover-owner",
                    IsResume = true,
                    TrialStatus = trial.Status
                });
            Assert.True(recoveryResult.IsComplete);
            Assert.Equal(ValidationAuditRecoveryDecision.AlreadyCompleted, recoveryResult.RecoveryDecision);
            Assert.False(recoveryResult.MustRerunTrial);

            var loadedExec = await scope.ServiceProvider.GetRequiredService<IValidationAuditExecutionRepository>()
                .GetByAuditExecutionIdAsync(execution.AuditExecutionId);
            var batchList = await scope.ServiceProvider.GetRequiredService<IValidationAuditBatchRepository>()
                .GetByAuditExecutionIdAsync(execution.AuditExecutionId);
            var rows = await scope.ServiceProvider.GetRequiredService<IValidationCandleAccessAuditRepository>()
                .GetByExperimentIdAsync(experiment.Id);
            var completeness = verifier.Verify(trial, loadedExec, batchList, rows);
            Assert.True(completeness.IsComplete);
            Assert.Equal(ValidationAuditCompletenessCode.Complete, completeness.CompletionCode);
            Assert.Equal(ValidationAuditExecutionStatus.Completed, loadedExec!.Status);
            Assert.Equal(ValidationAuditCompletionStatus.Complete, trial.AuditCompletionStatus);
            _ = finalizer;
            _ = batch;
        }
        finally
        {
            if (experimentId > 0)
            {
                await E2C1AuditFixtures.CleanupAsync(_factory, experimentId);
            }
        }
    }

    [Fact]
    public async Task RestartAfterConfirmedBatch_RequiredRerunCreatesNewExecutionAndSequenceOne()
    {
        long experimentId = 0;
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
        var recovery = scope.ServiceProvider.GetRequiredService<IValidationAuditExecutionRecoveryService>();
        var supersession = scope.ServiceProvider.GetRequiredService<IValidationAuditExecutionSupersessionService>();
        var executions = scope.ServiceProvider.GetRequiredService<IValidationAuditExecutionRepository>();
        var recorder = scope.ServiceProvider.GetRequiredService<IValidationCandleAccessRecorder>();

        try
        {
            var (experiment, trial, execution, _) = await SeedConfirmedBatchAsync(
                scope.ServiceProvider, db, "rerun-new", markCompleted: false);
            experimentId = experiment.Id;

            var recoveryResult = await recovery.RecoverAsync(execution.AuditExecutionId);
            Assert.True(recoveryResult.MustRerunTrial);

            var superseded = await supersession.SupersedeForRerunAsync(
                execution.AuditExecutionId,
                Guid.NewGuid().ToString("N"),
                recoveryResult.FailureCode ?? "STRATEGY_LAB_RERUN_REQUIRED");

            Assert.Equal(2, superseded.AttemptNumber);
            Assert.Equal(0, superseded.LastConfirmedSequence);
            Assert.Equal(ValidationAuditExecutionStatus.InProgress, superseded.Status);

            using var ambient = ValidationAuditExecutionAmbient.Enter(new ValidationAuditExecutionAmbientContext
            {
                AuditExecutionId = superseded.AuditExecutionId,
                ScopeExecutionId = superseded.ScopeExecutionId,
                ExecutionToken = superseded.ExecutionToken,
                AttemptNumber = superseded.AttemptNumber,
                ValidationExperimentId = experiment.Id,
                ValidationTrialId = trial.Id
            });

            var trainingScope = new FakeTrainingCandleScope(
                experiment.Id,
                superseded.ScopeExecutionId,
                trial.Id,
                [
                    new ValidationCandleAccessRecord
                    {
                        AccessEventId = Guid.NewGuid(),
                        ScopeExecutionId = superseded.ScopeExecutionId,
                        ScopeSequenceNumber = 1,
                        ValidationExperimentId = experiment.Id,
                        TrialId = trial.Id,
                        TrialNumber = 1,
                        CallerComponent = "E2C1B",
                        AccessPurpose = ValidationCandleAccessPurpose.EvaluationRange,
                        DatasetPartition = "Training",
                        CandleContentFingerprint = "RERUN0001",
                        AccessedAtUtc = DateTime.UtcNow,
                        ReturnedCandleCount = 1,
                        RecorderVersion = ValidationCandleAccessRecorder.RecorderVersion
                    }
                ],
                boundAuditExecutionId: superseded.AuditExecutionId);

            await recorder.FlushAsync(trainingScope);

            var reloaded = await executions.GetByAuditExecutionIdAsync(superseded.AuditExecutionId);
            Assert.Equal(1, reloaded!.LastConfirmedSequence);
            Assert.Equal(1, reloaded.ConfirmedEventCount);
        }
        finally
        {
            if (experimentId > 0)
            {
                await E2C1AuditFixtures.CleanupAsync(_factory, experimentId);
            }
        }
    }

    [Fact]
    public async Task TenThousandCandle_Materialization_UsesBoundedAuditWrites()
    {
        long experimentId = 0;
        var candleIds = new List<long>();
        var symbolName = $"E2C1-10K-{Guid.NewGuid():N}"[..20];
        const int RequiredWarmup = 20;
        const int EvalCount = 10_000;
        var evalStart = new DateTime(2043, 3, 1, 0, 0, 0, DateTimeKind.Utc);

        await using var scope = _factory.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<MomoQuantDbContext>();
        var executions = sp.GetRequiredService<IValidationAuditExecutionRepository>();
        var recorder = sp.GetRequiredService<IValidationCandleAccessRecorder>();
        var finalizer = sp.GetRequiredService<IValidationAuditExecutionFinalizer>();
        var verifier = sp.GetRequiredService<IValidationAuditCompletenessVerifier>();
        var scopeFactory = sp.GetRequiredService<IValidationTrainingCandleScopeFactory>();

        try
        {
            var testExchange = await db.Exchanges.AsNoTracking().OrderBy(e => e.Id).FirstAsync();
            var evalEnd = evalStart.AddMinutes(EvalCount * 15);
            var boundary = evalEnd.AddMinutes(30);

            var symbol = new Symbol
            {
                ExchangeId = testExchange.Id,
                SymbolName = symbolName,
                BaseAsset = "E2C1",
                QuoteAsset = "USDT",
                ContractType = ContractType.Perpetual,
                IsActive = true
            };
            db.Symbols.Add(symbol);
            await db.SaveChangesAsync();

            var warmupStart = evalStart.AddMinutes(-RequiredWarmup * 15);
            var totalCandles = RequiredWarmup + EvalCount;
            for (var i = 0; i < totalCandles; i += 500)
            {
                var batchCandles = new List<Candle>();
                var batchSize = Math.Min(500, totalCandles - i);
                for (var j = 0; j < batchSize; j++)
                {
                    var index = i + j;
                    var open = warmupStart.AddMinutes(index * 15);
                    batchCandles.Add(new Candle
                    {
                        ExchangeId = testExchange.Id,
                        SymbolId = symbol.Id,
                        Timeframe = Timeframe.M15,
                        OpenTimeUtc = open,
                        CloseTimeUtc = open.AddMinutes(15),
                        Open = 50000m + index,
                        High = 50100m + index,
                        Low = 49900m + index,
                        Close = 50050m + index,
                        Volume = 1000m + index,
                        IsClosed = true,
                        CreatedAtUtc = DateTime.UtcNow
                    });
                }

                db.Candles.AddRange(batchCandles);
                await db.SaveChangesAsync();
                candleIds.AddRange(batchCandles.Select(c => c.Id));
            }

            var (experiment, trial) = await E2C1AuditFixtures.CreateExperimentAndTrialAsync(db, "10k-prod");
            experiment.ExchangeId = testExchange.Id;
            experiment.SymbolId = symbol.Id;
            experiment.Symbol = symbolName;
            experiment.TrainingStartUtc = evalStart;
            experiment.TrainingEndUtc = evalEnd;
            experiment.ValidationStartUtc = boundary;
            db.ValidationExperiments.Update(experiment);
            await db.SaveChangesAsync();
            experimentId = experiment.Id;

            var execution = E2C1AuditFixtures.NewExecution(experiment, trial);
            await executions.CreateAndAssignTrialAuthoritativeAsync(execution, trial);

            var scopeRequest = new ValidationTrainingCandleScopeRequest
            {
                ValidationExperimentId = experiment.Id,
                SymbolId = symbol.Id,
                SymbolName = symbolName,
                Timeframe = "15m",
                TrainingEvaluationStartUtc = evalStart,
                TrainingEvaluationEndExclusiveUtc = evalEnd,
                ValidationBoundaryUtc = boundary,
                RequiredWarmupCandleCount = RequiredWarmup,
                RequirementsVersion = StrategyExecutionRequirements.Version,
                StrategyCode = experiment.StrategyCode,
                BoundScopeExecutionId = execution.ScopeExecutionId,
                BoundAuditExecutionId = execution.AuditExecutionId,
                BoundExecutionToken = execution.ExecutionToken,
                BoundAttemptNumber = execution.AttemptNumber
            };

            var sw = Stopwatch.StartNew();
            await using var trainingScope = await scopeFactory.CreateAsync(scopeRequest);

            using (ValidationAuditExecutionAmbient.Enter(new ValidationAuditExecutionAmbientContext
            {
                AuditExecutionId = execution.AuditExecutionId,
                ScopeExecutionId = execution.ScopeExecutionId,
                ExecutionToken = execution.ExecutionToken,
                AttemptNumber = execution.AttemptNumber,
                ValidationExperimentId = experiment.Id,
                ValidationTrialId = trial.Id
            }))
            {
                _ = trainingScope.CreateStrategyLabDataset(new ValidationDatasetMaterializationRequest
                {
                    SymbolId = symbol.Id,
                    SymbolName = symbolName,
                    Timeframe = "15m",
                    EvaluationFromUtc = evalStart,
                    EvaluationToExclusiveUtc = evalEnd,
                    WarmupCandleCount = RequiredWarmup,
                    CallerComponent = "E2C1.10K"
                });

                await recorder.FlushAsync(trainingScope);
            }

            var completeResult = await finalizer.CompleteAsync(
                execution.AuditExecutionId,
                trainingScope.AccessLog.Max(r => r.ScopeSequenceNumber));
            sw.Stop();

            var executionCount = await db.ValidationAuditExecutions
                .CountAsync(e => e.ValidationTrialId == trial.Id);
            var batchCount = await db.ValidationAuditBatches
                .CountAsync(b => b.AuditExecutionId == execution.AuditExecutionId);
            var eventCount = await db.ValidationCandleAccessAudits
                .CountAsync(a => a.ValidationExperimentId == experiment.Id
                                 && a.ScopeExecutionId == execution.ScopeExecutionId);
            var loadedExec = await executions.GetByAuditExecutionIdAsync(execution.AuditExecutionId);
            var batches = await sp.GetRequiredService<IValidationAuditBatchRepository>()
                .GetByAuditExecutionIdAsync(execution.AuditExecutionId);
            var rows = await sp.GetRequiredService<IValidationCandleAccessAuditRepository>()
                .GetByExperimentIdAsync(experiment.Id);
            var completeness = verifier.Verify(trial, loadedExec, batches, rows);

            Assert.Equal(1, executionCount);
            Assert.Equal(1, batchCount);
            Assert.Equal(3, eventCount);
            Assert.Equal(3, trainingScope.AccessLog.Count);
            Assert.Contains(rows, r => r.ReturnedCandleCount == RequiredWarmup);
            Assert.Contains(rows, r => r.ReturnedCandleCount == EvalCount);
            Assert.True(completeResult.IsComplete);
            Assert.Equal(3, loadedExec!.FinalExpectedSequence);
            Assert.Equal(ValidationAuditExecutionStatus.Completed, loadedExec.Status);
            Assert.True(completeness.IsComplete);
            Assert.True(sw.ElapsedMilliseconds < 120_000, $"TenThousandCandle bounded audit elapsed: {sw.ElapsedMilliseconds}ms");
        }
        finally
        {
            if (experimentId > 0)
            {
                await E2C1AuditFixtures.CleanupAsync(_factory, experimentId);
            }

            if (candleIds.Count > 0)
            {
                await db.Candles.Where(c => candleIds.Contains(c.Id)).ExecuteDeleteAsync();
            }

            var symbol = await db.Symbols.FirstOrDefaultAsync(s => s.SymbolName == symbolName);
            if (symbol is not null)
            {
                await db.Symbols.Where(s => s.Id == symbol.Id).ExecuteDeleteAsync();
            }
        }
    }

    [Fact]
    public async Task FixtureCleanup()
    {
        long experimentId = 0;
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
        var (experiment, trial) = await E2C1AuditFixtures.CreateExperimentAndTrialAsync(db, "cleanup");
        experimentId = experiment.Id;
        var execution = E2C1AuditFixtures.NewExecution(experiment, trial);
        await scope.ServiceProvider.GetRequiredService<IValidationAuditExecutionRepository>()
            .CreateAndAssignTrialAuthoritativeAsync(execution, trial);

        await E2C1AuditFixtures.CleanupAsync(_factory, experimentId);

        Assert.Equal(0, await db.ValidationExperiments.CountAsync(e => e.Id == experimentId));
        Assert.Equal(0, await db.ValidationAuditExecutions.CountAsync(e => e.ValidationExperimentId == experimentId));
    }

    private async Task<(
        ValidationExperiment Experiment,
        ValidationParameterTrial Trial,
        ValidationAuditExecution Execution,
        ValidationAuditBatch Batch)> SeedConfirmedBatchAsync(
        IServiceProvider sp,
        MomoQuantDbContext db,
        string suffix,
        bool markCompleted = false)
    {
        var (experiment, trial) = await E2C1AuditFixtures.CreateExperimentAndTrialAsync(db, suffix);
        var execution = E2C1AuditFixtures.NewExecution(experiment, trial);
        await sp.GetRequiredService<IValidationAuditExecutionRepository>()
            .CreateAndAssignTrialAuthoritativeAsync(execution, trial);

        var eventId = Guid.NewGuid();
        var access = E2BAuditFixtures.NewAudit(experiment.Id, eventId, execution.ScopeExecutionId, 1, "Seed");
        var canonicalizer = new ValidationAccessPayloadCanonicalizer();
        var hash = canonicalizer.ComputeSha256(access);
        access.AccessPayloadHash = hash;
        access.AccessPayloadContractVersion = ValidationAccessPayloadContractVersions.Current;

        var entries = new[]
        {
            new ValidationAuditPayloadSetEntry(1, eventId, hash, ValidationAccessPayloadContractVersions.Current)
        };
        var setHash = _hasher.ComputeSetHash(entries);
        var (ids, hashes) = _hasher.BuildManifestJsons(entries);

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
        execution.Status = markCompleted
            ? ValidationAuditExecutionStatus.Completed
            : ValidationAuditExecutionStatus.EventsConfirmed;
        if (markCompleted)
        {
            execution.CompletedAtUtc = DateTime.UtcNow;
            trial.AuditCompletionStatus = ValidationAuditCompletionStatus.Complete;
            await sp.GetRequiredService<IValidationParameterTrialRepository>().UpdateAsync(trial);
        }

        await sp.GetRequiredService<IValidationAuditExecutionRepository>().UpdateAsync(execution);
        return (experiment, trial, execution, batch);
    }

    private async Task SeedConfirmedSequenceBatchAsync(
        IServiceProvider sp,
        ValidationExperiment experiment,
        ValidationAuditExecution execution,
        int batchNumber,
        long sequence,
        string label)
    {
        var eventId = Guid.NewGuid();
        var access = E2BAuditFixtures.NewAudit(experiment.Id, eventId, execution.ScopeExecutionId, sequence, label);
        var canonicalizer = new ValidationAccessPayloadCanonicalizer();
        var hash = canonicalizer.ComputeSha256(access);
        access.AccessPayloadHash = hash;
        access.AccessPayloadContractVersion = ValidationAccessPayloadContractVersions.Current;

        var entries = new[]
        {
            new ValidationAuditPayloadSetEntry(
                sequence, eventId, hash, ValidationAccessPayloadContractVersions.Current)
        };
        var setHash = _hasher.ComputeSetHash(entries);
        var (ids, hashes) = _hasher.BuildManifestJsons(entries);

        var batch = new ValidationAuditBatch
        {
            AuditBatchId = Guid.NewGuid(),
            AuditExecutionId = execution.AuditExecutionId,
            BatchNumber = batchNumber,
            FirstSequence = sequence,
            LastSequence = sequence,
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
    }
}

internal sealed class E2C1OrderingFactory : MomoQuantWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            services.AddScoped<OrderingAuditBatchRepository>(sp =>
                new OrderingAuditBatchRepository(
                    ActivatorUtilities.CreateInstance<ValidationAuditBatchRepository>(sp)));
            services.RemoveAll<IValidationAuditBatchRepository>();
            services.AddScoped<IValidationAuditBatchRepository>(sp =>
                sp.GetRequiredService<OrderingAuditBatchRepository>());

            services.RemoveAll<IValidationCandleAccessAuditRepository>();
            services.AddScoped<IValidationCandleAccessAuditRepository>(sp =>
                new OrderingAccessAuditRepository(
                    ActivatorUtilities.CreateInstance<ValidationCandleAccessAuditRepository>(sp),
                    sp.GetRequiredService<OrderingAuditBatchRepository>()));
        });
    }
}
