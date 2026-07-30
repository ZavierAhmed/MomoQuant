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

/// <summary>Milestone 23.1B1C — genuine factory bootstrap recovery and idempotent retry proofs.</summary>
[Collection("Integration")]
public sealed class Milestone231B1CFactoryRecoveryTests : IClassFixture<MomoQuantWebApplicationFactory>
{
    private readonly MomoQuantWebApplicationFactory _factory;

    public Milestone231B1CFactoryRecoveryTests(MomoQuantWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task AllowedBootstrap_RecoverAndRetry_IsStableAndIdempotent()
    {
        long experimentId = 0;
        var symbolName = $"B1C-OK-{Guid.NewGuid():N}"[..18];

        await using var scope = _factory.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<MomoQuantDbContext>();
        var executions = sp.GetRequiredService<IValidationAuditExecutionRepository>();
        var recorder = sp.GetRequiredService<IValidationCandleAccessRecorder>();
        var scopeFactory = sp.GetRequiredService<IValidationTrainingCandleScopeFactory>();
        var canonicalizer = new ValidationAccessPayloadCanonicalizer();

        try
        {
            var testExchange = await db.Exchanges.AsNoTracking().OrderBy(e => e.Id).FirstAsync();
            var evalStart = new DateTime(2044, 6, 1, 0, 0, 0, DateTimeKind.Utc);
            var evalEnd = evalStart.AddHours(4);
            var boundary = evalEnd.AddHours(1);

            var symbol = await SeedSymbolAsync(db, testExchange.Id, symbolName);
            await SeedLtfAndHtfCandlesAsync(db, testExchange.Id, symbol.Id, evalStart);

            var (experiment, trial) = await E2C1AuditFixtures.CreateExperimentAndTrialAsync(db, "b1c-bootstrap-ok");
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

            var scopeRequest = BuildScopeRequest(experiment, symbol, testExchange.Id, evalStart, evalEnd, boundary, execution);

            Guid bootstrapEventId;
            string payloadHash;
            await using (var trainingScope = await scopeFactory.CreateAsync(scopeRequest))
            {
                var bootstrap = Assert.Single(trainingScope.AccessLog, r =>
                    r.AccessPurpose == ValidationCandleAccessPurpose.FactoryBootstrapHtfLoad && !r.WasDenied);
                bootstrapEventId = bootstrap.AccessEventId;
                Assert.Equal(1, bootstrap.ScopeSequenceNumber);

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
            var reloadSp = reloadScope.ServiceProvider;
            var reloadDb = reloadSp.GetRequiredService<MomoQuantDbContext>();
            var recovery = reloadSp.GetRequiredService<IValidationAuditExecutionRecoveryService>();

            var persisted = await AssertSingleBootstrapRowAsync(reloadDb, experiment.Id, execution.ScopeExecutionId);
            Assert.Equal(bootstrapEventId, persisted.AccessEventId);
            payloadHash = persisted.AccessPayloadHash;
            Assert.False(string.IsNullOrWhiteSpace(payloadHash));
            Assert.Equal(canonicalizer.ComputeSha256(persisted), payloadHash);

            var recoveryResult = await recovery.RecoverAsync(execution.AuditExecutionId);
            Assert.True(recoveryResult.RecoveredLastConfirmedSequence >= 1);

            await using (var retryScope = await reloadSp.GetRequiredService<IValidationTrainingCandleScopeFactory>().CreateAsync(scopeRequest))
            {
                var retryBootstrap = Assert.Single(retryScope.AccessLog, r =>
                    r.AccessPurpose == ValidationCandleAccessPurpose.FactoryBootstrapHtfLoad && !r.WasDenied);
                Assert.Equal(bootstrapEventId, retryBootstrap.AccessEventId);
                Assert.Equal(1, retryBootstrap.ScopeSequenceNumber);

                using var ambient = ValidationAuditExecutionAmbient.Enter(new ValidationAuditExecutionAmbientContext
                {
                    AuditExecutionId = execution.AuditExecutionId,
                    ScopeExecutionId = execution.ScopeExecutionId,
                    ExecutionToken = execution.ExecutionToken,
                    AttemptNumber = execution.AttemptNumber,
                    ValidationExperimentId = experiment.Id,
                    ValidationTrialId = trial.Id
                });
                await reloadSp.GetRequiredService<IValidationCandleAccessRecorder>().FlushAsync(retryScope);
            }

            var afterRetry = await AssertSingleBootstrapRowAsync(reloadDb, experiment.Id, execution.ScopeExecutionId);
            Assert.Equal(bootstrapEventId, afterRetry.AccessEventId);
            Assert.Equal(payloadHash, afterRetry.AccessPayloadHash);
            Assert.Equal(persisted.Id, afterRetry.Id);
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
    public async Task DeniedBootstrap_RecoverAfterDispose_SingleDurableRow()
    {
        long experimentId = 0;
        var symbolName = $"B1C-DENY-{Guid.NewGuid():N}"[..18];

        await using var scope = _factory.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<MomoQuantDbContext>();
        var executions = sp.GetRequiredService<IValidationAuditExecutionRepository>();
        var scopeFactory = sp.GetRequiredService<IValidationTrainingCandleScopeFactory>();
        var canonicalizer = new ValidationAccessPayloadCanonicalizer();

        try
        {
            var testExchange = await db.Exchanges.AsNoTracking().OrderBy(e => e.Id).FirstAsync();
            var evalStart = new DateTime(2044, 7, 1, 0, 0, 0, DateTimeKind.Utc);
            var evalEnd = evalStart.AddHours(4);
            var boundary = evalEnd.AddHours(1);

            var symbol = await SeedSymbolAsync(db, testExchange.Id, symbolName);
            await SeedLtfOnlyCandlesAsync(db, testExchange.Id, symbol.Id, evalStart);

            var (experiment, trial) = await E2C1AuditFixtures.CreateExperimentAndTrialAsync(db, "b1c-bootstrap-deny");
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

            var scopeRequest = BuildScopeRequest(experiment, symbol, testExchange.Id, evalStart, evalEnd, boundary, execution);
            var factory = (ValidationTrainingCandleScopeFactory)scopeFactory;

            Guid deniedEventId;
            var ex = await Assert.ThrowsAsync<ValidationCandlePartitionViolationException>(() =>
                scopeFactory.CreateAsync(scopeRequest));
            Assert.Equal(ValidationCandlePartitionDenialCodes.MissingPartitionHtf, ex.DenialCode);

            var denied = Assert.Single(factory.LastBootstrapAccessEvidence, r => r.WasDenied);
            deniedEventId = denied.AccessEventId;
            Assert.Equal(execution.AuditExecutionId, denied.AuditExecutionId);
            Assert.Equal(1, denied.ScopeSequenceNumber);

            await using var reloadScope = _factory.Services.CreateAsyncScope();
            var reloadSp = reloadScope.ServiceProvider;
            var reloadDb = reloadSp.GetRequiredService<MomoQuantDbContext>();
            var recovery = reloadSp.GetRequiredService<IValidationAuditExecutionRecoveryService>();

            var persisted = await AssertSingleBootstrapRowAsync(reloadDb, experiment.Id, execution.ScopeExecutionId);
            Assert.Equal(deniedEventId, persisted.AccessEventId);
            Assert.True(persisted.WasDenied);
            Assert.Equal(ValidationCandlePartitionDenialCodes.MissingPartitionHtf, persisted.DenialCode);
            Assert.False(string.IsNullOrWhiteSpace(persisted.AccessPayloadHash));
            Assert.Equal(canonicalizer.ComputeSha256(persisted), persisted.AccessPayloadHash);

            var recoveryResult = await recovery.RecoverAsync(execution.AuditExecutionId);
            Assert.True(recoveryResult.RecoveredLastConfirmedSequence >= 1);
        }
        finally
        {
            if (experimentId > 0)
            {
                await E2C1AuditFixtures.CleanupAsync(_factory, experimentId);
            }
        }
    }

    private static ValidationTrainingCandleScopeRequest BuildScopeRequest(
        ValidationExperiment experiment,
        Symbol symbol,
        long exchangeId,
        DateTime evalStart,
        DateTime evalEnd,
        DateTime boundary,
        ValidationAuditExecution execution) =>
        new()
        {
            ValidationExperimentId = experiment.Id,
            SymbolId = symbol.Id,
            SymbolName = symbol.SymbolName,
            Timeframe = "5m",
            TrainingEvaluationStartUtc = evalStart,
            TrainingEvaluationEndExclusiveUtc = evalEnd,
            ValidationBoundaryUtc = boundary,
            RequiredWarmupCandleCount = 0,
            RequirementsVersion = StrategyExecutionRequirements.Version,
            StrategyId = 1,
            StrategyCode = StrategyCodes.MomoAdaptiveMultiTimeframeTrendBreakout,
            StrategyVersion = MomoAdaptiveMultiTimeframeTrendBreakoutStrategy.Version,
            ExchangeId = exchangeId,
            BoundScopeExecutionId = execution.ScopeExecutionId,
            BoundAuditExecutionId = execution.AuditExecutionId,
            BoundExecutionToken = execution.ExecutionToken,
            BoundAttemptNumber = execution.AttemptNumber
        };

    private static async Task<ValidationCandleAccessAudit> AssertSingleBootstrapRowAsync(
        MomoQuantDbContext db,
        long experimentId,
        Guid scopeExecutionId)
    {
        var rows = await db.ValidationCandleAccessAudits
            .AsNoTracking()
            .Where(a => a.ValidationExperimentId == experimentId && a.ScopeExecutionId == scopeExecutionId)
            .ToListAsync();

        var bootstrapRows = rows
            .Where(r => string.Equals(r.AccessPurpose, nameof(ValidationCandleAccessPurpose.FactoryBootstrapHtfLoad), StringComparison.Ordinal))
            .ToList();
        return Assert.Single(bootstrapRows);
    }

    private static async Task<Symbol> SeedSymbolAsync(MomoQuantDbContext db, long exchangeId, string symbolName)
    {
        var symbol = new Symbol
        {
            ExchangeId = exchangeId,
            SymbolName = symbolName,
            BaseAsset = "B1C",
            QuoteAsset = "USDT",
            ContractType = ContractType.Perpetual,
            IsActive = true
        };
        db.Symbols.Add(symbol);
        await db.SaveChangesAsync();
        return symbol;
    }

    private static async Task SeedLtfCandlesAsync(MomoQuantDbContext db, long exchangeId, long symbolId, DateTime evalStart)
    {
        for (var i = 0; i < 48; i++)
        {
            var open = evalStart.AddMinutes(i * 5);
            db.Candles.Add(new Candle
            {
                ExchangeId = exchangeId,
                SymbolId = symbolId,
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
    }

    private static async Task SeedLtfAndHtfCandlesAsync(MomoQuantDbContext db, long exchangeId, long symbolId, DateTime evalStart)
    {
        await SeedLtfCandlesAsync(db, exchangeId, symbolId, evalStart);

        for (var i = 0; i < 6; i++)
        {
            var open = evalStart.AddHours(i);
            db.Candles.Add(new Candle
            {
                ExchangeId = exchangeId,
                SymbolId = symbolId,
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
    }

    private static async Task SeedLtfOnlyCandlesAsync(MomoQuantDbContext db, long exchangeId, long symbolId, DateTime evalStart) =>
        await SeedLtfCandlesAsync(db, exchangeId, symbolId, evalStart);
}
