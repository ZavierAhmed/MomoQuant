using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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

namespace MomoQuant.IntegrationTests;

/// <summary>Milestone 23.0E2C1D — production-path bounded audit instrumentation.</summary>
[Collection("Integration")]
public sealed class Milestone230E2C1DIntegrationTests : IClassFixture<E2C1InstrumentationFactory>, IDisposable
{
    private readonly E2C1InstrumentationFactory _factory;
    private readonly E2C1AuditWriteCounters _counters;

    public Milestone230E2C1DIntegrationTests(E2C1InstrumentationFactory factory)
    {
        _factory = factory;
        _counters = factory.Counters;
    }

    public void Dispose()
    {
        _counters.ExecutionCreates = 0;
        _counters.ExecutionUpdates = 0;
        _counters.ManifestCreates = 0;
        _counters.ManifestUpdates = 0;
        _counters.AccessEventPersistCalls = 0;
        _counters.AccessRowsPersisted = 0;
        _counters.ConfirmationReadCalls = 0;
        _counters.FinalizationCalls = 0;
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

        var executionCreatesBefore = _counters.ExecutionCreates;
        var executionUpdatesBefore = _counters.ExecutionUpdates;
        var manifestCreatesBefore = _counters.ManifestCreates;
        var manifestUpdatesBefore = _counters.ManifestUpdates;
        var accessPersistBefore = _counters.AccessEventPersistCalls;
        var accessRowsBefore = _counters.AccessRowsPersisted;
        var confirmationReadsBefore = _counters.ConfirmationReadCalls;
        var finalizationBefore = _counters.FinalizationCalls;

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
                var dataSource = new ValidationTrainingStrategyLabCandleDataSource(trainingScope, "E2C1.10K");
                var labRun = new StrategyLabRun
                {
                    SymbolId = symbol.Id,
                    Symbol = symbolName,
                    Timeframe = "15m",
                    FromUtc = evalStart,
                    ToUtc = evalEnd
                };
                _ = await dataSource.LoadAsync(labRun, RequiredWarmup);

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

            var executionCreates = _counters.ExecutionCreates - executionCreatesBefore;
            var executionUpdates = _counters.ExecutionUpdates - executionUpdatesBefore;
            var manifestCreates = _counters.ManifestCreates - manifestCreatesBefore;
            var manifestUpdates = _counters.ManifestUpdates - manifestUpdatesBefore;
            var accessPersistCalls = _counters.AccessEventPersistCalls - accessPersistBefore;
            var accessRowsPersisted = _counters.AccessRowsPersisted - accessRowsBefore;
            var confirmationReads = _counters.ConfirmationReadCalls - confirmationReadsBefore;
            var finalizationCalls = _counters.FinalizationCalls - finalizationBefore;
            var persistenceRoundTrips = accessPersistCalls + confirmationReads + manifestCreates + manifestUpdates
                + executionUpdates + finalizationCalls;

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

            Assert.Equal(1, executionCreates);
            Assert.Equal(1, manifestCreates);
            Assert.Equal(1, accessPersistCalls);
            Assert.Equal(3, accessRowsPersisted);
            Assert.True(executionUpdates >= 1, "Sequence cursor must advance on flush.");
            Assert.Equal(1, finalizationCalls);
            Assert.True(confirmationReads <= 2, $"Expected bounded O(1) confirmation reads, got {confirmationReads}.");
            Assert.True(confirmationReads < EvalCount, "Must not issue per-candle confirmation queries.");
            Assert.True(batchCount < eventCount * 2, "Must not create per-candle manifests.");
            Assert.True(eventCount < EvalCount, "Must not create per-candle audit rows.");
            Assert.True(persistenceRoundTrips < 50, $"Persistence round-trips: {persistenceRoundTrips}");
            Assert.True(sw.ElapsedMilliseconds < 120_000, $"TenThousandCandle bounded audit elapsed: {sw.ElapsedMilliseconds}ms, roundTrips={persistenceRoundTrips}");
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

            var symbolEntity = await db.Symbols.FirstOrDefaultAsync(s => s.SymbolName == symbolName);
            if (symbolEntity is not null)
            {
                await db.Symbols.Where(s => s.Id == symbolEntity.Id).ExecuteDeleteAsync();
            }
        }
    }
}
