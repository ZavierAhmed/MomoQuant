using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Strategies;
using MomoQuant.Application.Strategies.Implementations;
using MomoQuant.Application.Strategies.MomoAdaptive;
using MomoQuant.Application.ValidationLab;
using MomoQuant.Domain.Constants;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Exchanges;
using MomoQuant.Domain.MarketData;
using MomoQuant.Domain.ValidationLab;
using MomoQuant.Persistence;

namespace MomoQuant.IntegrationTests;

/// <summary>Milestone 23.1B1B — factory bootstrap HTF evidence survives MySQL restart.</summary>
[Collection("Integration")]
public sealed class Milestone231B1BFactoryRestartTests : IClassFixture<MomoQuantWebApplicationFactory>
{
    private readonly MomoQuantWebApplicationFactory _factory;

    public Milestone231B1BFactoryRestartTests(MomoQuantWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task FactoryBootstrapDeniedEvidence_SurvivesFlushRestart_NoDuplicate()
    {
        long experimentId = 0;
        var symbolName = $"B1B-DENY-{Guid.NewGuid():N}"[..18];

        await using var scope = _factory.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<MomoQuantDbContext>();
        var executions = sp.GetRequiredService<IValidationAuditExecutionRepository>();
        var scopeFactory = sp.GetRequiredService<IValidationTrainingCandleScopeFactory>();

        try
        {
            var testExchange = await db.Exchanges.AsNoTracking().OrderBy(e => e.Id).FirstAsync();
            var evalStart = new DateTime(2044, 5, 1, 0, 0, 0, DateTimeKind.Utc);
            var evalEnd = evalStart.AddHours(4);
            var boundary = evalEnd.AddHours(1);

            var symbol = new Symbol
            {
                ExchangeId = testExchange.Id,
                SymbolName = symbolName,
                BaseAsset = "B1B",
                QuoteAsset = "USDT",
                ContractType = ContractType.Perpetual,
                IsActive = true
            };
            db.Symbols.Add(symbol);
            await db.SaveChangesAsync();

            for (var i = 0; i < 48; i++)
            {
                var open = evalStart.AddMinutes(i * 5);
                db.Candles.Add(new Candle
                {
                    ExchangeId = testExchange.Id,
                    SymbolId = symbol.Id,
                    Timeframe = Timeframe.M5,
                    OpenTimeUtc = open,
                    CloseTimeUtc = open.AddMinutes(5),
                    Open = 100m + i,
                    High = 101m + i,
                    Low = 99m + i,
                    Close = 100.5m + i,
                    Volume = 10m,
                    IsClosed = true,
                    CreatedAtUtc = DateTime.UtcNow
                });
            }

            await db.SaveChangesAsync();

            var (experiment, trial) = await E2C1AuditFixtures.CreateExperimentAndTrialAsync(db, "b1b-bootstrap-deny");
            experiment.ExchangeId = testExchange.Id;
            experiment.SymbolId = symbol.Id;
            experiment.Symbol = symbolName;
            experiment.Timeframe = "5m";
            experiment.StrategyCode = StrategyCodes.MomoAdaptiveMultiTimeframeTrendBreakout;
            experiment.StrategyVersion = MomoAdaptiveMultiTimeframeTrendBreakoutStrategy.Version;
            experiment.TrainingStartUtc = evalStart;
            experiment.TrainingEndUtc = evalEnd;
            experiment.ValidationStartUtc = boundary;
            db.ValidationExperiments.Update(experiment);
            await db.SaveChangesAsync();
            experimentId = experiment.Id;

            var execution = E2C1AuditFixtures.NewExecution(experiment, trial);
            await executions.CreateAndAssignTrialAuthoritativeAsync(execution, trial);

            var scopeRequest = new ValidationCanonicalTrainingCandleScopeRequest
            {
                Experiment = experiment,
                Requirements = new StrategyExecutionRequirements
                {
                    StrategyId = 1,
                    StrategyCode = StrategyCodes.MomoAdaptiveMultiTimeframeTrendBreakout,
                    StrategyVersion = MomoAdaptiveMultiTimeframeTrendBreakoutStrategy.Version,
                    RequiredWarmupCandleCount = 0,
                    RequiresHigherTimeframePartition = true,
                    RequiredHigherTimeframeApi = "1h",
                    HigherTimeframeMappingContractVersion = StrategyHigherTimeframeSupport.AdaptiveHtfMappingContractVersion
                },
                AuditExecution = execution,
                TrainingEvaluationEndExclusiveUtc = evalEnd
            };

            Guid deniedEventId;
            var factory = (ValidationTrainingCandleScopeFactory)scopeFactory;
            var ex = await Assert.ThrowsAsync<ValidationCandlePartitionViolationException>(() =>
                scopeFactory.CreateCanonicalAsync(scopeRequest));
            Assert.Equal(ValidationCandlePartitionDenialCodes.MissingPartitionHtf, ex.DenialCode);

            var denied = Assert.Single(factory.LastBootstrapAccessEvidence, r => r.WasDenied);
            deniedEventId = denied.AccessEventId;
            Assert.Equal(execution.AuditExecutionId, denied.AuditExecutionId);
            Assert.Equal(0, denied.ReturnedCandleCount);

            await using var reloadScope = _factory.Services.CreateAsyncScope();
            var reloadDb = reloadScope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
            var rows = await reloadDb.ValidationCandleAccessAudits
                .AsNoTracking()
                .Where(a => a.ValidationExperimentId == experiment.Id
                            && a.ScopeExecutionId == execution.ScopeExecutionId)
                .ToListAsync();

            var bootstrapRows = rows
                .Where(r => string.Equals(r.AccessPurpose, nameof(ValidationCandleAccessPurpose.FactoryBootstrapHtfLoad), StringComparison.Ordinal))
                .ToList();
            var persisted = Assert.Single(bootstrapRows);
            Assert.Equal(deniedEventId, persisted.AccessEventId);
            Assert.True(persisted.WasDenied);
            Assert.Equal(ValidationCandlePartitionDenialCodes.MissingPartitionHtf, persisted.DenialCode);
            Assert.Single(bootstrapRows);
            Assert.DoesNotContain(rows, r =>
                r.AccessEventId == deniedEventId && r.Id != persisted.Id);
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
    public async Task FactoryBootstrapHtfEvidence_SurvivesFlushRestart_NoDuplicate()
    {
        long experimentId = 0;
        var symbolName = $"B1B-{Guid.NewGuid():N}"[..18];

        await using var scope = _factory.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<MomoQuantDbContext>();
        var executions = sp.GetRequiredService<IValidationAuditExecutionRepository>();
        var recorder = sp.GetRequiredService<IValidationCandleAccessRecorder>();
        var scopeFactory = sp.GetRequiredService<IValidationTrainingCandleScopeFactory>();

        try
        {
            var testExchange = await db.Exchanges.AsNoTracking().OrderBy(e => e.Id).FirstAsync();
            var evalStart = new DateTime(2044, 4, 1, 0, 0, 0, DateTimeKind.Utc);
            var evalEnd = evalStart.AddHours(4);
            var boundary = evalEnd.AddHours(1);

            var symbol = new Symbol
            {
                ExchangeId = testExchange.Id,
                SymbolName = symbolName,
                BaseAsset = "B1B",
                QuoteAsset = "USDT",
                ContractType = ContractType.Perpetual,
                IsActive = true
            };
            db.Symbols.Add(symbol);
            await db.SaveChangesAsync();

            for (var i = 0; i < 48; i++)
            {
                var open = evalStart.AddMinutes(i * 5);
                db.Candles.Add(new Candle
                {
                    ExchangeId = testExchange.Id,
                    SymbolId = symbol.Id,
                    Timeframe = Timeframe.M5,
                    OpenTimeUtc = open,
                    CloseTimeUtc = open.AddMinutes(5),
                    Open = 100m + i,
                    High = 101m + i,
                    Low = 99m + i,
                    Close = 100.5m + i,
                    Volume = 10m,
                    IsClosed = true,
                    CreatedAtUtc = DateTime.UtcNow
                });
            }

            for (var i = 0; i < 6; i++)
            {
                var open = evalStart.AddHours(i);
                db.Candles.Add(new Candle
                {
                    ExchangeId = testExchange.Id,
                    SymbolId = symbol.Id,
                    Timeframe = Timeframe.H1,
                    OpenTimeUtc = open,
                    CloseTimeUtc = open.AddHours(1),
                    Open = 200m + i,
                    High = 201m + i,
                    Low = 199m + i,
                    Close = 200.5m + i,
                    Volume = 20m,
                    IsClosed = true,
                    CreatedAtUtc = DateTime.UtcNow
                });
            }

            await db.SaveChangesAsync();

            var (experiment, trial) = await E2C1AuditFixtures.CreateExperimentAndTrialAsync(db, "b1b-bootstrap");
            experiment.ExchangeId = testExchange.Id;
            experiment.SymbolId = symbol.Id;
            experiment.Symbol = symbolName;
            experiment.Timeframe = "5m";
            experiment.StrategyCode = StrategyCodes.MomoAdaptiveMultiTimeframeTrendBreakout;
            experiment.StrategyVersion = MomoAdaptiveMultiTimeframeTrendBreakoutStrategy.Version;
            experiment.TrainingStartUtc = evalStart;
            experiment.TrainingEndUtc = evalEnd;
            experiment.ValidationStartUtc = boundary;
            db.ValidationExperiments.Update(experiment);
            await db.SaveChangesAsync();
            experimentId = experiment.Id;

            var execution = E2C1AuditFixtures.NewExecution(experiment, trial);
            await executions.CreateAndAssignTrialAuthoritativeAsync(execution, trial);

            var scopeRequest = new ValidationCanonicalTrainingCandleScopeRequest
            {
                Experiment = experiment,
                Requirements = new StrategyExecutionRequirements
                {
                    StrategyId = 1,
                    StrategyCode = StrategyCodes.MomoAdaptiveMultiTimeframeTrendBreakout,
                    StrategyVersion = MomoAdaptiveMultiTimeframeTrendBreakoutStrategy.Version,
                    RequiredWarmupCandleCount = 0,
                    RequiresHigherTimeframePartition = true,
                    RequiredHigherTimeframeApi = "1h",
                    HigherTimeframeMappingContractVersion = StrategyHigherTimeframeSupport.AdaptiveHtfMappingContractVersion
                },
                AuditExecution = execution,
                TrainingEvaluationEndExclusiveUtc = evalEnd
            };

            Guid bootstrapEventId;
            await using (var trainingScope = await scopeFactory.CreateCanonicalAsync(scopeRequest))
            {
                var bootstrap = Assert.Single(trainingScope.AccessLog, r =>
                    r.AccessPurpose == ValidationCandleAccessPurpose.FactoryBootstrapHtfLoad && !r.WasDenied);
                bootstrapEventId = bootstrap.AccessEventId;
                Assert.Equal(execution.AuditExecutionId, bootstrap.AuditExecutionId);

                using var ambient = ValidationAuditExecutionAmbient.Enter(new ValidationAuditExecutionAmbientContext
                {
                    AuditExecutionId = execution.AuditExecutionId,
                    ScopeExecutionId = execution.ScopeExecutionId,
                    ExecutionToken = execution.ExecutionToken,
                    AttemptNumber = execution.AttemptNumber,
                    ValidationExperimentId = experiment.Id,
                    ValidationTrialId = trial.Id
                });
                await recorder.FlushAsync(trainingScope);
            }

            await using var reloadScope = _factory.Services.CreateAsyncScope();
            var reloadDb = reloadScope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
            var rows = await reloadDb.ValidationCandleAccessAudits
                .AsNoTracking()
                .Where(a => a.ValidationExperimentId == experiment.Id
                            && a.ScopeExecutionId == execution.ScopeExecutionId)
                .ToListAsync();

            var bootstrapRows = rows
                .Where(r => string.Equals(r.AccessPurpose, nameof(ValidationCandleAccessPurpose.FactoryBootstrapHtfLoad), StringComparison.Ordinal))
                .ToList();
            var persisted = Assert.Single(bootstrapRows);
            Assert.Equal(bootstrapEventId, persisted.AccessEventId);
            Assert.Equal(1, bootstrapRows.Count);
            Assert.DoesNotContain(rows, r =>
                r.AccessEventId == bootstrapEventId && r.Id != persisted.Id);
        }
        finally
        {
            if (experimentId > 0)
            {
                await E2C1AuditFixtures.CleanupAsync(_factory, experimentId);
            }
        }
    }
}
