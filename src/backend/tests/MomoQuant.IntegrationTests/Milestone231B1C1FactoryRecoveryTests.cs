using Microsoft.EntityFrameworkCore;

using Microsoft.Extensions.Configuration;

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



/// <summary>

/// Milestone 23.1B1C1 — genuine isolated-provider factory bootstrap recovery and idempotent retry proofs.

/// Tested execution services must not reuse the integration fixture's DI scope.

/// </summary>

[Collection("Integration")]

public sealed class Milestone231B1C1FactoryRecoveryTests : IClassFixture<MomoQuantWebApplicationFactory>

{

    private readonly MomoQuantWebApplicationFactory _factory;



    public Milestone231B1C1FactoryRecoveryTests(MomoQuantWebApplicationFactory factory) => _factory = factory;



    [Fact]

    public async Task AllowedBootstrap_RecoverAndRetry_IsStableAndIdempotent()

    {

        var connection = IntegrationDatabaseConnectionResolver.Resolve().ConnectionString;

        long experimentId = 0;

        var symbolName = $"B1C1-OK-{Guid.NewGuid():N}"[..18];



        try

        {

            Guid bootstrapEventId;

            string payloadHash;

            ValidationCanonicalTrainingCandleScopeRequest scopeRequest;



            await using (var provider1 = BuildIsolatedProvider(connection))

            {

                await using var scope1 = provider1.CreateAsyncScope();

                var sp1 = scope1.ServiceProvider;

                var db1 = sp1.GetRequiredService<MomoQuantDbContext>();

                var executions1 = sp1.GetRequiredService<IValidationAuditExecutionRepository>();

                var recorder1 = sp1.GetRequiredService<IValidationCandleAccessRecorder>();

                var scopeFactory1 = sp1.GetRequiredService<IValidationTrainingCandleScopeFactory>();

                var canonicalizer = sp1.GetRequiredService<IValidationAccessPayloadCanonicalizer>();



                var testExchange = await db1.Exchanges.AsNoTracking().OrderBy(e => e.Id).FirstAsync();

                var evalStart = new DateTime(2044, 8, 1, 0, 0, 0, DateTimeKind.Utc);

                var evalEnd = evalStart.AddHours(4);

                var boundary = evalEnd.AddHours(1);



                var symbol = await SeedSymbolAsync(db1, testExchange.Id, symbolName);

                await SeedLtfAndHtfCandlesAsync(db1, testExchange.Id, symbol.Id, evalStart);



                var (experiment, trial) = await E2C1AuditFixtures.CreateExperimentAndTrialAsync(db1, "b1c1-bootstrap-ok");

                experiment.ExchangeId = testExchange.Id;

                experiment.SymbolId = symbol.Id;

                experiment.Symbol = symbolName;

                experiment.Timeframe = "5m";

                experiment.StrategyCode = StrategyCodes.MomoAdaptiveMultiTimeframeTrendBreakout;

                experiment.StrategyVersion = MomoAdaptiveMultiTimeframeTrendBreakoutStrategy.Version;

                experiment.TrainingStartUtc = evalStart;

                experiment.TrainingEndUtc = evalEnd;

                experiment.ValidationStartUtc = boundary;

                db1.ValidationExperiments.Update(experiment);

                await db1.SaveChangesAsync();

                experimentId = experiment.Id;



                var execution = E2C1AuditFixtures.NewExecution(experiment, trial);

                await executions1.CreateAndAssignTrialAuthoritativeAsync(execution, trial);



                var requirements = await ResolveRequirementsAsync(sp1, experiment);

                scopeRequest = BuildCanonicalScopeRequest(experiment, requirements, evalEnd, execution, trial);



                await using (var trainingScope = await scopeFactory1.CreateCanonicalAsync(scopeRequest))

                {

                    var bootstrap = Assert.Single(trainingScope.AccessLog, r =>

                        r.AccessPurpose == ValidationCandleAccessPurpose.FactoryBootstrapHtfLoad && !r.WasDenied);

                    bootstrapEventId = bootstrap.AccessEventId;

                    Assert.Equal(1, bootstrap.ScopeSequenceNumber);

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

                    await recorder1.FlushAsync(trainingScope);

                }



                var persisted = await AssertSingleBootstrapRowAsync(db1, experiment.Id, execution.ScopeExecutionId);

                Assert.Equal(bootstrapEventId, persisted.AccessEventId);

                payloadHash = persisted.AccessPayloadHash;

                Assert.False(string.IsNullOrWhiteSpace(payloadHash));

                Assert.Equal(canonicalizer.ComputeSha256(persisted), payloadHash);

            }



            await using (var provider2 = BuildIsolatedProvider(connection))

            {

                await using var scope2 = provider2.CreateAsyncScope();

                var sp2 = scope2.ServiceProvider;

                var db2 = sp2.GetRequiredService<MomoQuantDbContext>();

                var recovery = sp2.GetRequiredService<IValidationAuditExecutionRecoveryService>();

                var scopeFactory2 = sp2.GetRequiredService<IValidationTrainingCandleScopeFactory>();

                var recorder2 = sp2.GetRequiredService<IValidationCandleAccessRecorder>();

                var canonicalizer = sp2.GetRequiredService<IValidationAccessPayloadCanonicalizer>();



                var experiment = await db2.ValidationExperiments.AsNoTracking().FirstAsync(e => e.Id == experimentId);

                var trial = await db2.ValidationParameterTrials.AsNoTracking()

                    .FirstAsync(t => t.ValidationExperimentId == experimentId && t.TrialNumber == 1);

                var execution = await db2.ValidationAuditExecutions.AsNoTracking()

                    .FirstAsync(e => e.ValidationExperimentId == experimentId);



                var requirements = await ResolveRequirementsAsync(sp2, experiment);

                scopeRequest = BuildCanonicalScopeRequest(
                    experiment,
                    requirements,
                    DateTime.SpecifyKind(experiment.TrainingEndUtc!.Value, DateTimeKind.Utc),
                    execution,
                    trial);



                var recoveryResult = await recovery.RecoverAsync(execution.AuditExecutionId);

                Assert.True(recoveryResult.RecoveredLastConfirmedSequence >= 1);



                await using (var retryScope = await scopeFactory2.CreateCanonicalAsync(scopeRequest))

                {

                    var retryBootstrap = Assert.Single(retryScope.AccessLog, r =>

                        r.AccessPurpose == ValidationCandleAccessPurpose.FactoryBootstrapHtfLoad && !r.WasDenied);

                    Assert.Equal(bootstrapEventId, retryBootstrap.AccessEventId);

                    Assert.Equal(1, retryBootstrap.ScopeSequenceNumber);

                    Assert.Equal(execution.AuditExecutionId, retryBootstrap.AuditExecutionId);



                    using var ambient = ValidationAuditExecutionAmbient.Enter(new ValidationAuditExecutionAmbientContext

                    {

                        AuditExecutionId = execution.AuditExecutionId,

                        ScopeExecutionId = execution.ScopeExecutionId,

                        ExecutionToken = execution.ExecutionToken,

                        AttemptNumber = execution.AttemptNumber,

                        ValidationExperimentId = experiment.Id,

                        ValidationTrialId = trial.Id

                    });

                    await recorder2.FlushAsync(retryScope);

                }



                var afterRetry = await AssertSingleBootstrapRowAsync(db2, experiment.Id, execution.ScopeExecutionId);

                Assert.Equal(bootstrapEventId, afterRetry.AccessEventId);

                Assert.Equal(payloadHash, afterRetry.AccessPayloadHash);

                Assert.Equal(canonicalizer.ComputeSha256(afterRetry), afterRetry.AccessPayloadHash);

                Assert.False(afterRetry.WasDenied);

            }

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

    public async Task DeniedBootstrap_RecoverAfterDispose_SingleDurableRowAndStableRetryDenial()

    {

        var connection = IntegrationDatabaseConnectionResolver.Resolve().ConnectionString;

        long experimentId = 0;

        var symbolName = $"B1C1-DENY-{Guid.NewGuid():N}"[..18];



        try

        {

            Guid deniedEventId;

            string payloadHash;

            ValidationCanonicalTrainingCandleScopeRequest scopeRequest;



            await using (var provider1 = BuildIsolatedProvider(connection))

            {

                await using var scope1 = provider1.CreateAsyncScope();

                var sp1 = scope1.ServiceProvider;

                var db1 = sp1.GetRequiredService<MomoQuantDbContext>();

                var executions1 = sp1.GetRequiredService<IValidationAuditExecutionRepository>();

                var scopeFactory1 = sp1.GetRequiredService<IValidationTrainingCandleScopeFactory>();

                var canonicalizer = sp1.GetRequiredService<IValidationAccessPayloadCanonicalizer>();



                var testExchange = await db1.Exchanges.AsNoTracking().OrderBy(e => e.Id).FirstAsync();

                var evalStart = new DateTime(2044, 9, 1, 0, 0, 0, DateTimeKind.Utc);

                var evalEnd = evalStart.AddHours(4);

                var boundary = evalEnd.AddHours(1);



                var symbol = await SeedSymbolAsync(db1, testExchange.Id, symbolName);

                await SeedLtfOnlyCandlesAsync(db1, testExchange.Id, symbol.Id, evalStart);



                var (experiment, trial) = await E2C1AuditFixtures.CreateExperimentAndTrialAsync(db1, "b1c1-bootstrap-deny");

                experiment.ExchangeId = testExchange.Id;

                experiment.SymbolId = symbol.Id;

                experiment.Symbol = symbolName;

                experiment.Timeframe = "5m";

                experiment.StrategyCode = StrategyCodes.MomoAdaptiveMultiTimeframeTrendBreakout;

                experiment.StrategyVersion = MomoAdaptiveMultiTimeframeTrendBreakoutStrategy.Version;

                experiment.TrainingStartUtc = evalStart;

                experiment.TrainingEndUtc = evalEnd;

                experiment.ValidationStartUtc = boundary;

                db1.ValidationExperiments.Update(experiment);

                await db1.SaveChangesAsync();

                experimentId = experiment.Id;



                var execution = E2C1AuditFixtures.NewExecution(experiment, trial);

                await executions1.CreateAndAssignTrialAuthoritativeAsync(execution, trial);



                var requirements = await ResolveRequirementsAsync(sp1, experiment);

                scopeRequest = BuildCanonicalScopeRequest(experiment, requirements, evalEnd, execution, trial);



                var factory = (ValidationTrainingCandleScopeFactory)scopeFactory1;

                var ex = await Assert.ThrowsAsync<ValidationCandlePartitionViolationException>(() =>

                    scopeFactory1.CreateCanonicalAsync(scopeRequest));

                Assert.Equal(ValidationCandlePartitionDenialCodes.MissingPartitionHtf, ex.DenialCode);



                var denied = Assert.Single(factory.LastBootstrapAccessEvidence, r => r.WasDenied);

                deniedEventId = denied.AccessEventId;

                Assert.Equal(execution.AuditExecutionId, denied.AuditExecutionId);

                Assert.Equal(1, denied.ScopeSequenceNumber);



                var persisted = await AssertSingleBootstrapRowAsync(db1, experiment.Id, execution.ScopeExecutionId);

                Assert.Equal(deniedEventId, persisted.AccessEventId);

                Assert.True(persisted.WasDenied);

                Assert.Equal(ValidationCandlePartitionDenialCodes.MissingPartitionHtf, persisted.DenialCode);

                payloadHash = persisted.AccessPayloadHash;

                Assert.False(string.IsNullOrWhiteSpace(payloadHash));

                Assert.Equal(canonicalizer.ComputeSha256(persisted), payloadHash);

            }



            await using (var provider2 = BuildIsolatedProvider(connection))

            {

                await using var scope2 = provider2.CreateAsyncScope();

                var sp2 = scope2.ServiceProvider;

                var db2 = sp2.GetRequiredService<MomoQuantDbContext>();

                var recovery = sp2.GetRequiredService<IValidationAuditExecutionRecoveryService>();

                var scopeFactory2 = sp2.GetRequiredService<IValidationTrainingCandleScopeFactory>();



                var experiment = await db2.ValidationExperiments.AsNoTracking().FirstAsync(e => e.Id == experimentId);

                var trial = await db2.ValidationParameterTrials.AsNoTracking()

                    .FirstAsync(t => t.ValidationExperimentId == experimentId && t.TrialNumber == 1);

                var execution = await db2.ValidationAuditExecutions.AsNoTracking()

                    .FirstAsync(e => e.ValidationExperimentId == experimentId);



                var requirements = await ResolveRequirementsAsync(sp2, experiment);

                scopeRequest = BuildCanonicalScopeRequest(
                    experiment,
                    requirements,
                    DateTime.SpecifyKind(experiment.TrainingEndUtc!.Value, DateTimeKind.Utc),
                    execution,
                    trial);



                var recoveryResult = await recovery.RecoverAsync(execution.AuditExecutionId);

                Assert.True(recoveryResult.RecoveredLastConfirmedSequence >= 1);



                var factory = (ValidationTrainingCandleScopeFactory)scopeFactory2;

                var retryEx = await Assert.ThrowsAsync<ValidationCandlePartitionViolationException>(() =>

                    scopeFactory2.CreateCanonicalAsync(scopeRequest));

                Assert.Equal(ValidationCandlePartitionDenialCodes.MissingPartitionHtf, retryEx.DenialCode);



                var retryDenied = Assert.Single(factory.LastBootstrapAccessEvidence, r => r.WasDenied);

                Assert.Equal(deniedEventId, retryDenied.AccessEventId);

                Assert.Equal(1, retryDenied.ScopeSequenceNumber);



                var afterRetry = await AssertSingleBootstrapRowAsync(db2, experiment.Id, execution.ScopeExecutionId);

                Assert.Equal(deniedEventId, afterRetry.AccessEventId);

                Assert.Equal(payloadHash, afterRetry.AccessPayloadHash);

                Assert.True(afterRetry.WasDenied);

                Assert.Equal(ValidationCandlePartitionDenialCodes.MissingPartitionHtf, afterRetry.DenialCode);

            }

        }

        finally

        {

            if (experimentId > 0)

            {

                await E2C1AuditFixtures.CleanupAsync(_factory, experimentId);

            }

        }

    }



    private static ServiceProvider BuildIsolatedProvider(string connection)

    {

        IntegrationDatabaseSafety.AssertDisposableTestDatabase(connection);



        var config = new ConfigurationBuilder()

            .AddInMemoryCollection(new Dictionary<string, string?>

            {

                ["ConnectionStrings:DefaultConnection"] = connection

            })

            .Build();



        var services = new ServiceCollection();

        services.AddSingleton<IConfiguration>(config);

        services.AddPersistence(config);

        services.AddScoped<IStrategyDataRequirementService, StrategyDataRequirementService>();

        services.AddScoped<IStrategyExecutionRequirementsResolver, StrategyExecutionRequirementsResolver>();

        services.AddScoped<IValidationTrainingCandleScopeFactory, ValidationTrainingCandleScopeFactory>();

        services.AddSingleton<IValidationAuditPayloadSetHasher, ValidationAuditPayloadSetHasher>();

        services.AddScoped<IValidationAuditCompletenessVerifier, ValidationAuditCompletenessVerifier>();

        services.AddScoped<IValidationAuditExecutionFactory, ValidationAuditExecutionService>();

        services.AddScoped<IValidationAuditExecutionSupersessionService, ValidationAuditExecutionSupersessionService>();

        services.AddScoped<IValidationAuditExecutionRecoveryService, ValidationAuditExecutionRecoveryService>();

        services.AddScoped<IValidationAuditExecutionFinalizer, ValidationAuditExecutionFinalizer>();

        services.AddScoped<IValidationCandleAccessRecorder, ValidationCandleAccessRecorder>();

        services.AddScoped<IValidationAccessPayloadCanonicalizer, ValidationAccessPayloadCanonicalizer>();

        return services.BuildServiceProvider();

    }



    private static async Task<StrategyExecutionRequirements> ResolveRequirementsAsync(

        IServiceProvider sp,

        ValidationExperiment experiment)

    {

        var resolver = sp.GetRequiredService<IStrategyExecutionRequirementsResolver>();

        var result = await resolver.ResolveAsync(new ResolveStrategyExecutionRequirementsRequest

        {

            StrategyCode = experiment.StrategyCode,

            StrategyVersion = experiment.StrategyVersion

        });



        if (!result.Succeeded || result.Data is null)

        {

            throw new InvalidOperationException(result.ErrorMessage ?? "Failed to resolve strategy execution requirements.");

        }



        var resolved = result.Data;

        Assert.True(resolved.StrategyId > 0);

        Assert.True(resolved.RequiresHigherTimeframePartition);

        Assert.False(string.IsNullOrWhiteSpace(resolved.RequiredHigherTimeframeApi));



        return new StrategyExecutionRequirements
        {
            StrategyId = resolved.StrategyId,
            StrategyCode = resolved.StrategyCode,
            StrategyName = resolved.StrategyName,
            StrategyVersion = resolved.StrategyVersion,
            RequiredWarmupCandleCount = 0,
            RequirementsVersion = resolved.RequirementsVersion,
            RequiredIndicators = resolved.RequiredIndicators,
            PreferredTimeframes = resolved.PreferredTimeframes,
            PreferredExecutionTimeframe = resolved.PreferredExecutionTimeframe,
            RequiresHigherTimeframePartition = resolved.RequiresHigherTimeframePartition,
            RequiredHigherTimeframeApi = resolved.RequiredHigherTimeframeApi,
            RequiredDataTimeframes = resolved.RequiredDataTimeframes,
            HigherTimeframeFilters = resolved.HigherTimeframeFilters,
            HigherTimeframeMappingContractVersion = resolved.HigherTimeframeMappingContractVersion
        };

    }



    private static ValidationCanonicalTrainingCandleScopeRequest BuildCanonicalScopeRequest(

        ValidationExperiment experiment,

        StrategyExecutionRequirements requirements,

        DateTime evalEnd,

        ValidationAuditExecution execution,

        ValidationParameterTrial trial) =>

        new()

        {

            Experiment = experiment,

            Requirements = requirements,

            AuditExecution = execution,

            Trial = trial,

            TrainingEvaluationEndExclusiveUtc = DateTime.SpecifyKind(evalEnd, DateTimeKind.Utc)

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

            BaseAsset = "B1C1",

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
