using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Backtesting;
using MomoQuant.Application.Common;
using MomoQuant.Application.MarketData;
using MomoQuant.Application.Strategies;
using MomoQuant.Application.StrategyLab;
using MomoQuant.Application.Validation.Dtos;
using MomoQuant.Application.ValidationLab;
using MomoQuant.Domain.Constants;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Exchanges;
using MomoQuant.Domain.MarketData;
using MomoQuant.Domain.Strategies;
using MomoQuant.Domain.StrategyLab;
using MomoQuant.Domain.ValidationLab;
using MomoQuant.Persistence;

namespace MomoQuant.IntegrationTests;

/// <summary>
/// Milestone 23.0E1B — Integration tests for validation runner parity with normal StrategyLabRunner.
/// Ensures identical deterministic candidate production across GeneralResearch and ValidationTraining paths.
/// </summary>
[Collection("Integration")]
public sealed class Milestone230E1BRunnerParityTests
{
    private static readonly DateTime EvalStart = new(2042, 6, 1, 0, 0, 0, DateTimeKind.Utc);
    private const int RequiredWarmup = 20;
    private const int AvailableWarmup = 40;
    private const int EvalCount = 100;

    [Fact]
    public async Task ScopeFactoryAndStandardSource_V2_ProduceIdenticalDatasets()
    {
        await using var factory = new E1BTestFactory();
        long? experimentId = null;
        var candleIds = new List<long>();
        string symbolName = $"E1B_SCOPE_{Guid.NewGuid():N}"[..16];

        try
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var sp = scope.ServiceProvider;
            var db = sp.GetRequiredService<MomoQuantDbContext>();

            var testExchange = await db.Exchanges.AsNoTracking().OrderBy(e => e.Id).FirstAsync();
            var evalEnd = EvalStart.AddMinutes(EvalCount * 15);
            var boundary = evalEnd.AddMinutes(30);

            var symbol = new Symbol
            {
                ExchangeId = testExchange.Id,
                SymbolName = symbolName,
                BaseAsset = "E1B",
                QuoteAsset = "USDT",
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
                    Open = 50000m + i * 10,
                    High = 50100m + i * 10,
                    Low = 49900m + i * 10,
                    Close = 50050m + i * 10,
                    Volume = 1000m + i,
                    IsClosed = true,
                    CreatedAtUtc = DateTime.UtcNow
                });
            }

            var exactEndCandle = new Candle
            {
                ExchangeId = testExchange.Id,
                SymbolId = symbol.Id,
                Timeframe = Timeframe.M15,
                OpenTimeUtc = evalEnd,
                CloseTimeUtc = evalEnd.AddMinutes(15),
                Open = 99000m,
                High = 99100m,
                Low = 98900m,
                Close = 99050m,
                Volume = 9999m,
                IsClosed = true,
                CreatedAtUtc = DateTime.UtcNow
            };
            allCandles.Add(exactEndCandle);

            var boundaryCandle = new Candle
            {
                ExchangeId = testExchange.Id,
                SymbolId = symbol.Id,
                Timeframe = Timeframe.M15,
                OpenTimeUtc = boundary,
                CloseTimeUtc = boundary.AddMinutes(15),
                Open = 99500m,
                High = 99600m,
                Low = 99400m,
                Close = 99550m,
                Volume = 9998m,
                IsClosed = true,
                CreatedAtUtc = DateTime.UtcNow
            };
            allCandles.Add(boundaryCandle);

            db.Candles.AddRange(allCandles);
            await db.SaveChangesAsync();
            candleIds.AddRange(allCandles.Select(c => c.Id));

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
            Assert.DoesNotContain(normalDataset.Candles, c => c.OpenTimeUtc == evalEnd);
            Assert.DoesNotContain(normalDataset.Candles, c => c.OpenTimeUtc == boundary);

            experimentId = 9001;
            var scopeFactory = sp.GetRequiredService<IValidationTrainingCandleScopeFactory>();
            var scopeRequest = new ValidationTrainingCandleScopeRequest
            {
                ValidationExperimentId = experimentId.Value,
                SymbolId = symbol.Id,
                SymbolName = symbolName,
                Timeframe = "15m",
                TrainingEvaluationStartUtc = EvalStart,
                TrainingEvaluationEndExclusiveUtc = evalEnd,
                ValidationBoundaryUtc = boundary,
                RequiredWarmupCandleCount = RequiredWarmup,
                RequirementsVersion = StrategyExecutionRequirements.Version,
                StrategyCode = StrategyCodes.DonchianBreakout
            };

            var trainingScope = await scopeFactory.CreateAsync(scopeRequest);
            var validationSource = new ValidationTrainingStrategyLabCandleDataSource(trainingScope, "E1B.Parity");

            var run = new StrategyLabRun
            {
                Id = 1,
                Name = "e1b-parity-test",
                StrategyCode = StrategyCodes.DonchianBreakout,
                StrategyVersion = "1.0.0",
                ExchangeId = testExchange.Id,
                SymbolId = symbol.Id,
                Symbol = symbolName,
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

            var accessLog = trainingScope.AccessLog;
            Assert.Contains(accessLog, a => a.AccessPurpose == ValidationCandleAccessPurpose.WarmupLoad && !a.WasDenied);
            Assert.Contains(accessLog, a => a.AccessPurpose == ValidationCandleAccessPurpose.EvaluationLoad && !a.WasDenied);
            Assert.Contains(accessLog, a => a.AccessPurpose == ValidationCandleAccessPurpose.DatasetMaterialization && !a.WasDenied);
            Assert.Equal(3, accessLog.Count(a => !a.WasDenied));
        }
        finally
        {
            await CleanupAsync(factory, experimentId, candleIds, symbolName);
        }
    }

    [Fact]
    public async Task StrategyLabRunner_NormalAndValidationV2_ProduceIdenticalCanonicalCandidates()
    {
        await using var factory = new E1BTestFactory();
        long? experimentId = null;
        var candleIds = new List<long>();
        string symbolName = $"E1B_RUN_{Guid.NewGuid():N}"[..16];

        try
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var sp = scope.ServiceProvider;
            var db = sp.GetRequiredService<MomoQuantDbContext>();

            var testExchange = await db.Exchanges.AsNoTracking().OrderBy(e => e.Id).FirstAsync();
            var evalEnd = EvalStart.AddMinutes(EvalCount * 15);
            var boundary = evalEnd.AddMinutes(30);

            var donchianStrategy = await db.Strategies.FirstOrDefaultAsync(s => s.Code == StrategyCode.DonchianBreakout);
            if (donchianStrategy == null)
            {
                donchianStrategy = new Domain.Strategies.Strategy
                {
                    Code = StrategyCode.DonchianBreakout,
                    Name = "Donchian Breakout",
                    Description = "Test strategy",
                    IsEnabled = true,
                    Version = "1.0.0",
                    CreatedAtUtc = DateTime.UtcNow
                };
                db.Strategies.Add(donchianStrategy);
                await db.SaveChangesAsync();
            }

            var symbol = new Symbol
            {
                ExchangeId = testExchange.Id,
                SymbolName = symbolName,
                BaseAsset = "E1B",
                QuoteAsset = "USDT",
                ContractType = ContractType.Perpetual,
                IsActive = true
            };
            db.Symbols.Add(symbol);
            await db.SaveChangesAsync();

            var warmupStart = EvalStart.AddMinutes(-(AvailableWarmup + 10) * 15);
            var allCandles = new List<Candle>();
            for (var i = 0; i < AvailableWarmup + 10 + EvalCount; i++)
            {
                var open = warmupStart.AddMinutes(i * 15);
                allCandles.Add(new Candle
                {
                    ExchangeId = testExchange.Id,
                    SymbolId = symbol.Id,
                    Timeframe = Timeframe.M15,
                    OpenTimeUtc = open,
                    CloseTimeUtc = open.AddMinutes(15),
                    Open = 50000m + i * 10,
                    High = 50100m + i * 10,
                    Low = 49900m + i * 10,
                    Close = 50050m + i * 10,
                    Volume = 1000m + i,
                    IsClosed = true,
                    CreatedAtUtc = DateTime.UtcNow
                });
            }

            var exactEndCandle = new Candle
            {
                ExchangeId = testExchange.Id,
                SymbolId = symbol.Id,
                Timeframe = Timeframe.M15,
                OpenTimeUtc = evalEnd,
                CloseTimeUtc = evalEnd.AddMinutes(15),
                Open = 99000m,
                High = 99100m,
                Low = 98900m,
                Close = 99050m,
                Volume = 9999m,
                IsClosed = true,
                CreatedAtUtc = DateTime.UtcNow
            };
            allCandles.Add(exactEndCandle);

            var boundaryCandle = new Candle
            {
                ExchangeId = testExchange.Id,
                SymbolId = symbol.Id,
                Timeframe = Timeframe.M15,
                OpenTimeUtc = boundary,
                CloseTimeUtc = boundary.AddMinutes(15),
                Open = 99500m,
                High = 99600m,
                Low = 99400m,
                Close = 99550m,
                Volume = 9998m,
                IsClosed = true,
                CreatedAtUtc = DateTime.UtcNow
            };
            allCandles.Add(boundaryCandle);

            db.Candles.AddRange(allCandles);
            await db.SaveChangesAsync();
            candleIds.AddRange(allCandles.Select(c => c.Id));

            var runRepo = sp.GetRequiredService<IStrategyLabRunRepository>();

            var runA = new StrategyLabRun
            {
                Name = "e1b-normal",
                StrategyCode = StrategyCodes.DonchianBreakout,
                StrategyVersion = "1.0.0",
                ExchangeId = testExchange.Id,
                SymbolId = symbol.Id,
                Symbol = symbolName,
                Timeframe = "15m",
                FromUtc = EvalStart,
                ToUtc = evalEnd,
                ExecutionMode = StrategyLabExecutionMode.RawStrategy,
                ParametersJson = "{}",
                InitialBalance = 10_000m,
                FeeSettingsJson = "{\"takerFeeRate\":0.0004,\"makerFeeRate\":0.0002}",
                SlippageSettingsJson = "{\"slippagePercent\":0.0}",
                Status = StrategyLabRunStatus.Created,
                CandleLoadContractVersion = StrategyLabCandleLoadContractVersions.ExactExclusiveV2,
                CreatedAtUtc = DateTime.UtcNow
            };

            var runB = new StrategyLabRun
            {
                Name = "e1b-validation",
                StrategyCode = StrategyCodes.DonchianBreakout,
                StrategyVersion = "1.0.0",
                ExchangeId = testExchange.Id,
                SymbolId = symbol.Id,
                Symbol = symbolName,
                Timeframe = "15m",
                FromUtc = EvalStart,
                ToUtc = evalEnd,
                ExecutionMode = StrategyLabExecutionMode.RawStrategy,
                ParametersJson = "{}",
                InitialBalance = 10_000m,
                FeeSettingsJson = "{\"takerFeeRate\":0.0004,\"makerFeeRate\":0.0002}",
                SlippageSettingsJson = "{\"slippagePercent\":0.0}",
                Status = StrategyLabRunStatus.Created,
                CandleLoadContractVersion = StrategyLabCandleLoadContractVersions.ExactExclusiveV2,
                CreatedAtUtc = DateTime.UtcNow
            };

            await runRepo.AddAsync(runA);
            await runRepo.AddAsync(runB);

            experimentId = 9002;
            var runner = sp.GetRequiredService<IStrategyLabRunner>();

            var normalContext = StrategyLabExecutionContext.ForGeneralResearch("E1B.Normal");
            await runner.ExecuteAsync(runA.Id, normalContext);

            var scopeFactory = sp.GetRequiredService<IValidationTrainingCandleScopeFactory>();
            var scopeRequest = new ValidationTrainingCandleScopeRequest
            {
                ValidationExperimentId = experimentId.Value,
                SymbolId = symbol.Id,
                SymbolName = symbolName,
                Timeframe = "15m",
                TrainingEvaluationStartUtc = EvalStart,
                TrainingEvaluationEndExclusiveUtc = evalEnd,
                ValidationBoundaryUtc = boundary,
                RequiredWarmupCandleCount = RequiredWarmup,
                RequirementsVersion = StrategyExecutionRequirements.Version,
                StrategyCode = StrategyCodes.DonchianBreakout
            };

            var trainingScope = await scopeFactory.CreateAsync(scopeRequest);
            var validationSource = new ValidationTrainingStrategyLabCandleDataSource(trainingScope, "E1B.Validation");
            var validationContext = StrategyLabExecutionContext.ForValidationTraining(
                validationExperimentId: experimentId.Value,
                validationTrialId: null,
                validationTrialNumber: 1,
                trainingBoundaryUtc: boundary,
                candleDataSource: validationSource,
                callerComponent: "E1B.Validation");

            await runner.ExecuteAsync(runB.Id, validationContext);

            var candidateRepo = sp.GetRequiredService<IStrategyResearchCandidateRepository>();
            var candidatesA = await candidateRepo.GetByRunIdAsync(runA.Id);
            var candidatesB = await candidateRepo.GetByRunIdAsync(runB.Id);

            var runAUpdated = await runRepo.GetByIdAsync(runA.Id);
            var runBUpdated = await runRepo.GetByIdAsync(runB.Id);

            Assert.Equal(StrategyLabRunStatus.Completed, runAUpdated?.Status);
            Assert.Equal(StrategyLabRunStatus.Completed, runBUpdated?.Status);

            Assert.True(candidatesA.Count >= 5, $"Normal run produced {candidatesA.Count} candidates, expected >= 5");
            Assert.True(candidatesB.Count >= 5, $"Validation run produced {candidatesB.Count} candidates, expected >= 5");
            Assert.Equal(candidatesA.Count, candidatesB.Count);

            var hashA = CanonicalCandidateHasher.ComputeHash(candidatesA);
            var hashB = CanonicalCandidateHasher.ComputeHash(candidatesB);

            Assert.Equal(hashA, hashB);

            var metadata = trainingScope.Partition;
            Assert.Equal(RequiredWarmup, metadata.RequiredWarmupCandleCount);
            Assert.Equal(EvalCount, metadata.EvaluationCandleCount);
            Assert.NotNull(metadata.CombinedContentFingerprint);

            var accessLog = trainingScope.AccessLog;
            Assert.Equal(3, accessLog.Count(a => !a.WasDenied));
        }
        finally
        {
            await CleanupAsync(factory, experimentId, candleIds, symbolName);
        }
    }

    [Fact]
    public async Task StrategyLabRunner_TimestampGapFixture_ProducesIdenticalCandidates()
    {
        await using var factory = new E1BTestFactory();
        long? experimentId = null;
        var candleIds = new List<long>();
        string symbolName = $"E1B_GAP_{Guid.NewGuid():N}"[..16];

        try
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var sp = scope.ServiceProvider;
            var db = sp.GetRequiredService<MomoQuantDbContext>();

            var testExchange = await db.Exchanges.AsNoTracking().OrderBy(e => e.Id).FirstAsync();
            var evalEnd = EvalStart.AddMinutes(60 * 15);
            var boundary = evalEnd.AddMinutes(30);

            var donchianStrategy = await db.Strategies.FirstOrDefaultAsync(s => s.Code == StrategyCode.DonchianBreakout);
            if (donchianStrategy == null)
            {
                donchianStrategy = new Domain.Strategies.Strategy
                {
                    Code = StrategyCode.DonchianBreakout,
                    Name = "Donchian Breakout",
                    Description = "Test strategy",
                    IsEnabled = true,
                    Version = "1.0.0",
                    CreatedAtUtc = DateTime.UtcNow
                };
                db.Strategies.Add(donchianStrategy);
                await db.SaveChangesAsync();
            }

            var symbol = new Symbol
            {
                ExchangeId = testExchange.Id,
                SymbolName = symbolName,
                BaseAsset = "E1B",
                QuoteAsset = "USDT",
                ContractType = ContractType.Perpetual,
                IsActive = true
            };
            db.Symbols.Add(symbol);
            await db.SaveChangesAsync();

            var warmupStart = EvalStart.AddMinutes(-30 * 15);
            var allCandles = new List<Candle>();

            for (var i = 0; i < 10; i++)
            {
                var open = warmupStart.AddMinutes(i * 15);
                allCandles.Add(new Candle
                {
                    ExchangeId = testExchange.Id,
                    SymbolId = symbol.Id,
                    Timeframe = Timeframe.M15,
                    OpenTimeUtc = open,
                    CloseTimeUtc = open.AddMinutes(15),
                    Open = 50000m + i * 10,
                    High = 50100m + i * 10,
                    Low = 49900m + i * 10,
                    Close = 50050m + i * 10,
                    Volume = 1000m + i,
                    IsClosed = true,
                    CreatedAtUtc = DateTime.UtcNow
                });
            }

            for (var i = 15; i < 25; i++)
            {
                var open = warmupStart.AddMinutes(i * 15);
                allCandles.Add(new Candle
                {
                    ExchangeId = testExchange.Id,
                    SymbolId = symbol.Id,
                    Timeframe = Timeframe.M15,
                    OpenTimeUtc = open,
                    CloseTimeUtc = open.AddMinutes(15),
                    Open = 50000m + i * 10,
                    High = 50100m + i * 10,
                    Low = 49900m + i * 10,
                    Close = 50050m + i * 10,
                    Volume = 1000m + i,
                    IsClosed = true,
                    CreatedAtUtc = DateTime.UtcNow
                });
            }

            for (var i = 0; i < 60; i++)
            {
                var open = EvalStart.AddMinutes(i * 15);
                if (i >= 10 && i < 15)
                    continue;
                if (i >= 35 && i < 40)
                    continue;

                allCandles.Add(new Candle
                {
                    ExchangeId = testExchange.Id,
                    SymbolId = symbol.Id,
                    Timeframe = Timeframe.M15,
                    OpenTimeUtc = open,
                    CloseTimeUtc = open.AddMinutes(15),
                    Open = 50500m + i * 10,
                    High = 50600m + i * 10,
                    Low = 50400m + i * 10,
                    Close = 50550m + i * 10,
                    Volume = 1500m + i,
                    IsClosed = true,
                    CreatedAtUtc = DateTime.UtcNow
                });
            }

            var exactEndCandle = new Candle
            {
                ExchangeId = testExchange.Id,
                SymbolId = symbol.Id,
                Timeframe = Timeframe.M15,
                OpenTimeUtc = evalEnd,
                CloseTimeUtc = evalEnd.AddMinutes(15),
                Open = 99000m,
                High = 99100m,
                Low = 98900m,
                Close = 99050m,
                Volume = 9999m,
                IsClosed = true,
                CreatedAtUtc = DateTime.UtcNow
            };
            allCandles.Add(exactEndCandle);

            var boundaryCandle = new Candle
            {
                ExchangeId = testExchange.Id,
                SymbolId = symbol.Id,
                Timeframe = Timeframe.M15,
                OpenTimeUtc = boundary,
                CloseTimeUtc = boundary.AddMinutes(15),
                Open = 99500m,
                High = 99600m,
                Low = 99400m,
                Close = 99550m,
                Volume = 9998m,
                IsClosed = true,
                CreatedAtUtc = DateTime.UtcNow
            };
            allCandles.Add(boundaryCandle);

            db.Candles.AddRange(allCandles);
            await db.SaveChangesAsync();
            candleIds.AddRange(allCandles.Select(c => c.Id));

            var runRepo = sp.GetRequiredService<IStrategyLabRunRepository>();

            var runA = new StrategyLabRun
            {
                Name = "e1b-gap-normal",
                StrategyCode = StrategyCodes.DonchianBreakout,
                StrategyVersion = "1.0.0",
                ExchangeId = testExchange.Id,
                SymbolId = symbol.Id,
                Symbol = symbolName,
                Timeframe = "15m",
                FromUtc = EvalStart,
                ToUtc = evalEnd,
                ExecutionMode = StrategyLabExecutionMode.RawStrategy,
                ParametersJson = "{}",
                InitialBalance = 10_000m,
                FeeSettingsJson = "{\"takerFeeRate\":0.0004,\"makerFeeRate\":0.0002}",
                SlippageSettingsJson = "{\"slippagePercent\":0.0}",
                Status = StrategyLabRunStatus.Created,
                CandleLoadContractVersion = StrategyLabCandleLoadContractVersions.ExactExclusiveV2,
                CreatedAtUtc = DateTime.UtcNow
            };

            var runB = new StrategyLabRun
            {
                Name = "e1b-gap-validation",
                StrategyCode = StrategyCodes.DonchianBreakout,
                StrategyVersion = "1.0.0",
                ExchangeId = testExchange.Id,
                SymbolId = symbol.Id,
                Symbol = symbolName,
                Timeframe = "15m",
                FromUtc = EvalStart,
                ToUtc = evalEnd,
                ExecutionMode = StrategyLabExecutionMode.RawStrategy,
                ParametersJson = "{}",
                InitialBalance = 10_000m,
                FeeSettingsJson = "{\"takerFeeRate\":0.0004,\"makerFeeRate\":0.0002}",
                SlippageSettingsJson = "{\"slippagePercent\":0.0}",
                Status = StrategyLabRunStatus.Created,
                CandleLoadContractVersion = StrategyLabCandleLoadContractVersions.ExactExclusiveV2,
                CreatedAtUtc = DateTime.UtcNow
            };

            await runRepo.AddAsync(runA);
            await runRepo.AddAsync(runB);

            experimentId = 9003;
            var runner = sp.GetRequiredService<IStrategyLabRunner>();

            var normalContext = StrategyLabExecutionContext.ForGeneralResearch("E1B.Gap.Normal");
            await runner.ExecuteAsync(runA.Id, normalContext);

            var scopeFactory = sp.GetRequiredService<IValidationTrainingCandleScopeFactory>();
            var scopeRequest = new ValidationTrainingCandleScopeRequest
            {
                ValidationExperimentId = experimentId.Value,
                SymbolId = symbol.Id,
                SymbolName = symbolName,
                Timeframe = "15m",
                TrainingEvaluationStartUtc = EvalStart,
                TrainingEvaluationEndExclusiveUtc = evalEnd,
                ValidationBoundaryUtc = boundary,
                RequiredWarmupCandleCount = RequiredWarmup,
                RequirementsVersion = StrategyExecutionRequirements.Version,
                StrategyCode = StrategyCodes.DonchianBreakout
            };

            var trainingScope = await scopeFactory.CreateAsync(scopeRequest);
            var validationSource = new ValidationTrainingStrategyLabCandleDataSource(trainingScope, "E1B.Gap.Validation");
            var validationContext = StrategyLabExecutionContext.ForValidationTraining(
                validationExperimentId: experimentId.Value,
                validationTrialId: null,
                validationTrialNumber: 1,
                trainingBoundaryUtc: boundary,
                candleDataSource: validationSource,
                callerComponent: "E1B.Gap.Validation");

            await runner.ExecuteAsync(runB.Id, validationContext);

            var candidateRepo = sp.GetRequiredService<IStrategyResearchCandidateRepository>();
            var candidatesA = await candidateRepo.GetByRunIdAsync(runA.Id);
            var candidatesB = await candidateRepo.GetByRunIdAsync(runB.Id);

            Assert.True(candidatesA.Count >= 1, $"Normal run with gaps produced {candidatesA.Count} candidates");
            Assert.True(candidatesB.Count >= 1, $"Validation run with gaps produced {candidatesB.Count} candidates");
            Assert.Equal(candidatesA.Count, candidatesB.Count);

            var hashA = CanonicalCandidateHasher.ComputeHash(candidatesA);
            var hashB = CanonicalCandidateHasher.ComputeHash(candidatesB);

            Assert.Equal(hashA, hashB);

            var metadata = trainingScope.Partition;
            Assert.Equal(RequiredWarmup, metadata.RequiredWarmupCandleCount);
        }
        finally
        {
            await CleanupAsync(factory, experimentId, candleIds, symbolName);
        }
    }

    [Fact]
    public async Task ValidationMaterialization_PersistsExactlyThreeLogicalAuditRows()
    {
        await using var factory = new E1BTestFactory();
        long? experimentId = null;
        var candleIds = new List<long>();
        string symbolName = $"E1B_AUDIT_{Guid.NewGuid():N}"[..16];

        try
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var sp = scope.ServiceProvider;
            var db = sp.GetRequiredService<MomoQuantDbContext>();

            var testExchange = await db.Exchanges.AsNoTracking().OrderBy(e => e.Id).FirstAsync();
            var evalEnd = EvalStart.AddMinutes(50 * 15);
            var boundary = evalEnd.AddMinutes(30);

            var symbol = new Symbol
            {
                ExchangeId = testExchange.Id,
                SymbolName = symbolName,
                BaseAsset = "E1B",
                QuoteAsset = "USDT",
                ContractType = ContractType.Perpetual,
                IsActive = true
            };
            db.Symbols.Add(symbol);
            await db.SaveChangesAsync();

            var warmupStart = EvalStart.AddMinutes(-AvailableWarmup * 15);
            var allCandles = new List<Candle>();
            for (var i = 0; i < AvailableWarmup + 50; i++)
            {
                var open = warmupStart.AddMinutes(i * 15);
                allCandles.Add(new Candle
                {
                    ExchangeId = testExchange.Id,
                    SymbolId = symbol.Id,
                    Timeframe = Timeframe.M15,
                    OpenTimeUtc = open,
                    CloseTimeUtc = open.AddMinutes(15),
                    Open = 50000m + i * 10,
                    High = 50100m + i * 10,
                    Low = 49900m + i * 10,
                    Close = 50050m + i * 10,
                    Volume = 1000m + i,
                    IsClosed = true,
                    CreatedAtUtc = DateTime.UtcNow
                });
            }

            db.Candles.AddRange(allCandles);
            await db.SaveChangesAsync();
            candleIds.AddRange(allCandles.Select(c => c.Id));

            experimentId = 9004;
            var scopeFactory = sp.GetRequiredService<IValidationTrainingCandleScopeFactory>();
            var scopeRequest = new ValidationTrainingCandleScopeRequest
            {
                ValidationExperimentId = experimentId.Value,
                SymbolId = symbol.Id,
                SymbolName = symbolName,
                Timeframe = "15m",
                TrainingEvaluationStartUtc = EvalStart,
                TrainingEvaluationEndExclusiveUtc = evalEnd,
                ValidationBoundaryUtc = boundary,
                RequiredWarmupCandleCount = RequiredWarmup,
                RequirementsVersion = StrategyExecutionRequirements.Version,
                StrategyCode = StrategyCodes.DonchianBreakout
            };

            var trainingScope = await scopeFactory.CreateAsync(scopeRequest);
            var validationSource = new ValidationTrainingStrategyLabCandleDataSource(trainingScope, "E1B.Audit");

            var run = new StrategyLabRun
            {
                Id = 1,
                Name = "e1b-audit-test",
                StrategyCode = StrategyCodes.DonchianBreakout,
                StrategyVersion = "1.0.0",
                ExchangeId = testExchange.Id,
                SymbolId = symbol.Id,
                Symbol = symbolName,
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

            await validationSource.LoadAsync(run, RequiredWarmup);

            var recorder = sp.GetRequiredService<IValidationCandleAccessRecorder>();
            await recorder.FlushAsync(trainingScope);

            var scopeExecId = trainingScope.Partition.ValidationExperimentId.ToString();
            var audits = await db.ValidationCandleAccessAudits
                .Where(a => a.ValidationExperimentId == experimentId.Value)
                .OrderBy(a => a.ScopeSequenceNumber)
                .ToListAsync();

            Assert.Equal(3, audits.Count);

            var warmupAudit = audits.FirstOrDefault(a => a.AccessPurpose == "WarmupLoad");
            var evalAudit = audits.FirstOrDefault(a => a.AccessPurpose == "EvaluationLoad");
            var datasetAudit = audits.FirstOrDefault(a => a.AccessPurpose == "DatasetMaterialization");

            Assert.NotNull(warmupAudit);
            Assert.NotNull(evalAudit);
            Assert.NotNull(datasetAudit);

            Assert.Equal("Warmup", warmupAudit.DatasetPartition);
            Assert.Equal("Evaluation", evalAudit.DatasetPartition);
            Assert.Equal("Combined", datasetAudit.DatasetPartition);

            Assert.Equal(1, warmupAudit.ScopeSequenceNumber);
            Assert.Equal(2, evalAudit.ScopeSequenceNumber);
            Assert.Equal(3, datasetAudit.ScopeSequenceNumber);

            Assert.Equal(RequiredWarmup, warmupAudit.RequestedCandleCount);
            Assert.Equal(50, evalAudit.RequestedCandleCount);
            Assert.Equal(RequiredWarmup + 50, datasetAudit.RequestedCandleCount);

            Assert.False(warmupAudit.WasDenied);
            Assert.False(evalAudit.WasDenied);
            Assert.False(datasetAudit.WasDenied);
        }
        finally
        {
            await CleanupAsync(factory, experimentId, candleIds, symbolName);
        }
    }

    [Fact]
    public async Task TenThousandCandleValidationRunner_PersistsO1LogicalAuditEvents()
    {
        await using var factory = new E1BTestFactory();
        long? experimentId = null;
        var candleIds = new List<long>();
        string symbolName = $"E1B_10K_{Guid.NewGuid():N}"[..16];

        try
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var sp = scope.ServiceProvider;
            var db = sp.GetRequiredService<MomoQuantDbContext>();

            var testExchange = await db.Exchanges.AsNoTracking().OrderBy(e => e.Id).FirstAsync();
            var evalEnd = EvalStart.AddMinutes(10000 * 15);
            var boundary = evalEnd.AddMinutes(30);

            var donchianStrategy = await db.Strategies.FirstOrDefaultAsync(s => s.Code == StrategyCode.DonchianBreakout);
            if (donchianStrategy == null)
            {
                donchianStrategy = new Domain.Strategies.Strategy
                {
                    Code = StrategyCode.DonchianBreakout,
                    Name = "Donchian Breakout",
                    Description = "Test strategy",
                    IsEnabled = true,
                    Version = "1.0.0",
                    CreatedAtUtc = DateTime.UtcNow
                };
                db.Strategies.Add(donchianStrategy);
                await db.SaveChangesAsync();
            }

            var symbol = new Symbol
            {
                ExchangeId = testExchange.Id,
                SymbolName = symbolName,
                BaseAsset = "E1B",
                QuoteAsset = "USDT",
                ContractType = ContractType.Perpetual,
                IsActive = true
            };
            db.Symbols.Add(symbol);
            await db.SaveChangesAsync();

            var warmupStart = EvalStart.AddMinutes(-RequiredWarmup * 15);
            var totalCandles = RequiredWarmup + 10000;
            var allCandles = new List<Candle>();

            for (var i = 0; i < totalCandles; i += 500)
            {
                var batch = new List<Candle>();
                var batchSize = Math.Min(500, totalCandles - i);
                for (var j = 0; j < batchSize; j++)
                {
                    var index = i + j;
                    var open = warmupStart.AddMinutes(index * 15);
                    batch.Add(new Candle
                    {
                        ExchangeId = testExchange.Id,
                        SymbolId = symbol.Id,
                        Timeframe = Timeframe.M15,
                        OpenTimeUtc = open,
                        CloseTimeUtc = open.AddMinutes(15),
                        Open = 50000m + index * 10,
                        High = 50100m + index * 10,
                        Low = 49900m + index * 10,
                        Close = 50050m + index * 10,
                        Volume = 1000m + index,
                        IsClosed = true,
                        CreatedAtUtc = DateTime.UtcNow
                    });
                }
                db.Candles.AddRange(batch);
                await db.SaveChangesAsync();
                allCandles.AddRange(batch);
            }

            candleIds.AddRange(allCandles.Select(c => c.Id));

            var runRepo = sp.GetRequiredService<IStrategyLabRunRepository>();

            var run = new StrategyLabRun
            {
                Name = "e1b-10k-validation",
                StrategyCode = StrategyCodes.DonchianBreakout,
                StrategyVersion = "1.0.0",
                ExchangeId = testExchange.Id,
                SymbolId = symbol.Id,
                Symbol = symbolName,
                Timeframe = "15m",
                FromUtc = EvalStart,
                ToUtc = evalEnd,
                ExecutionMode = StrategyLabExecutionMode.RawStrategy,
                ParametersJson = "{}",
                InitialBalance = 10_000m,
                FeeSettingsJson = "{\"takerFeeRate\":0.0004,\"makerFeeRate\":0.0002}",
                SlippageSettingsJson = "{\"slippagePercent\":0.0}",
                Status = StrategyLabRunStatus.Created,
                CandleLoadContractVersion = StrategyLabCandleLoadContractVersions.ExactExclusiveV2,
                CreatedAtUtc = DateTime.UtcNow
            };

            await runRepo.AddAsync(run);

            experimentId = 9005;
            var scopeFactory = sp.GetRequiredService<IValidationTrainingCandleScopeFactory>();
            var scopeRequest = new ValidationTrainingCandleScopeRequest
            {
                ValidationExperimentId = experimentId.Value,
                SymbolId = symbol.Id,
                SymbolName = symbolName,
                Timeframe = "15m",
                TrainingEvaluationStartUtc = EvalStart,
                TrainingEvaluationEndExclusiveUtc = evalEnd,
                ValidationBoundaryUtc = boundary,
                RequiredWarmupCandleCount = RequiredWarmup,
                RequirementsVersion = StrategyExecutionRequirements.Version,
                StrategyCode = StrategyCodes.DonchianBreakout
            };

            var sw = Stopwatch.StartNew();

            var trainingScope = await scopeFactory.CreateAsync(scopeRequest);
            var validationSource = new ValidationTrainingStrategyLabCandleDataSource(trainingScope, "E1B.10K");
            var validationContext = StrategyLabExecutionContext.ForValidationTraining(
                validationExperimentId: experimentId.Value,
                validationTrialId: null,
                validationTrialNumber: 1,
                trainingBoundaryUtc: boundary,
                candleDataSource: validationSource,
                callerComponent: "E1B.10K");

            var runner = sp.GetRequiredService<IStrategyLabRunner>();
            await runner.ExecuteAsync(run.Id, validationContext);

            var recorder = sp.GetRequiredService<IValidationCandleAccessRecorder>();
            await recorder.FlushAsync(trainingScope);

            sw.Stop();

            var audits = await db.ValidationCandleAccessAudits
                .Where(a => a.ValidationExperimentId == experimentId.Value)
                .OrderBy(a => a.ScopeSequenceNumber)
                .ToListAsync();

            Assert.Equal(3, audits.Count);

            var candidateRepo = sp.GetRequiredService<IStrategyResearchCandidateRepository>();
            var candidates = await candidateRepo.GetByRunIdAsync(run.Id);
            Assert.True(candidates.Count >= 1, $"10k validation run produced {candidates.Count} candidates");
        }
        finally
        {
            await CleanupAsync(factory, experimentId, candleIds, symbolName);
        }
    }

    [Fact]
    public async Task ExactEndCandle_ExcludedByBothV2RunnerPaths()
    {
        await using var factory = new E1BTestFactory();
        long? experimentId = null;
        var candleIds = new List<long>();
        string symbolName = $"E1B_END_{Guid.NewGuid():N}"[..16];

        try
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var sp = scope.ServiceProvider;
            var db = sp.GetRequiredService<MomoQuantDbContext>();

            var testExchange = await db.Exchanges.AsNoTracking().OrderBy(e => e.Id).FirstAsync();
            var evalEnd = EvalStart.AddMinutes(30 * 15);
            var boundary = evalEnd.AddMinutes(30);

            var symbol = new Symbol
            {
                ExchangeId = testExchange.Id,
                SymbolName = symbolName,
                BaseAsset = "E1B",
                QuoteAsset = "USDT",
                ContractType = ContractType.Perpetual,
                IsActive = true
            };
            db.Symbols.Add(symbol);
            await db.SaveChangesAsync();

            var warmupStart = EvalStart.AddMinutes(-RequiredWarmup * 15);
            var allCandles = new List<Candle>();
            for (var i = 0; i < RequiredWarmup + 30; i++)
            {
                var open = warmupStart.AddMinutes(i * 15);
                allCandles.Add(new Candle
                {
                    ExchangeId = testExchange.Id,
                    SymbolId = symbol.Id,
                    Timeframe = Timeframe.M15,
                    OpenTimeUtc = open,
                    CloseTimeUtc = open.AddMinutes(15),
                    Open = 50000m + i * 10,
                    High = 50100m + i * 10,
                    Low = 49900m + i * 10,
                    Close = 50050m + i * 10,
                    Volume = 1000m + i,
                    IsClosed = true,
                    CreatedAtUtc = DateTime.UtcNow
                });
            }

            var exactEndCandle = new Candle
            {
                ExchangeId = testExchange.Id,
                SymbolId = symbol.Id,
                Timeframe = Timeframe.M15,
                OpenTimeUtc = evalEnd,
                CloseTimeUtc = evalEnd.AddMinutes(15),
                Open = 99000m,
                High = 99100m,
                Low = 98900m,
                Close = 99050m,
                Volume = 9999m,
                IsClosed = true,
                CreatedAtUtc = DateTime.UtcNow
            };
            allCandles.Add(exactEndCandle);

            db.Candles.AddRange(allCandles);
            await db.SaveChangesAsync();
            candleIds.AddRange(allCandles.Select(c => c.Id));

            var dbCandlesInDB = await db.Candles
                .Where(c => c.SymbolId == symbol.Id && c.OpenTimeUtc == evalEnd)
                .CountAsync();
            Assert.Equal(1, dbCandlesInDB);

            var dataLoader = sp.GetRequiredService<IBacktestDataLoader>();
            var normalDataset = await dataLoader.LoadSymbolTimeframeAsync(
                testExchange.Id,
                symbol.Id,
                Timeframe.M15,
                EvalStart,
                evalEnd,
                RequiredWarmup,
                StrategyLabCandleLoadContractVersions.ExactExclusiveV2);

            Assert.DoesNotContain(normalDataset.Candles, c => c.OpenTimeUtc == evalEnd);

            experimentId = 9006;
            var scopeFactory = sp.GetRequiredService<IValidationTrainingCandleScopeFactory>();
            var scopeRequest = new ValidationTrainingCandleScopeRequest
            {
                ValidationExperimentId = experimentId.Value,
                SymbolId = symbol.Id,
                SymbolName = symbolName,
                Timeframe = "15m",
                TrainingEvaluationStartUtc = EvalStart,
                TrainingEvaluationEndExclusiveUtc = evalEnd,
                ValidationBoundaryUtc = boundary,
                RequiredWarmupCandleCount = RequiredWarmup,
                RequirementsVersion = StrategyExecutionRequirements.Version,
                StrategyCode = StrategyCodes.DonchianBreakout
            };

            var trainingScope = await scopeFactory.CreateAsync(scopeRequest);
            var validationSource = new ValidationTrainingStrategyLabCandleDataSource(trainingScope, "E1B.End");

            var run = new StrategyLabRun
            {
                Id = 1,
                Name = "e1b-end-test",
                StrategyCode = StrategyCodes.DonchianBreakout,
                StrategyVersion = "1.0.0",
                ExchangeId = testExchange.Id,
                SymbolId = symbol.Id,
                Symbol = symbolName,
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

            Assert.DoesNotContain(validationDataset.Candles, c => c.OpenTimeUtc == evalEnd);
        }
        finally
        {
            await CleanupAsync(factory, experimentId, candleIds, symbolName);
        }
    }

    [Fact]
    public async Task ValidationBoundaryCandle_ExcludedByValidationRunner()
    {
        await using var factory = new E1BTestFactory();
        long? experimentId = null;
        var candleIds = new List<long>();
        string symbolName = $"E1B_BND_{Guid.NewGuid():N}"[..16];

        try
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var sp = scope.ServiceProvider;
            var db = sp.GetRequiredService<MomoQuantDbContext>();

            var testExchange = await db.Exchanges.AsNoTracking().OrderBy(e => e.Id).FirstAsync();
            var evalEnd = EvalStart.AddMinutes(30 * 15);
            var boundary = evalEnd.AddMinutes(15);

            var symbol = new Symbol
            {
                ExchangeId = testExchange.Id,
                SymbolName = symbolName,
                BaseAsset = "E1B",
                QuoteAsset = "USDT",
                ContractType = ContractType.Perpetual,
                IsActive = true
            };
            db.Symbols.Add(symbol);
            await db.SaveChangesAsync();

            var warmupStart = EvalStart.AddMinutes(-RequiredWarmup * 15);
            var allCandles = new List<Candle>();
            for (var i = 0; i < RequiredWarmup + 30; i++)
            {
                var open = warmupStart.AddMinutes(i * 15);
                allCandles.Add(new Candle
                {
                    ExchangeId = testExchange.Id,
                    SymbolId = symbol.Id,
                    Timeframe = Timeframe.M15,
                    OpenTimeUtc = open,
                    CloseTimeUtc = open.AddMinutes(15),
                    Open = 50000m + i * 10,
                    High = 50100m + i * 10,
                    Low = 49900m + i * 10,
                    Close = 50050m + i * 10,
                    Volume = 1000m + i,
                    IsClosed = true,
                    CreatedAtUtc = DateTime.UtcNow
                });
            }

            var boundaryCandle = new Candle
            {
                ExchangeId = testExchange.Id,
                SymbolId = symbol.Id,
                Timeframe = Timeframe.M15,
                OpenTimeUtc = boundary,
                CloseTimeUtc = boundary.AddMinutes(15),
                Open = 99500m,
                High = 99600m,
                Low = 99400m,
                Close = 99550m,
                Volume = 9998m,
                IsClosed = true,
                CreatedAtUtc = DateTime.UtcNow
            };
            allCandles.Add(boundaryCandle);

            db.Candles.AddRange(allCandles);
            await db.SaveChangesAsync();
            candleIds.AddRange(allCandles.Select(c => c.Id));

            var dbBoundaryCandle = await db.Candles
                .Where(c => c.SymbolId == symbol.Id && c.OpenTimeUtc == boundary)
                .CountAsync();
            Assert.Equal(1, dbBoundaryCandle);

            experimentId = 9007;
            var scopeFactory = sp.GetRequiredService<IValidationTrainingCandleScopeFactory>();
            var scopeRequest = new ValidationTrainingCandleScopeRequest
            {
                ValidationExperimentId = experimentId.Value,
                SymbolId = symbol.Id,
                SymbolName = symbolName,
                Timeframe = "15m",
                TrainingEvaluationStartUtc = EvalStart,
                TrainingEvaluationEndExclusiveUtc = evalEnd,
                ValidationBoundaryUtc = boundary,
                RequiredWarmupCandleCount = RequiredWarmup,
                RequirementsVersion = StrategyExecutionRequirements.Version,
                StrategyCode = StrategyCodes.DonchianBreakout
            };

            var trainingScope = await scopeFactory.CreateAsync(scopeRequest);
            var validationSource = new ValidationTrainingStrategyLabCandleDataSource(trainingScope, "E1B.Boundary");

            var run = new StrategyLabRun
            {
                Id = 1,
                Name = "e1b-boundary-test",
                StrategyCode = StrategyCodes.DonchianBreakout,
                StrategyVersion = "1.0.0",
                ExchangeId = testExchange.Id,
                SymbolId = symbol.Id,
                Symbol = symbolName,
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

            Assert.DoesNotContain(validationDataset.Candles, c => c.OpenTimeUtc == boundary);
        }
        finally
        {
            await CleanupAsync(factory, experimentId, candleIds, symbolName);
        }
    }

    [Fact]
    public async Task ConstructionFailure_PreventsStrategyEvaluation()
    {
        await using var factory = new E1BTestFactory();

        await using var scope = factory.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var scopeFactory = sp.GetRequiredService<IValidationTrainingCandleScopeFactory>();

        var invalidRequest = new ValidationTrainingCandleScopeRequest
        {
            ValidationExperimentId = 9999,
            SymbolId = 1,
            SymbolName = "INVALID",
            Timeframe = "15m",
            TrainingEvaluationStartUtc = EvalStart,
            TrainingEvaluationEndExclusiveUtc = EvalStart.AddMinutes(-100 * 15),
            ValidationBoundaryUtc = EvalStart,
            RequiredWarmupCandleCount = 50,
            RequirementsVersion = StrategyExecutionRequirements.Version,
            StrategyCode = StrategyCodes.DonchianBreakout
        };

        await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await scopeFactory.CreateAsync(invalidRequest);
        });
    }

    [Fact]
    public async Task FixtureCleanup_RemovesRunsCandidatesCandlesAuditsAndTestEntities()
    {
        await using var factory = new E1BTestFactory();
        long? experimentId = null;
        var candleIds = new List<long>();
        string symbolName = $"E1B_CLN_{Guid.NewGuid():N}"[..16];

        try
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var sp = scope.ServiceProvider;
            var db = sp.GetRequiredService<MomoQuantDbContext>();

            var testExchange = await db.Exchanges.AsNoTracking().OrderBy(e => e.Id).FirstAsync();
            var evalEnd = EvalStart.AddMinutes(30 * 15);

            var symbol = new Symbol
            {
                ExchangeId = testExchange.Id,
                SymbolName = symbolName,
                BaseAsset = "E1B",
                QuoteAsset = "USDT",
                ContractType = ContractType.Perpetual,
                IsActive = true
            };
            db.Symbols.Add(symbol);
            await db.SaveChangesAsync();

            var warmupStart = EvalStart.AddMinutes(-RequiredWarmup * 15);
            var allCandles = new List<Candle>();
            for (var i = 0; i < RequiredWarmup + 30; i++)
            {
                var open = warmupStart.AddMinutes(i * 15);
                allCandles.Add(new Candle
                {
                    ExchangeId = testExchange.Id,
                    SymbolId = symbol.Id,
                    Timeframe = Timeframe.M15,
                    OpenTimeUtc = open,
                    CloseTimeUtc = open.AddMinutes(15),
                    Open = 50000m + i * 10,
                    High = 50100m + i * 10,
                    Low = 49900m + i * 10,
                    Close = 50050m + i * 10,
                    Volume = 1000m + i,
                    IsClosed = true,
                    CreatedAtUtc = DateTime.UtcNow
                });
            }

            db.Candles.AddRange(allCandles);
            await db.SaveChangesAsync();
            candleIds.AddRange(allCandles.Select(c => c.Id));

            var symbolIdToClean = symbol.Id;

            await CleanupAsync(factory, experimentId, candleIds, symbolName);

            var remainingCandles = await db.Candles.Where(c => candleIds.Contains(c.Id)).CountAsync();
            Assert.Equal(0, remainingCandles);

            var remainingSymbol = await db.Symbols.Where(s => s.Id == symbolIdToClean).FirstOrDefaultAsync();
            Assert.Null(remainingSymbol);

            candleIds.Clear();
        }
        finally
        {
            await CleanupAsync(factory, experimentId, candleIds, symbolName);
        }
    }

    [Fact]
    public async Task MigrationFreshAndUpgradePaths_Pass()
    {
        await using var factory = new E1BTestFactory();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();

        var pending = await db.Database.GetPendingMigrationsAsync();
        var applied = await db.Database.GetAppliedMigrationsAsync();

        Assert.Empty(pending);
        Assert.Contains(applied, m => m.Contains("M230E1"));
    }

    [Fact]
    public async Task NoPendingModelChanges()
    {
        await using var factory = new E1BTestFactory();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();

        var pending = await db.Database.GetPendingMigrationsAsync();
        Assert.Empty(pending);
    }

    private static async Task CleanupAsync(
        MomoQuantWebApplicationFactory factory,
        long? experimentId,
        List<long> candleIds,
        string symbolName)
    {
        if (candleIds.Count == 0 && experimentId is null && string.IsNullOrEmpty(symbolName))
        {
            return;
        }

        try
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();

            if (experimentId.HasValue)
            {
                await db.ValidationCandleAccessAudits
                    .Where(a => a.ValidationExperimentId == experimentId.Value)
                    .ExecuteDeleteAsync();
            }

            if (!string.IsNullOrEmpty(symbolName))
            {
                var symbol = await db.Symbols.FirstOrDefaultAsync(s => s.SymbolName == symbolName);
                if (symbol != null)
                {
                    var runs = await db.StrategyLabRuns.Where(r => r.SymbolId == symbol.Id).ToListAsync();
                    var runIds = runs.Select(r => r.Id).ToList();

                    if (runIds.Any())
                    {
                        await db.StrategyResearchCandidates
                            .Where(c => runIds.Contains(c.StrategyLabRunId))
                            .ExecuteDeleteAsync();

                        db.StrategyLabRuns.RemoveRange(runs);
                    }

                    if (candleIds.Count > 0)
                    {
                        await db.Candles.Where(c => candleIds.Contains(c.Id)).ExecuteDeleteAsync();
                    }
                    else
                    {
                        await db.Candles.Where(c => c.SymbolId == symbol.Id).ExecuteDeleteAsync();
                    }

                    db.Symbols.Remove(symbol);
                }
            }

            await db.SaveChangesAsync();
        }
        catch
        {
        }
    }

    private sealed class E1BTestFactory : MomoQuantWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IStrategyRegistry>();
                    services.AddSingleton<IStrategyRegistry, E1BStrategyRegistry>();

                services.RemoveAll<IStrategyExecutionRequirementsResolver>();
                services.AddSingleton<IStrategyExecutionRequirementsResolver, FixedWarmupRequirementsResolver>();

                services.RemoveAll<IHistoricalCandleCoverageService>();
                services.AddSingleton<IHistoricalCandleCoverageService, AlwaysSucceedCoverageService>();
            });
        }
    }

    private sealed class E1BStrategyRegistry : IStrategyRegistry
    {
        private readonly DeterministicE1BStrategy _strategy = new();

        public IReadOnlyCollection<ITradingStrategy> GetAll() => new[] { _strategy };

        public ITradingStrategy? GetByCode(StrategyCode code) =>
            code == StrategyCode.DonchianBreakout ? _strategy : null;

        public IReadOnlyCollection<ITradingStrategy> GetEnabled(IReadOnlyCollection<StrategyCode> enabledCodes) =>
            enabledCodes.Contains(StrategyCode.DonchianBreakout) ? new[] { _strategy } : Array.Empty<ITradingStrategy>();
    }

    private sealed class DeterministicE1BStrategy : StrategyBase
    {
        public override StrategyCode Code => StrategyCode.DonchianBreakout;
        public override string Name => "Deterministic E1B";
        public override string Description => "Test strategy emitting entries every 10 candles";
        public override IReadOnlyCollection<MarketRegime> SupportedRegimes => new[] { MarketRegime.Trending, MarketRegime.Ranging };
        public override IReadOnlyCollection<Timeframe> SupportedTimeframes => new[] { Timeframe.M15 };

        public override StrategySignalResult Evaluate(StrategyContext context)
        {
            if (!context.CurrentCandleIndex.HasValue)
                return NoTrade("no-index");

            var index = context.CurrentCandleIndex.Value;

            if (index < 10)
                return NoTrade("warmup");

            if (index % 10 != 0)
                return NoTrade("not-trigger");

            var direction = (index / 10) % 2 == 0 ? TradeDirection.Long : TradeDirection.Short;
            var currentCandle = context.Candles[index];
            var entry = currentCandle.Close;
            var stop = direction == TradeDirection.Long ? entry - 1m : entry + 1m;
            var target = direction == TradeDirection.Long ? entry + 2m : entry - 2m;

            var fingerprint = MomoQuant.Application.Strategies.PriceStructure.SetupFingerprintHasher.Hash(
                $"e1b|{index}|{direction}|{entry:G29}|{stop:G29}|{target:G29}");
            var rawData = JsonSerializer.Serialize(new { setupFingerprint = fingerprint });

            return Entry(
                direction,
                strength: 75m,
                confidenceContribution: 70m,
                entryPrice: entry,
                stopLoss: stop,
                takeProfit: target,
                reason: "E1B-Entry",
                rawDataJson: rawData);
        }
    }

    private sealed class FixedWarmupRequirementsResolver : IStrategyExecutionRequirementsResolver
    {
        public Task<ServiceResult<StrategyExecutionRequirements>> ResolveAsync(
            ResolveStrategyExecutionRequirementsRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result(request.StrategyCode ?? StrategyCodes.DonchianBreakout, request.StrategyVersion));

        public Task<ServiceResult<StrategyExecutionRequirements>> ResolveByStrategyIdAsync(
            long strategyId,
            string? strategyVersion = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result(StrategyCodes.DonchianBreakout, strategyVersion));

        private static ServiceResult<StrategyExecutionRequirements> Result(string? code, string? version) =>
            ServiceResult<StrategyExecutionRequirements>.Ok(new StrategyExecutionRequirements
            {
                StrategyId = 1,
                StrategyCode = code ?? StrategyCodes.DonchianBreakout,
                StrategyVersion = version ?? "1.0.0",
                RequiredWarmupCandleCount = code == StrategyCodes.DonchianBreakout ? RequiredWarmup : 50,
                RequirementsVersion = "E1B/v1"
            });
    }

    private sealed class AlwaysSucceedCoverageService : IHistoricalCandleCoverageService
    {
        public Task<ServiceResult<HistoricalCandleCoverageResult>> EnsureCoverageAsync(
            long exchangeId,
            long symbolId,
            string timeframe,
            DateTime fromUtc,
            DateTime toUtc,
            int warmupCandles,
            bool allowAutoImport,
            Func<HistoricalCoverageProgress, CancellationToken, Task>? onProgress = null,
            CancellationToken cancellationToken = default)
        {
            var result = new HistoricalCandleCoverageResult
            {
                Coverage = new CandleCoverageDto
                {
                    Symbol = "TEST",
                    Exchange = "TEST",
                    Timeframe = timeframe,
                    RequiredFromUtc = fromUtc,
                    RequiredToUtc = toUtc,
                    AvailableFromUtc = fromUtc,
                    AvailableToUtc = toUtc,
                    CandleCount = 0,
                    MissingCandleCountEstimate = 0,
                    CoverageStatus = "Complete",
                    ImportedDuringRun = false
                },
                CoverageCheckStartedAtUtc = DateTime.UtcNow,
                RequestedFromUtc = fromUtc,
                RequestedToUtc = toUtc,
                RequestedTimeframe = timeframe,
                ExistingCandleCount = 0,
                MissingRanges = new List<CoverageMissingRange>(),
                AutoImportAttempted = false
            };
            return Task.FromResult(ServiceResult<HistoricalCandleCoverageResult>.Ok(result));
        }

        public Task<HistoricalCandleCoverageResult> CheckCoverageAsync(
            long exchangeId,
            long symbolId,
            string timeframe,
            DateTime fromUtc,
            DateTime toUtc,
            int warmupCandles = 0,
            CancellationToken cancellationToken = default)
        {
            var result = new HistoricalCandleCoverageResult
            {
                Coverage = new CandleCoverageDto
                {
                    Symbol = "TEST",
                    Exchange = "TEST",
                    Timeframe = timeframe,
                    RequiredFromUtc = fromUtc,
                    RequiredToUtc = toUtc,
                    AvailableFromUtc = fromUtc,
                    AvailableToUtc = toUtc,
                    CandleCount = 0,
                    MissingCandleCountEstimate = 0,
                    CoverageStatus = "Complete",
                    ImportedDuringRun = false
                },
                CoverageCheckStartedAtUtc = DateTime.UtcNow,
                RequestedFromUtc = fromUtc,
                RequestedToUtc = toUtc,
                RequestedTimeframe = timeframe,
                ExistingCandleCount = 0,
                MissingRanges = new List<CoverageMissingRange>(),
                AutoImportAttempted = false
            };
            return Task.FromResult(result);
        }
    }

    private static class CanonicalCandidateHasher
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        public static string ComputeHash(IReadOnlyList<StrategyResearchCandidate> candidates)
        {
            var ordered = candidates
                .OrderBy(c => c.ProposedEntryTimeUtc)
                .ThenBy(c => c.SetupFingerprint)
                .ThenBy(c => c.Direction)
                .ThenBy(c => c.ProposedEntryPrice)
                .ToList();

            var snapshots = ordered.Select(CreateSnapshot).ToList();
            var json = JsonSerializer.Serialize(snapshots, JsonOptions);
            var bytes = Encoding.UTF8.GetBytes(json);
            var hash = SHA256.HashData(bytes);
            return Convert.ToHexString(hash);
        }

        private static object CreateSnapshot(StrategyResearchCandidate c) => new
        {
            StrategyCode = c.StrategyCode,
            StrategyVersion = c.StrategyVersion,
            ExchangeId = c.ExchangeId,
            SymbolId = c.SymbolId,
            Symbol = c.Symbol,
            Timeframe = c.Timeframe,
            Direction = c.Direction.ToString(),
            SetupDetectedAtUtc = c.SetupDetectedAtUtc.ToString("o", CultureInfo.InvariantCulture),
            ProposedEntryTimeUtc = c.ProposedEntryTimeUtc.ToString("o", CultureInfo.InvariantCulture),
            ProposedEntryPrice = c.ProposedEntryPrice.ToString("F8", CultureInfo.InvariantCulture),
            StopLoss = c.StopLoss.ToString("F8", CultureInfo.InvariantCulture),
            Target1 = c.Target1.ToString("F8", CultureInfo.InvariantCulture),
            Target2 = c.Target2?.ToString("F8", CultureInfo.InvariantCulture),
            RewardRisk = c.RewardRisk.ToString("F8", CultureInfo.InvariantCulture),
            StrategyReason = c.StrategyReason,
            SetupFingerprint = c.SetupFingerprint,
            ParametersJson = c.ParametersJson,
            StructureJson = c.StructureJson
        };
    }
}
