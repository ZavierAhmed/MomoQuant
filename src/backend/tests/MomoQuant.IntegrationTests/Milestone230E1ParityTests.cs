using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MomoQuant.Application.Backtesting;
using MomoQuant.Application.Strategies;
using MomoQuant.Application.StrategyLab;
using MomoQuant.Application.ValidationLab;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Exchanges;
using MomoQuant.Domain.Indicators;
using MomoQuant.Domain.MarketData;
using MomoQuant.Domain.StrategyLab;
using MomoQuant.Persistence;

namespace MomoQuant.IntegrationTests;

/// <summary>
/// Milestone 23.0E1 — Integration tests for validation/normal production parity.
/// Ensures ValidationTrainingCandleScope + StandardStrategyLabCandleDataSource
/// produce identical datasets to BacktestDataLoader for the same candles.
/// </summary>
[Collection("Integration")]
public sealed class Milestone230E1ParityTests
{
    private static readonly DateTime EvalStart = new(2041, 4, 1, 0, 0, 0, DateTimeKind.Utc);
    private const int RequiredWarmup = 50;
    private const int AvailableWarmup = 80;
    private const int EvalCount = 100;
    private const long TestSymbolId = 301;
    private const string TestSymbol = "PARITYETH";

    [Fact]
    public async Task NormalAndValidationProductionSources_ReturnIdenticalDataset()
    {
        await using var factory = new MomoQuantWebApplicationFactory();
        long? experimentId = null;
        var candleIds = new List<long>();

        try
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var sp = scope.ServiceProvider;
            var db = sp.GetRequiredService<MomoQuantDbContext>();

            var testExchange = await db.Exchanges.AsNoTracking().OrderBy(e => e.Id).FirstAsync();
            var evalEnd = EvalStart.AddMinutes(EvalCount * 15);

            var symbol = new Symbol
            {
                ExchangeId = testExchange.Id,
                SymbolName = TestSymbol,
                BaseAsset = "PARITY",
                QuoteAsset = "ETH",
                ContractType = ContractType.Perpetual,
                IsActive = true
            };
            db.Symbols.Add(symbol);
            await db.SaveChangesAsync();

            var warmupStart = EvalStart.AddMinutes(-AvailableWarmup * 15);
            var allCandles = new List<Candle>();
            for (var i = 0; i < AvailableWarmup + EvalCount; i++)
            {
                var open = warmupStart.AddMinutes(i * 15);
                allCandles.Add(new Candle
                {
                    ExchangeId = testExchange.Id,
                    SymbolId = symbol.Id,
                    Timeframe = Timeframe.M15,
                    OpenTimeUtc = open,
                    CloseTimeUtc = open.AddMinutes(15),
                    Open = 1000m + i,
                    High = 1001m + i,
                    Low = 999m + i,
                    Close = 1000.5m + i,
                    Volume = 100m + i,
                    IsClosed = true,
                    CreatedAtUtc = DateTime.UtcNow
                });
            }
            db.Candles.AddRange(allCandles);
            await db.SaveChangesAsync();
            candleIds.AddRange(allCandles.Select(c => c.Id));

            // Reload from DB so validation partition uses the same decimal scale as BacktestDataLoader.
            var dbCandles = await db.Candles
                .AsNoTracking()
                .Where(c => c.SymbolId == symbol.Id && c.Timeframe == Timeframe.M15)
                .OrderBy(c => c.OpenTimeUtc)
                .ToListAsync();

            var dataLoader = sp.GetRequiredService<IBacktestDataLoader>();
            var normalDataset = await dataLoader.LoadSymbolTimeframeAsync(
                testExchange.Id,
                symbol.Id,
                Timeframe.M15,
                EvalStart,
                evalEnd,
                RequiredWarmup,
                StrategyLabCandleLoadContractVersions.ExactExclusiveV2);

            Assert.NotNull(normalDataset);
            Assert.Equal(RequiredWarmup + EvalCount, normalDataset.Candles.Count);

            var warmup = dbCandles.Where(c => c.OpenTimeUtc < EvalStart).TakeLast(RequiredWarmup).ToList();
            var evaluation = dbCandles.Where(c => c.OpenTimeUtc >= EvalStart && c.OpenTimeUtc < evalEnd).ToList();

            var partition = ValidationTrainingCandleScope.BuildPartition(
                validationExperimentId: 999,
                symbolId: symbol.Id,
                symbolName: TestSymbol,
                timeframe: "15m",
                requiredWarmup: RequiredWarmup,
                availableWarmup: warmup.Count,
                evaluationCount: evaluation.Count,
                status: ValidationWarmupStatus.Complete,
                evalStart: EvalStart,
                evalEndExclusive: evalEnd,
                boundary: evalEnd,
                requirementsVersion: StrategyExecutionRequirements.Version,
                warmup: warmup,
                evaluation: evaluation,
                combined: warmup.Concat(evaluation).ToList());

            var validationScope = new ValidationTrainingCandleScope(partition, warmup, evaluation);
            var validationSource = new ValidationTrainingStrategyLabCandleDataSource(validationScope, "ParityTest");

            var run = new StrategyLabRun
            {
                Id = 1,
                Name = "parity-test",
                StrategyCode = "PRICE_STRUCTURE_BREAKOUT_RETEST",
                StrategyVersion = "1.0.0",
                ExchangeId = testExchange.Id,
                SymbolId = symbol.Id,
                Symbol = TestSymbol,
                Timeframe = "15m",
                FromUtc = EvalStart,
                ToUtc = evalEnd,
                ExecutionMode = StrategyLabExecutionMode.RawStrategy,
                ParametersJson = "{}",
                InitialBalance = 10_000m,
                FeeSettingsJson = "{}",
                SlippageSettingsJson = "{}",
                Status = StrategyLabRunStatus.Created,
                CandleLoadContractVersion = StrategyLabCandleLoadContractVersions.ExactExclusiveV2,
                CreatedAtUtc = DateTime.UtcNow
            };

            var validationDataset = await validationSource.LoadAsync(run, RequiredWarmup);

            Assert.Equal(normalDataset.Candles.Count, validationDataset.Candles.Count);
            Assert.Equal(normalDataset.EvaluationIndices.Count, validationDataset.EvaluationIndices.Count);
            Assert.Equal(RequiredWarmup, validationDataset.WarmupCandleCount);

            for (var i = 0; i < normalDataset.Candles.Count; i++)
            {
                var normal = normalDataset.Candles[i];
                var validation = validationDataset.Candles[i];
                Assert.Equal(normal.OpenTimeUtc, validation.OpenTimeUtc);
                Assert.Equal(normal.Open, validation.Open);
                Assert.Equal(normal.High, validation.High);
                Assert.Equal(normal.Low, validation.Low);
                Assert.Equal(normal.Close, validation.Close);
                Assert.Equal(normal.Volume, validation.Volume);
            }

            var normalFingerprint = ValidationTrainingCandleScope.ComputeContentFingerprint(normalDataset.Candles);
            Assert.Equal(normalFingerprint, validationDataset.CombinedContentFingerprint);

            var accessLog = validationScope.AccessLog;
            Assert.Contains(accessLog, a => a.AccessPurpose == ValidationCandleAccessPurpose.WarmupLoad && !a.WasDenied);
            Assert.Contains(accessLog, a => a.AccessPurpose == ValidationCandleAccessPurpose.EvaluationLoad && !a.WasDenied);
            Assert.Contains(accessLog, a => a.AccessPurpose == ValidationCandleAccessPurpose.DatasetMaterialization && !a.WasDenied);
        }
        finally
        {
            await CleanupAsync(factory, experimentId, candleIds);
        }
    }

    [Fact]
    public async Task ExactEndCandle_IsExcluded()
    {
        await using var factory = new MomoQuantWebApplicationFactory();
        var candleIds = new List<long>();

        try
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var sp = scope.ServiceProvider;
            var db = sp.GetRequiredService<MomoQuantDbContext>();

            var testExchange = await db.Exchanges.AsNoTracking().OrderBy(e => e.Id).FirstAsync();
            var evalEnd = EvalStart.AddMinutes(10 * 15);

            var symbol = new Symbol
            {
                ExchangeId = testExchange.Id,
                SymbolName = $"{TestSymbol}_EXACT",
                BaseAsset = "PARITY",
                QuoteAsset = "ETH",
                ContractType = ContractType.Perpetual,
                IsActive = true
            };
            db.Symbols.Add(symbol);
            await db.SaveChangesAsync();

            var warmupStart = EvalStart.AddMinutes(-10 * 15);
            var allCandles = new List<Candle>();
            for (var i = 0; i < 25; i++)
            {
                var open = warmupStart.AddMinutes(i * 15);
                allCandles.Add(new Candle
                {
                    ExchangeId = testExchange.Id,
                    SymbolId = symbol.Id,
                    Timeframe = Timeframe.M15,
                    OpenTimeUtc = open,
                    CloseTimeUtc = open.AddMinutes(15),
                    Open = 2000m + i,
                    High = 2001m + i,
                    Low = 1999m + i,
                    Close = 2000.5m + i,
                    Volume = 200m + i,
                    IsClosed = true,
                    CreatedAtUtc = DateTime.UtcNow
                });
            }
            db.Candles.AddRange(allCandles);
            await db.SaveChangesAsync();
            candleIds.AddRange(allCandles.Select(c => c.Id));

            var dataLoader = sp.GetRequiredService<IBacktestDataLoader>();
            var dataset = await dataLoader.LoadSymbolTimeframeAsync(
                testExchange.Id,
                symbol.Id,
                Timeframe.M15,
                EvalStart,
                evalEnd,
                5,
                StrategyLabCandleLoadContractVersions.ExactExclusiveV2);

            Assert.NotNull(dataset);
            var lastCandle = dataset.Candles[^1];
            Assert.True(lastCandle.OpenTimeUtc < evalEnd);

            var evalCandles = dataset.Candles.Where(c => c.OpenTimeUtc >= EvalStart && c.OpenTimeUtc < evalEnd).ToList();
            Assert.DoesNotContain(evalCandles, c => c.OpenTimeUtc == evalEnd);
        }
        finally
        {
            await CleanupAsync(factory, null, candleIds);
        }
    }

    [Fact]
    public void ThreeEvents_PersistedWithCorrectPartitionLabels()
    {
        var warmupStart = EvalStart.AddHours(-AvailableWarmup);
        var evalEnd = EvalStart.AddHours(EvalCount);
        var allCandles = new List<Candle>();
        for (var i = 0; i < AvailableWarmup + EvalCount; i++)
        {
            var open = warmupStart.AddHours(i);
            allCandles.Add(new Candle
            {
                Id = i + 1,
                ExchangeId = 1,
                SymbolId = TestSymbolId,
                Timeframe = Timeframe.H1,
                OpenTimeUtc = open,
                CloseTimeUtc = open.AddHours(1),
                Open = 3000m + i,
                High = 3001m + i,
                Low = 2999m + i,
                Close = 3000.5m + i,
                Volume = 300m + i,
                IsClosed = true,
                CreatedAtUtc = DateTime.UtcNow
            });
        }

        var warmup = allCandles.Where(c => c.OpenTimeUtc < EvalStart).TakeLast(RequiredWarmup).ToList();
        var evaluation = allCandles.Where(c => c.OpenTimeUtc >= EvalStart && c.OpenTimeUtc < evalEnd).ToList();

        var partition = ValidationTrainingCandleScope.BuildPartition(
            validationExperimentId: 777,
            symbolId: TestSymbolId,
            symbolName: "PARITYBTC",
            timeframe: "1h",
            requiredWarmup: RequiredWarmup,
            availableWarmup: warmup.Count,
            evaluationCount: evaluation.Count,
            status: ValidationWarmupStatus.Complete,
            evalStart: EvalStart,
            evalEndExclusive: evalEnd,
            boundary: evalEnd,
            requirementsVersion: StrategyExecutionRequirements.Version,
            warmup: warmup,
            evaluation: evaluation,
            combined: warmup.Concat(evaluation).ToList());

        var scope = new ValidationTrainingCandleScope(partition, warmup, evaluation);

        var request = new ValidationDatasetMaterializationRequest
        {
            SymbolId = TestSymbolId,
            SymbolName = "PARITYBTC",
            Timeframe = "1h",
            EvaluationFromUtc = EvalStart,
            EvaluationToExclusiveUtc = evalEnd,
            WarmupCandleCount = RequiredWarmup,
            CallerComponent = "ParityTest"
        };

        var dataset = scope.CreateStrategyLabDataset(request);
        Assert.NotNull(dataset);

        var accessLog = scope.AccessLog;
        var warmupEvent = accessLog.FirstOrDefault(a => a.AccessPurpose == ValidationCandleAccessPurpose.WarmupLoad);
        var evalEvent = accessLog.FirstOrDefault(a => a.AccessPurpose == ValidationCandleAccessPurpose.EvaluationLoad);
        var datasetEvent = accessLog.FirstOrDefault(a => a.AccessPurpose == ValidationCandleAccessPurpose.DatasetMaterialization);

        Assert.NotNull(warmupEvent);
        Assert.NotNull(evalEvent);
        Assert.NotNull(datasetEvent);

        Assert.Equal("Warmup", warmupEvent.DatasetPartition);
        Assert.Equal("Evaluation", evalEvent.DatasetPartition);
        Assert.Equal("Combined", datasetEvent.DatasetPartition);
    }

    private static async Task CleanupAsync(
        MomoQuantWebApplicationFactory factory,
        long? experimentId,
        List<long> candleIds)
    {
        if (candleIds.Count == 0 && experimentId is null)
        {
            return;
        }

        try
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();

            if (experimentId.HasValue)
            {
                var exp = await db.ValidationExperiments.FindAsync(experimentId.Value);
                if (exp is not null)
                {
                    db.ValidationExperiments.Remove(exp);
                }
            }

            if (candleIds.Count > 0)
            {
                var candles = await db.Candles.Where(c => candleIds.Contains(c.Id)).ToListAsync();
                db.Candles.RemoveRange(candles);

                var symbolIds = candles.Select(c => c.SymbolId).Distinct().ToList();
                var symbols = await db.Symbols.Where(s => symbolIds.Contains(s.Id)).ToListAsync();
                db.Symbols.RemoveRange(symbols);
            }

            await db.SaveChangesAsync();
        }
        catch
        {
            // Best effort cleanup
        }
    }
}
