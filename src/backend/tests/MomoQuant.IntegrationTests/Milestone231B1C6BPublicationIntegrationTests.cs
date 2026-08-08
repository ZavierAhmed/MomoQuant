using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Audit;
using MomoQuant.Application.Common;
using MomoQuant.Application.Optimization.Dtos;
using MomoQuant.Application.Strategies.PriceStructure;
using MomoQuant.Application.ValidationLab;
using MomoQuant.Application.ValidationLab.Dtos;
using MomoQuant.Domain.Audit;
using MomoQuant.Domain.Constants;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Strategies;
using MomoQuant.Domain.ValidationLab;
using MomoQuant.Persistence;
using MomoQuant.Shared.Contracts;
using MySqlConnector;

namespace MomoQuant.IntegrationTests;

[Collection("Integration")]
public sealed class Milestone231B1C6BPublicationIntegrationTests
    : IClassFixture<DisposableIntegrationDatabaseFixture>
{
    private const string PreviousMigration = "20260801174916_AddParameterSetQualificationStatus";
    private const string CurrentMigration = "20260801185538_PublishQualifiedValidationLabParameterSets";
    private const string B1C6CSuccessorMigration = "20260801232441_GatePaperDeploymentSimulation";
    private readonly DisposableIntegrationDatabaseFixture _fixture;

    public Milestone231B1C6BPublicationIntegrationTests(DisposableIntegrationDatabaseFixture fixture) =>
        _fixture = fixture;

    [Fact]
    public async Task Publish_WithRealAuthoritativeEvaluator_PersistsExactEvidenceBoundRowAndAudit()
    {
        var seeded = await SeedQualifiedExperimentAsync("real-evaluator");
        try
        {
            await using var scope = _fixture.Factory.Services.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<IValidationParameterSetPublicationService>();

            var result = await service.PublishAsync(seeded.ExperimentId,
                new PublishValidationParameterSetRequest { DisplayName = "B1C6B relational publication" });

            Assert.True(result.Succeeded, result.ErrorMessage);
            var dto = Assert.IsType<StrategyParameterSetDto>(result.Data);
            var db = scope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
            db.ChangeTracker.Clear();
            var stored = await db.StrategyParameterSets.AsNoTracking().SingleAsync(row => row.Id == dto.Id);
            Assert.Equal(seeded.FrozenJson, stored.ParametersJson);
            Assert.Equal(seeded.Fingerprint, stored.QualificationParameterFingerprint);
            Assert.Equal(seeded.ExperimentId, stored.QualificationSourceExperimentId);
            Assert.Equal(seeded.TrialId, stored.QualificationSourceTrialId);
            Assert.Equal(StrategyCodes.PriceStructureBreakoutRetest, stored.StrategyCode);
            Assert.Equal(seeded.SymbolId, stored.SymbolId);
            Assert.Equal(seeded.Timeframe, stored.Timeframe);
            Assert.Equal(StrategyParameterSetSource.ValidationLab, stored.Source);
            Assert.True(stored.IsApproved);
            Assert.Equal(ParameterSetQualificationStatus.DeploymentQualified, stored.QualificationStatus);
            Assert.False(stored.IsDefaultForStrategy);
            Assert.False(stored.IsDefaultForSymbolTimeframe);
            Assert.Null(stored.TrainingMetricsJson);
            Assert.Null(stored.ValidationMetricsJson);

            var experiment = await db.ValidationExperiments.AsNoTracking()
                .SingleAsync(row => row.Id == seeded.ExperimentId);
            var strategy = await db.Strategies.AsNoTracking()
                .SingleAsync(row => row.Code == StrategyCode.PriceStructureBreakoutRetest);
            Assert.True(experiment.IsCanonical);
            Assert.Equal(experiment.Id, strategy.CanonicalValidationExperimentId);
            Assert.True(strategy.DeploymentQualificationEligible);

            var audit = await db.AuditLogs.AsNoTracking().SingleAsync(row =>
                row.Action == ValidationParameterSetPublicationService.AuditAction
                && row.EntityId == stored.Id);
            Assert.Contains(seeded.Fingerprint, audit.NewValueJson, StringComparison.Ordinal);
            Assert.DoesNotContain("minimumStrength", audit.NewValueJson, StringComparison.Ordinal);
        }
        finally
        {
            await CleanupAsync([seeded]);
        }
    }

    [Fact]
    public async Task Endpoint_IsAdminOnly_AndIgnoresCallerControlledPublicationFields()
    {
        var seeded = await SeedQualifiedExperimentAsync("endpoint");
        var anonymous = _fixture.Factory.CreateClient();
        var (admin, userId) = await IntegrationDisposableAuth.CreateAuthorizedAdminClientAsync(
            _fixture.Factory,
            "b1c6b-publication");
        try
        {
            using var denied = await anonymous.PostAsJsonAsync(
                $"/api/v1/validation-lab/experiments/{seeded.ExperimentId}/publish-parameter-set",
                new { displayName = "Denied" });
            Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);

            using var response = await admin.PostAsJsonAsync(
                $"/api/v1/validation-lab/experiments/{seeded.ExperimentId}/publish-parameter-set",
                new
                {
                    displayName = "Authorized",
                    strategyCode = "FORGED",
                    trialId = -1,
                    parameters = new { forged = true },
                    qualificationStatus = "ResearchOnly",
                    qualificationParameterFingerprint = "FORGED",
                    isApproved = false,
                    isDefaultForStrategy = true
                });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var payload = await response.Content.ReadFromJsonAsync<ApiResponse<StrategyParameterSetDto>>(
                IntegrationTestJson.Options);
            var dto = Assert.IsType<StrategyParameterSetDto>(payload!.Data);
            Assert.Equal(StrategyCodes.PriceStructureBreakoutRetest, dto.StrategyCode);
            Assert.Equal(seeded.TrialId, dto.QualificationSourceTrialId);
            Assert.Equal(seeded.Fingerprint, dto.QualificationParameterFingerprint);
            Assert.True(dto.IsApproved);
            Assert.True(dto.IsDeploymentQualified);
            Assert.False(dto.IsDefaultForStrategy);
        }
        finally
        {
            anonymous.Dispose();
            admin.Dispose();
            await CleanupAsync([seeded]);
            await IntegrationDisposableAuth.DeleteUsersAsync(_fixture.Factory, userId);
        }
    }

    [Fact]
    public async Task ConcurrentSameExperiment_UsesIndependentOverlappingTransactions_AndCreatesOneRow()
    {
        var seeded = await SeedQualifiedExperimentAsync("same-race");
        try
        {
            var barrier = new TwoParticipantBarrier();
            await using var firstScope = _fixture.Factory.Services.CreateAsyncScope();
            await using var secondScope = _fixture.Factory.Services.CreateAsyncScope();
            var first = BuildBarrierService(firstScope.ServiceProvider, barrier);
            var second = BuildBarrierService(secondScope.ServiceProvider, barrier);

            var results = await Task.WhenAll(
                first.PublishAsync(seeded.ExperimentId, new()),
                second.PublishAsync(seeded.ExperimentId, new()));

            Assert.All(results, result => Assert.True(result.Succeeded, result.ErrorMessage));
            Assert.Equal(results[0].Data!.Id, results[1].Data!.Id);
            Assert.Equal(2, barrier.Arrivals);
            await using var verifyScope = _fixture.Factory.Services.CreateAsyncScope();
            var db = verifyScope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
            Assert.Equal(1, await db.StrategyParameterSets.CountAsync(row =>
                row.QualificationSourceExperimentId == seeded.ExperimentId));
            Assert.Equal(1, await db.AuditLogs.CountAsync(row =>
                row.Action == ValidationParameterSetPublicationService.AuditAction
                && row.NewValueJson!.Contains($"\"experimentId\":{seeded.ExperimentId}")));
        }
        finally
        {
            await CleanupAsync([seeded]);
        }
    }

    [Fact]
    public async Task ConcurrentDifferentExperiments_UsesIndependentOverlappingTransactions_AndOnlyOneBecomesCanonical()
    {
        var firstSeed = await SeedQualifiedExperimentAsync("different-race-a");
        var secondSeed = await SeedQualifiedExperimentAsync("different-race-b", resetCanonical: false);
        try
        {
            var barrier = new TwoParticipantBarrier();
            await using var firstScope = _fixture.Factory.Services.CreateAsyncScope();
            await using var secondScope = _fixture.Factory.Services.CreateAsyncScope();
            var first = BuildBarrierService(firstScope.ServiceProvider, barrier);
            var second = BuildBarrierService(secondScope.ServiceProvider, barrier);

            var results = await Task.WhenAll(
                first.PublishAsync(firstSeed.ExperimentId, new()),
                second.PublishAsync(secondSeed.ExperimentId, new()));

            var success = Assert.Single(results.Where(result => result.Succeeded));
            var blocked = Assert.Single(results.Where(result => !result.Succeeded));
            Assert.Equal(ValidationParameterSetPublicationCodes.ExistingCanonicalQualification, blocked.ErrorField);
            Assert.Equal(2, barrier.Arrivals);
            await using var verifyScope = _fixture.Factory.Services.CreateAsyncScope();
            var db = verifyScope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
            Assert.Equal(1, await db.StrategyParameterSets.CountAsync(row =>
                row.StrategyCode == StrategyCodes.PriceStructureBreakoutRetest
                && row.QualificationStatus == ParameterSetQualificationStatus.DeploymentQualified));
            Assert.Equal(success.Data!.QualificationSourceExperimentId,
                (await db.Strategies.AsNoTracking().SingleAsync(row =>
                    row.Code == StrategyCode.PriceStructureBreakoutRetest)).CanonicalValidationExperimentId);
        }
        finally
        {
            await CleanupAsync([firstSeed, secondSeed]);
        }
    }

    [Fact]
    public async Task Migration_PreservesHistoricalRows_EnforcesProvenance_AndRollsBackCleanly()
    {
        await using var scope = _fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
        var migrator = db.GetService<IMigrator>();

        await migrator.MigrateAsync(PreviousMigration);
        try
        {
            await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO StrategyParameterSets
                    (Name, StrategyCode, SymbolId, Timeframe, MarketRegime, ParametersJson, Source,
                     OptimizationRunId, TrainingRangeJson, ValidationRangeJson, TrainingMetricsJson,
                     ValidationMetricsJson, RobustnessScore, IsApproved, QualificationStatus,
                     IsDefaultForStrategy, IsDefaultForSymbolTimeframe, CreatedAtUtc, ApprovedAtUtc)
                VALUES
                    ('B1C6B historical approved', 'MOMO_ADAPTIVE_MTF_TREND_BREAKOUT', NULL, '15m', NULL,
                     '{{}}', 'Manual', NULL, NULL, NULL, NULL, NULL, NULL, 1, 'HistoricalNotEvaluated',
                     0, 0, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6)),
                    ('B1C6B research only', 'MOMO_ADAPTIVE_MTF_TREND_BREAKOUT', NULL, '15m', NULL,
                     '{{}}', 'Manual', NULL, NULL, NULL, NULL, NULL, NULL, 0, 'ResearchOnly',
                     0, 0, UTC_TIMESTAMP(6), NULL)
                """);

            await migrator.MigrateAsync(CurrentMigration);
            db.ChangeTracker.Clear();
            var historical = await db.StrategyParameterSets.AsNoTracking()
                .Where(row => row.Name.StartsWith("B1C6B "))
                .OrderBy(row => row.Name)
                .ToListAsync();
            Assert.Equal(2, historical.Count);
            Assert.Equal(ParameterSetQualificationStatus.HistoricalNotEvaluated,
                historical.Single(row => row.Name == "B1C6B historical approved").QualificationStatus);
            Assert.True(historical.Single(row => row.Name == "B1C6B historical approved").IsApproved);
            Assert.Equal(ParameterSetQualificationStatus.ResearchOnly,
                historical.Single(row => row.Name == "B1C6B research only").QualificationStatus);
            Assert.All(historical, row =>
            {
                Assert.Null(row.QualificationSourceExperimentId);
                Assert.Null(row.QualificationSourceTrialId);
                Assert.Null(row.QualificationParameterFingerprint);
                Assert.Null(row.QualificationEvidenceVersion);
                Assert.Null(row.QualifiedAtUtc);
            });

            db.StrategyParameterSets.Add(new StrategyParameterSet
            {
                Name = "B1C6B forged deployment qualification",
                StrategyCode = StrategyCodes.MomoAdaptiveMultiTimeframeTrendBreakout,
                Timeframe = "15m",
                ParametersJson = "{}",
                Source = StrategyParameterSetSource.Manual,
                IsApproved = true,
                QualificationStatus = ParameterSetQualificationStatus.DeploymentQualified,
                CreatedAtUtc = DateTime.UtcNow
            });
            var exception = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            Assert.Equal(3819, FindMySqlException(exception)?.Number);
            db.ChangeTracker.Clear();

            Assert.Equal(
                [B1C6CSuccessorMigration],
                await db.Database.GetPendingMigrationsAsync());
            Assert.Equal(5, await ScalarLongAsync(db, """
                SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'StrategyParameterSets'
                  AND COLUMN_NAME IN ('QualificationSourceExperimentId', 'QualificationSourceTrialId',
                    'QualificationParameterFingerprint', 'QualificationEvidenceVersion', 'QualifiedAtUtc')
                """));
            Assert.Equal(2, await ScalarLongAsync(db, """
                SELECT COUNT(*) FROM INFORMATION_SCHEMA.STATISTICS
                WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'StrategyParameterSets'
                  AND INDEX_NAME IN ('IX_StrategyParameterSets_QualificationSourceExperimentId',
                    'IX_StrategyParameterSets_QualificationSourceTrialId') AND NON_UNIQUE = 0
                """));

            await migrator.MigrateAsync(PreviousMigration);
            Assert.Equal(0, await ScalarLongAsync(db, """
                SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'StrategyParameterSets'
                  AND COLUMN_NAME = 'QualificationSourceExperimentId'
                """));
            Assert.Equal(1, await ScalarLongAsync(db, """
                SELECT COUNT(*) FROM StrategyParameterSets
                WHERE Name = 'B1C6B historical approved' AND IsApproved = 1
                  AND QualificationStatus = 'HistoricalNotEvaluated'
                """));
            Assert.Equal(1, await ScalarLongAsync(db, """
                SELECT COUNT(*) FROM StrategyParameterSets
                WHERE Name = 'B1C6B research only' AND IsApproved = 0
                  AND QualificationStatus = 'ResearchOnly'
                """));
        }
        finally
        {
            await migrator.MigrateAsync(CurrentMigration);
            await db.StrategyParameterSets
                .Where(row => row.Name.StartsWith("B1C6B "))
                .ExecuteDeleteAsync();
        }
    }

    internal async Task<SeededPublication> SeedQualifiedExperimentAsync(
        string suffix,
        bool resetCanonical = true)
    {
        await using var scope = _fixture.Factory.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<MomoQuantDbContext>();
        var strategy = await db.Strategies.SingleAsync(row =>
            row.Code == StrategyCode.PriceStructureBreakoutRetest);
        strategy.IsEnabled = true;
        strategy.Version = PriceStructureBreakoutRetestEvaluator.StrategyVersion;
        if (resetCanonical)
        {
            strategy.CanonicalValidationExperimentId = null;
            strategy.DeploymentQualificationEligible = false;
        }

        var (experiment, trial) = await E2C1AuditFixtures.CreateExperimentAndTrialAsync(db, suffix);
        var fingerprints = services.GetRequiredService<IValidationParameterFingerprintService>();
        const string frozenJson = "{\"minimumStrength\":\"0.5\",\"lookback\":\"20\"}";
        const string selectedJson = "{\"lookback\":\"20.0\",\"minimumStrength\":\"0.50\"}";
        var fingerprint = fingerprints.ComputeFingerprintFromSnapshotJson(frozenJson);
        experiment.ExperimentType = ValidationExperimentType.TrainingSearchHoldoutValidation;
        experiment.Status = ValidationExperimentStatus.Completed;
        experiment.StrategyCode = StrategyCodes.PriceStructureBreakoutRetest;
        experiment.StrategyVersion = strategy.Version;
        experiment.ValidationRevealStatus = ValidationRevealStatus.Revealed;
        experiment.StrategyRobustnessDecision = StrategyRobustnessDecision.Passed;
        experiment.QualificationRuleResultsJson = ValidationVerdictService.SerializeRules(
        [
            new QualificationRuleResult
            {
                RuleKey = "DataQuality",
                Status = QualificationRuleStatus.Passed,
                Reason = "Durable integration evidence passes."
            }
        ]);
        experiment.IsQualificationCapable = true;
        experiment.SupersessionStatus = ValidationExperimentSupersessionStatus.None;
        experiment.FrozenSnapshotValidationStatus = FrozenSnapshotValidationStatus.Valid;
        experiment.SelectionIntegrityStatus = ValidationSelectionIntegrityStatus.Passed;
        experiment.TrialSegmentReconciliationStatus = ValidationTrialSegmentReconciliationStatus.Matched;
        experiment.SelectedTrialId = trial.Id;
        experiment.SelectedTrialNumber = trial.TrialNumber;
        experiment.SelectedTrialParameterSnapshotJson = selectedJson;
        experiment.SelectedTrialParameterFingerprint = fingerprint;
        experiment.FrozenStrategyParameterSnapshotJson = frozenJson;
        experiment.FrozenParameterFingerprint = fingerprint;
        experiment.IsCanonical = false;

        trial.ParameterSnapshotJson = selectedJson;
        trial.ParameterFingerprint = fingerprint;
        trial.Status = ValidationTrialStatus.Completed;
        trial.CompletedAtUtc = DateTime.UtcNow;
        trial.GuardrailDecision = "Passed";
        trial.TrialRankEligibility = ValidationTrialRankEligibility.Eligible;
        await db.SaveChangesAsync();

        var execution = E2C1AuditFixtures.NewExecution(experiment, trial);
        await services.GetRequiredService<IValidationAuditExecutionRepository>()
            .CreateAndAssignTrialAuthoritativeAsync(execution, trial);

        var eventId = Guid.NewGuid();
        var access = E2BAuditFixtures.NewAudit(
            experiment.Id,
            eventId,
            execution.ScopeExecutionId,
            1,
            "B1C6B");
        access.TrialId = trial.Id;
        access.TrialNumber = trial.TrialNumber;
        var canonicalizer = new ValidationAccessPayloadCanonicalizer();
        var payloadHash = canonicalizer.ComputeSha256(access);
        access.AccessPayloadHash = payloadHash;
        access.AccessPayloadContractVersion = ValidationAccessPayloadContractVersions.Current;
        var entries = new[]
        {
            new ValidationAuditPayloadSetEntry(
                1,
                eventId,
                payloadHash,
                ValidationAccessPayloadContractVersions.Current)
        };
        var hasher = services.GetRequiredService<IValidationAuditPayloadSetHasher>();
        var setHash = hasher.ComputeSetHash(entries);
        var (eventIdsJson, payloadHashesJson) = hasher.BuildManifestJsons(entries);
        await services.GetRequiredService<IValidationAuditBatchRepository>().AddAsync(new ValidationAuditBatch
        {
            AuditBatchId = Guid.NewGuid(),
            AuditExecutionId = execution.AuditExecutionId,
            BatchNumber = 1,
            FirstSequence = 1,
            LastSequence = 1,
            ExpectedEventCount = 1,
            ExpectedEventIdsJson = eventIdsJson,
            ExpectedPayloadHashesJson = payloadHashesJson,
            ExpectedPayloadSetHash = setHash,
            Status = ValidationAuditBatchStatus.Confirmed,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            ConfirmedAtUtc = DateTime.UtcNow,
            AuditBatchContractVersion = ValidationAuditBatch.ContractVersionV1,
            RowVersion = 1
        });
        await services.GetRequiredService<IValidationCandleAccessAuditRepository>()
            .AddRangeIdempotentByAccessEventIdAsync([access]);

        execution.LastConfirmedSequence = 1;
        execution.ConfirmedEventCount = 1;
        execution.FinalExpectedSequence = 1;
        execution.ExpectedEventCount = 1;
        execution.FinalPayloadSetHash = setHash;
        execution.Status = ValidationAuditExecutionStatus.Completed;
        execution.CompletedAtUtc = DateTime.UtcNow;
        execution.UpdatedAtUtc = DateTime.UtcNow;
        trial.AuditCompletionStatus = ValidationAuditCompletionStatus.Complete;
        await services.GetRequiredService<IValidationParameterTrialRepository>().UpdateAsync(trial);
        await services.GetRequiredService<IValidationAuditExecutionRepository>().UpdateAsync(execution);

        return new SeededPublication(
            experiment.Id,
            trial.Id,
            execution.AuditExecutionId,
            experiment.SymbolId,
            experiment.Timeframe,
            frozenJson,
            fingerprint);
    }

    internal async Task CleanupAsync(IReadOnlyList<SeededPublication> seeded)
    {
        await using var scope = _fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
        var experimentIds = seeded.Select(item => item.ExperimentId).ToArray();
        var executionIds = seeded.Select(item => item.ExecutionId).ToArray();
        var publicationIds = await db.StrategyParameterSets
            .Where(row => row.QualificationSourceExperimentId != null
                && experimentIds.Contains(row.QualificationSourceExperimentId.Value))
            .Select(row => row.Id)
            .ToListAsync();
        if (publicationIds.Count > 0)
        {
            await db.AuditLogs.Where(row =>
                row.Action == ValidationParameterSetPublicationService.AuditAction
                && row.EntityId != null
                && publicationIds.Contains(row.EntityId.Value)).ExecuteDeleteAsync();
            await db.StrategyParameterSets.Where(row => publicationIds.Contains(row.Id)).ExecuteDeleteAsync();
        }

        var strategy = await db.Strategies.SingleAsync(row =>
            row.Code == StrategyCode.PriceStructureBreakoutRetest);
        strategy.CanonicalValidationExperimentId = null;
        strategy.DeploymentQualificationEligible = false;
        await db.SaveChangesAsync();
        await db.ValidationAuditBatches.Where(row => executionIds.Contains(row.AuditExecutionId)).ExecuteDeleteAsync();
        await db.ValidationAuditExecutions.Where(row => experimentIds.Contains(row.ValidationExperimentId)).ExecuteDeleteAsync();
        await db.ValidationCandleAccessAudits.Where(row => experimentIds.Contains(row.ValidationExperimentId)).ExecuteDeleteAsync();
        await db.ValidationParameterTrials.Where(row => experimentIds.Contains(row.ValidationExperimentId)).ExecuteDeleteAsync();
        await db.ValidationExperiments.Where(row => experimentIds.Contains(row.Id)).ExecuteDeleteAsync();
    }

    private static IValidationParameterSetPublicationService BuildBarrierService(
        IServiceProvider services,
        TwoParticipantBarrier barrier) =>
        new ValidationParameterSetPublicationService(
            new BarrierPublicationStore(
                services.GetRequiredService<IValidationParameterSetPublicationStore>(),
                barrier),
            services.GetRequiredService<IValidationParameterFingerprintService>(),
            services.GetRequiredService<IValidationAuthoritativeAuditQualificationEvaluator>(),
            services.GetRequiredService<IValidationVerdictService>(),
            services.GetRequiredService<ICurrentUserService>(),
            services.GetRequiredService<TimeProvider>(),
            services.GetRequiredService<ILogger<ValidationParameterSetPublicationService>>(),
            services.GetRequiredService<IRequiredAuditWriter>());

    private static MySqlException? FindMySqlException(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException!)
        {
            if (current is MySqlException mysql)
            {
                return mysql;
            }
        }

        return null;
    }

    private static async Task<long> ScalarLongAsync(MomoQuantDbContext db, string sql)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        if (command.Connection!.State != System.Data.ConnectionState.Open)
        {
            await command.Connection.OpenAsync();
        }

        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    internal sealed record SeededPublication(
        long ExperimentId,
        long TrialId,
        Guid ExecutionId,
        long SymbolId,
        string Timeframe,
        string FrozenJson,
        string Fingerprint);

    private sealed class TwoParticipantBarrier
    {
        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrivals;

        public int Arrivals => Volatile.Read(ref _arrivals);

        public async Task ArriveAsync(CancellationToken cancellationToken)
        {
            var arrivals = Interlocked.Increment(ref _arrivals);
            if (arrivals == 2)
            {
                _release.TrySetResult();
            }
            else if (arrivals > 2)
            {
                throw new InvalidOperationException("The two-participant publication barrier was reused.");
            }

            await _release.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class BarrierPublicationStore(
        IValidationParameterSetPublicationStore inner,
        TwoParticipantBarrier barrier) : IValidationParameterSetPublicationStore
    {
        public Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken = default) =>
            inner.ExecuteInTransactionAsync(action, cancellationToken);

        public async Task<ValidationExperiment?> LockExperimentAsync(long experimentId, CancellationToken cancellationToken = default)
        {
            await barrier.ArriveAsync(cancellationToken);
            return await inner.LockExperimentAsync(experimentId, cancellationToken);
        }

        public Task<ValidationParameterTrial?> LockTrialAsync(long trialId, CancellationToken cancellationToken = default) =>
            inner.LockTrialAsync(trialId, cancellationToken);

        public Task<Strategy?> LockStrategyByCodeAsync(string strategyCode, CancellationToken cancellationToken = default) =>
            inner.LockStrategyByCodeAsync(strategyCode, cancellationToken);

        public Task<IReadOnlyList<ValidationExperiment>> ListCanonicalExperimentsAsync(string strategyCode, CancellationToken cancellationToken = default) =>
            inner.ListCanonicalExperimentsAsync(strategyCode, cancellationToken);

        public Task<StrategyParameterSet?> LockPublicationByExperimentAsync(long experimentId, CancellationToken cancellationToken = default) =>
            inner.LockPublicationByExperimentAsync(experimentId, cancellationToken);

        public Task<IReadOnlyList<StrategyParameterSet>> LockQualifiedPublicationsByStrategyAsync(string strategyCode, CancellationToken cancellationToken = default) =>
            inner.LockQualifiedPublicationsByStrategyAsync(strategyCode, cancellationToken);

        public void AddParameterSet(StrategyParameterSet parameterSet) => inner.AddParameterSet(parameterSet);
        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => inner.SaveChangesAsync(cancellationToken);
    }
}
