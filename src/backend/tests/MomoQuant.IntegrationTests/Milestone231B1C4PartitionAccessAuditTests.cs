using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Strategies;
using MomoQuant.Application.ValidationLab;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.MarketData;
using MomoQuant.Domain.ValidationLab;
using MomoQuant.Persistence;

namespace MomoQuant.IntegrationTests;

[Collection("Integration")]
public sealed class Milestone231B1C4PartitionAccessAuditTests : IClassFixture<MomoQuantWebApplicationFactory>
{
    private readonly MomoQuantWebApplicationFactory _factory;

    public Milestone231B1C4PartitionAccessAuditTests(MomoQuantWebApplicationFactory factory) =>
        _factory = factory;

    [Fact]
    public async Task WrongIdentityAndWriteDenials_FlushThroughAuthoritativeAuditExecution()
    {
        await using var services = _factory.Services.CreateAsyncScope();
        var test = await CreateTestScopeAsync(services.ServiceProvider, evaluationCount: 3, "deny");
        try
        {
            using var auditAmbient = EnterAudit(test);
            using var candleAmbient = ValidationTrainingCandleScopeAmbient.Enter(test.Scope);

            var identity = await Assert.ThrowsAsync<ValidationCandlePartitionViolationException>(() =>
                test.Repository.GetCandlesAsync(2, Timeframe.M15, test.Start, test.End, 1));
            var write = await Assert.ThrowsAsync<ValidationCandlePartitionViolationException>(() =>
                test.Repository.SaveChangesAsync());

            Assert.Equal(ValidationCandlePartitionDenialCodes.SymbolMismatch, identity.DenialCode);
            Assert.Equal(ValidationCandlePartitionDenialCodes.ValidationTrainingWriteForbidden, write.DenialCode);
            await test.Recorder.FlushAsync(test.Scope);

            var expectedSequence = test.Scope.AccessLog.Max(record => record.ScopeSequenceNumber);
            var completion = await test.Finalizer.CompleteAsync(test.Execution.AuditExecutionId, expectedSequence);
            Assert.True(completion.IsComplete);

            test.Db.ChangeTracker.Clear();
            var rows = await test.Db.ValidationCandleAccessAudits.AsNoTracking()
                .Where(row => row.ValidationExperimentId == test.Experiment.Id)
                .OrderBy(row => row.ScopeSequenceNumber)
                .ToListAsync();

            Assert.Equal(2, rows.Count);
            Assert.All(rows, row =>
            {
                Assert.True(row.WasDenied);
                Assert.Equal(0, row.ReturnedCandleCount);
                Assert.Null(row.CandleContentFingerprint);
                Assert.Equal(test.Execution.ScopeExecutionId, row.ScopeExecutionId);
                Assert.Equal(test.Trial.Id, row.TrialId);
            });
            Assert.Equal(ValidationCandlePartitionDenialCodes.SymbolMismatch, rows[0].DenialCode);
            Assert.Contains("expected=1; actual=2", rows[0].DenialReason, StringComparison.Ordinal);
            Assert.StartsWith(nameof(ICandleRepository.GetCandlesAsync), rows[0].CallerComponent, StringComparison.Ordinal);
            Assert.Equal(ValidationCandlePartitionDenialCodes.ValidationTrainingWriteForbidden, rows[1].DenialCode);
            Assert.StartsWith(nameof(ICandleRepository.SaveChangesAsync), rows[1].CallerComponent, StringComparison.Ordinal);

            var execution = await test.Db.ValidationAuditExecutions.AsNoTracking()
                .SingleAsync(item => item.AuditExecutionId == test.Execution.AuditExecutionId);
            Assert.Equal(ValidationAuditExecutionStatus.Completed, execution.Status);
        }
        finally
        {
            await test.Scope.DisposeAsync();
            await E2C1AuditFixtures.CleanupAsync(_factory, test.Experiment.Id);
        }
    }

    [Fact]
    public async Task ExactSubsetAndCombinedCountEvidence_PersistExactFingerprints()
    {
        await using var services = _factory.Services.CreateAsyncScope();
        var test = await CreateTestScopeAsync(services.ServiceProvider, evaluationCount: 4, "allowed");
        try
        {
            using var auditAmbient = EnterAudit(test);
            using var candleAmbient = ValidationTrainingCandleScopeAmbient.Enter(test.Scope);

            var subset = await test.Repository.GetCandlesAsync(1, Timeframe.M15, test.Start, test.End, 2);
            var count = await test.Repository.CountCandlesAsync(1, Timeframe.M15);
            await test.Recorder.FlushAsync(test.Scope);

            Assert.Equal(2, subset.Count);
            Assert.Equal(test.AllCandles.Count, count);

            test.Db.ChangeTracker.Clear();
            var rows = await test.Db.ValidationCandleAccessAudits.AsNoTracking()
                .Where(row => row.ValidationExperimentId == test.Experiment.Id)
                .OrderBy(row => row.ScopeSequenceNumber)
                .ToListAsync();

            Assert.Equal(2, rows.Count);
            Assert.False(rows[0].WasDenied);
            Assert.Equal(2, rows[0].RequestedCandleCount);
            Assert.Equal(2, rows[0].ReturnedCandleCount);
            Assert.Equal("EvaluationPartial", rows[0].DatasetPartition);
            Assert.Equal(ValidationTrainingCandleScope.ComputeContentFingerprint(subset), rows[0].CandleContentFingerprint);

            Assert.False(rows[1].WasDenied);
            Assert.Equal(test.AllCandles.Count, rows[1].RequestedCandleCount);
            Assert.Equal(test.AllCandles.Count, rows[1].ReturnedCandleCount);
            Assert.Equal("Combined", rows[1].DatasetPartition);
            Assert.Equal(ValidationTrainingCandleScope.ComputeContentFingerprint(test.AllCandles), rows[1].CandleContentFingerprint);
        }
        finally
        {
            await test.Scope.DisposeAsync();
            await E2C1AuditFixtures.CleanupAsync(_factory, test.Experiment.Id);
        }
    }

    [Fact]
    public async Task TenThousandCandleCombinedCount_PersistsOneLogicalAuditRow()
    {
        await using var services = _factory.Services.CreateAsyncScope();
        var test = await CreateTestScopeAsync(services.ServiceProvider, evaluationCount: 10_000, "bounded");
        try
        {
            using var auditAmbient = EnterAudit(test);
            using var candleAmbient = ValidationTrainingCandleScopeAmbient.Enter(test.Scope);

            Assert.Equal(10_002, await test.Repository.CountCandlesAsync(1, Timeframe.M15));
            Assert.Single(test.Scope.AccessLog);
            await test.Recorder.FlushAsync(test.Scope);

            test.Db.ChangeTracker.Clear();
            var rows = await test.Db.ValidationCandleAccessAudits.AsNoTracking()
                .Where(row => row.ValidationExperimentId == test.Experiment.Id)
                .ToListAsync();
            var row = Assert.Single(rows);
            Assert.Equal(10_002, row.ReturnedCandleCount);
            Assert.Equal("Combined", row.DatasetPartition);
            Assert.NotNull(row.CandleContentFingerprint);
        }
        finally
        {
            await test.Scope.DisposeAsync();
            await E2C1AuditFixtures.CleanupAsync(_factory, test.Experiment.Id);
        }
    }

    private static IDisposable EnterAudit(TestScope test) =>
        ValidationAuditExecutionAmbient.Enter(new ValidationAuditExecutionAmbientContext
        {
            AuditExecutionId = test.Execution.AuditExecutionId,
            ScopeExecutionId = test.Execution.ScopeExecutionId,
            ExecutionToken = test.Execution.ExecutionToken,
            AttemptNumber = test.Execution.AttemptNumber,
            ValidationExperimentId = test.Experiment.Id,
            ValidationTrialId = test.Trial.Id
        });

    private static async Task<TestScope> CreateTestScopeAsync(
        IServiceProvider services,
        int evaluationCount,
        string suffix)
    {
        var db = services.GetRequiredService<MomoQuantDbContext>();
        var (experiment, trial) = await E2C1AuditFixtures.CreateExperimentAndTrialAsync(db, $"b1c4-{suffix}");
        var execution = E2C1AuditFixtures.NewExecution(experiment, trial);
        await services.GetRequiredService<IValidationAuditExecutionRepository>()
            .CreateAndAssignTrialAuthoritativeAsync(execution, trial);

        var start = new DateTime(2045, 1, 1, 1, 0, 0, DateTimeKind.Utc);
        var warmup = new[]
        {
            CreateCandle(1, start.AddMinutes(-30)),
            CreateCandle(2, start.AddMinutes(-15))
        };
        var evaluation = Enumerable.Range(0, evaluationCount)
            .Select(index => CreateCandle(index + 3, start.AddMinutes(index * 15d)))
            .ToArray();
        var end = start.AddMinutes(evaluationCount * 15d);
        var all = warmup.Concat(evaluation).ToArray();
        var partition = ValidationTrainingCandleScope.BuildPartition(
            experiment.Id,
            symbolId: 1,
            symbolName: "BTCUSDT",
            timeframe: "15m",
            requiredWarmup: warmup.Length,
            availableWarmup: warmup.Length,
            evaluationCount: evaluation.Length,
            status: ValidationWarmupStatus.Complete,
            evalStart: start,
            evalEndExclusive: end,
            boundary: end.AddMinutes(15),
            requirementsVersion: StrategyExecutionRequirements.Version,
            warmup: warmup,
            evaluation: evaluation,
            combined: all,
            exchangeId: 1);
        var candleScope = new ValidationTrainingCandleScope(
            partition,
            warmup,
            evaluation,
            scopeExecutionId: execution.ScopeExecutionId,
            boundAuditExecutionId: execution.AuditExecutionId,
            exchangeId: 1)
        {
            ActiveTrialId = trial.Id,
            ActiveTrialNumber = trial.TrialNumber,
            CorrelationId = $"b1c4-{suffix}"
        };

        return new TestScope(
            db,
            experiment,
            trial,
            execution,
            candleScope,
            services.GetRequiredService<ICandleRepository>(),
            services.GetRequiredService<IValidationCandleAccessRecorder>(),
            services.GetRequiredService<IValidationAuditExecutionFinalizer>(),
            start,
            end,
            all);
    }

    private static Candle CreateCandle(long id, DateTime open) => new()
    {
        Id = id,
        ExchangeId = 1,
        SymbolId = 1,
        Timeframe = Timeframe.M15,
        OpenTimeUtc = open,
        CloseTimeUtc = open.AddMinutes(15),
        Open = 100 + id,
        High = 101 + id,
        Low = 99 + id,
        Close = 100.5m + id,
        Volume = 10 + id,
        IsClosed = true,
        CreatedAtUtc = DateTime.UtcNow
    };

    private sealed record TestScope(
        MomoQuantDbContext Db,
        ValidationExperiment Experiment,
        ValidationParameterTrial Trial,
        ValidationAuditExecution Execution,
        ValidationTrainingCandleScope Scope,
        ICandleRepository Repository,
        IValidationCandleAccessRecorder Recorder,
        IValidationAuditExecutionFinalizer Finalizer,
        DateTime Start,
        DateTime End,
        IReadOnlyList<Candle> AllCandles);
}
