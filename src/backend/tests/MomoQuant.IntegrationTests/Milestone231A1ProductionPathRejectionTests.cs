using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Common;
using MomoQuant.Application.Backtesting;
using MomoQuant.Application.Backtesting.Dtos;
using MomoQuant.Application.Optimization;
using MomoQuant.Application.Optimization.Dtos;
using MomoQuant.Application.PaperTrading;
using MomoQuant.Application.PaperTrading.Dtos;
using MomoQuant.Application.Replay;
using MomoQuant.Application.Replay.Dtos;
using MomoQuant.Application.Strategies;
using MomoQuant.Application.Strategies.Dtos;
using MomoQuant.Application.Strategies.Models;
using MomoQuant.Application.StrategyBenchmarks;
using MomoQuant.Application.StrategyBenchmarks.Dtos;
using MomoQuant.Application.Validation.Dtos;
using MomoQuant.Application.ValidationLab;
using MomoQuant.Application.ValidationLab.Dtos;
using MomoQuant.Domain.Benchmarks;
using MomoQuant.Domain.Constants;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.MarketData;
using MomoQuant.Domain.Strategies;
using MomoQuant.Persistence;

namespace MomoQuant.IntegrationTests;

/// <summary>
/// Milestone 23.1A1C — production services + MySQL-backed repos reject archived/unsupported
/// requests with ZERO durable rows and ZERO queue / runner / research-executor / engine calls.
/// </summary>
[Collection("Integration")]
public sealed class Milestone231A1ProductionPathRejectionTests : IClassFixture<Milestone231A1RejectionFactory>
{
    private readonly Milestone231A1RejectionFactory _factory;

    public Milestone231A1ProductionPathRejectionTests(Milestone231A1RejectionFactory factory)
    {
        _factory = factory;
        _factory.ResetTrackers();
    }

    [Fact]
    public async Task BacktestRunner_ArchivedStrategy_LeavesZeroDurableBacktestRuns()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
        var refs = await LoadRefsAsync(db);
        var archived = await EnsureArchivedAsync(db, StrategyCode.EmaPullback, "EMA Pullback");
        var marker = $"m231a1c-bt-{Guid.NewGuid():N}";

        var before = await CountDurableAsync(db);
        var runner = scope.ServiceProvider.GetRequiredService<IBacktestRunner>();

        var result = await runner.RunAsync(new RunBacktestRequest
        {
            Name = marker,
            ExchangeId = refs.ExchangeId,
            SymbolIds = [refs.SymbolId],
            Timeframes = ["5m"],
            FromUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            ToUtc = new DateTime(2026, 1, 31, 0, 0, 0, DateTimeKind.Utc),
            InitialBalance = 10_000m,
            RiskProfileId = refs.RiskProfileId,
            StrategyIds = [archived.Id],
            AutoImportMissingCandles = false,
            RunAnyway = true
        });

        Assert.False(result.Succeeded);
        Assert.Contains("archived", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, await db.BacktestRuns.CountAsync(r => r.Name == marker));
        await AssertDurableUnchangedAsync(db, before);
        _factory.AssertNoSideEffects();
    }

    [Fact]
    public async Task ReplaySessionService_ArchivedStrategy_LeavesZeroDurableReplaySessions()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
        var refs = await LoadRefsAsync(db);
        var archived = await EnsureArchivedAsync(db, StrategyCode.EmaPullback, "EMA Pullback");
        var marker = $"m231a1c-rp-{Guid.NewGuid():N}";

        var before = await CountDurableAsync(db);
        var service = scope.ServiceProvider.GetRequiredService<IReplaySessionService>();

        var result = await service.CreateAsync(new CreateReplaySessionRequest
        {
            Name = marker,
            ExchangeId = refs.ExchangeId,
            SymbolId = refs.SymbolId,
            Timeframe = "5m",
            FromUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            ToUtc = new DateTime(2026, 1, 31, 0, 0, 0, DateTimeKind.Utc),
            InitialBalance = 10_000m,
            RiskProfileId = refs.RiskProfileId,
            StrategyIds = [archived.Id]
        });

        Assert.False(result.Succeeded);
        Assert.Contains("archived", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, await db.ReplaySessions.CountAsync(r => r.Name == marker));
        await AssertDurableUnchangedAsync(db, before);
        _factory.AssertNoSideEffects();
    }

    [Fact]
    public async Task PaperSessionService_ArchivedStrategy_LeavesZeroDurablePaperSessions()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
        var refs = await LoadRefsAsync(db);
        var archived = await EnsureArchivedAsync(db, StrategyCode.FourHourRangeReEntry, "4H Range Re-entry");
        var marker = $"m231a1c-pp-{Guid.NewGuid():N}";

        var before = await CountDurableAsync(db);
        var service = scope.ServiceProvider.GetRequiredService<IPaperSessionService>();

        var result = await service.CreateAsync(new CreatePaperSessionRequest
        {
            Name = marker,
            PaperAccountId = refs.PaperAccountId,
            ExchangeId = refs.ExchangeId,
            SymbolIds = [refs.SymbolId],
            Timeframes = ["5m"],
            Mode = "HistoricalPaper",
            FromUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            ToUtc = new DateTime(2026, 1, 31, 0, 0, 0, DateTimeKind.Utc),
            RiskProfileId = refs.RiskProfileId,
            StrategyIds = [archived.Id]
        });

        Assert.False(result.Succeeded);
        Assert.Contains("archived", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, await db.PaperTradingSessions.CountAsync(r => r.Name == marker));
        await AssertDurableUnchangedAsync(db, before);
        _factory.AssertNoSideEffects();
    }

    [Fact]
    public async Task StrategyBenchmarkService_ArchivedStrategy_LeavesZeroDurableBenchmarkRuns()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
        var refs = await LoadRefsAsync(db);
        var archived = await EnsureArchivedAsync(db, StrategyCode.EmaPullback, "EMA Pullback");
        var marker = $"m231a1c-bm-{Guid.NewGuid():N}";

        var before = await CountDurableAsync(db);
        var service = scope.ServiceProvider.GetRequiredService<IStrategyBenchmarkService>();

        var result = await service.CreateAsync(new CreateStrategyBenchmarkRequest
        {
            Name = marker,
            ExchangeCode = "BINANCE_FUTURES",
            Symbols = [refs.SymbolName],
            StrategyIds = [archived.Id],
            BenchmarkFromDate = new DateOnly(2026, 6, 1),
            BenchmarkToDate = new DateOnly(2026, 6, 10),
            WarmupFromDate = new DateOnly(2026, 5, 25),
            RiskProfileId = refs.RiskProfileId,
            IncludeDisabledStrategies = true
        });

        Assert.False(result.Succeeded);
        Assert.Contains("archived", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, await db.StrategyBenchmarkRuns.CountAsync(r => r.Name == marker));
        await AssertDurableUnchangedAsync(db, before);
        Assert.Equal(0, _factory.Queue.EnqueueCount);
        _factory.AssertNoSideEffects();
    }

    [Fact]
    public async Task ParameterOptimizationService_Adaptive_LeavesZeroDurableOptimizationRuns()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
        var refs = await LoadRefsAsync(db);
        var before = await CountDurableAsync(db);
        var service = scope.ServiceProvider.GetRequiredService<IParameterOptimizationService>();

        var result = await service.RunAsync(new RunParameterOptimizationRequest
        {
            StrategyCode = StrategyCodes.MomoAdaptiveMultiTimeframeTrendBreakout,
            ExchangeId = refs.ExchangeId,
            SymbolId = refs.SymbolId,
            Timeframe = "5m",
            FromUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            ToUtc = new DateTime(2026, 1, 31, 0, 0, 0, DateTimeKind.Utc),
            OptimizationMode = ParameterOptimizationMode.GridSearch,
            MaxCombinations = 10,
            MaxRuntimeMinutes = 5
        }, userId: 1);

        Assert.False(result.Succeeded);
        Assert.Contains("does not support parameter optimization", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        await AssertDurableUnchangedAsync(db, before);
        Assert.Equal(0, _factory.Research.CallCount);
        _factory.AssertNoSideEffects();
    }

    [Fact]
    public async Task ParameterOptimizationService_Range_LeavesZeroDurableOptimizationRuns()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
        var refs = await LoadRefsAsync(db);
        var before = await CountDurableAsync(db);
        var service = scope.ServiceProvider.GetRequiredService<IParameterOptimizationService>();

        var result = await service.RunAsync(new RunParameterOptimizationRequest
        {
            StrategyCode = StrategyCodes.MomoVolatilityRangeReversion,
            ExchangeId = refs.ExchangeId,
            SymbolId = refs.SymbolId,
            Timeframe = "15m",
            FromUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            ToUtc = new DateTime(2026, 1, 31, 0, 0, 0, DateTimeKind.Utc),
            OptimizationMode = ParameterOptimizationMode.GridSearch,
            MaxCombinations = 10,
            MaxRuntimeMinutes = 5
        }, userId: 1);

        Assert.False(result.Succeeded);
        Assert.Contains("does not support parameter optimization", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        await AssertDurableUnchangedAsync(db, before);
        Assert.Equal(0, _factory.Research.CallCount);
        _factory.AssertNoSideEffects();
    }

    [Fact]
    public async Task StrategyValidationService_Adaptive_FailsBeforeResearchExecutor()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
        var refs = await LoadRefsAsync(db);
        var before = await CountDurableAsync(db);
        var service = scope.ServiceProvider.GetRequiredService<IStrategyValidationService>();

        var result = await service.RunAsync(new RunStrategyValidationRequest
        {
            StrategyCode = StrategyCodes.MomoAdaptiveMultiTimeframeTrendBreakout,
            ExchangeId = refs.ExchangeId,
            SymbolId = refs.SymbolId,
            Timeframe = "15m",
            FromUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            ToUtc = new DateTime(2026, 1, 31, 0, 0, 0, DateTimeKind.Utc)
        });

        Assert.False(result.Succeeded);
        Assert.Contains("does not support Validation Laboratory", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        await AssertDurableUnchangedAsync(db, before);
        Assert.Equal(0, _factory.Research.CallCount);
        _factory.AssertNoSideEffects();
    }

    [Fact]
    public async Task StrategyValidationService_Range_FailsBeforeResearchExecutor()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
        var refs = await LoadRefsAsync(db);
        var before = await CountDurableAsync(db);
        var service = scope.ServiceProvider.GetRequiredService<IStrategyValidationService>();

        var result = await service.RunAsync(new RunStrategyValidationRequest
        {
            StrategyCode = StrategyCodes.MomoVolatilityRangeReversion,
            ExchangeId = refs.ExchangeId,
            SymbolId = refs.SymbolId,
            Timeframe = "15m",
            FromUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            ToUtc = new DateTime(2026, 1, 31, 0, 0, 0, DateTimeKind.Utc)
        });

        Assert.False(result.Succeeded);
        Assert.Contains("does not support Validation Laboratory", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        await AssertDurableUnchangedAsync(db, before);
        Assert.Equal(0, _factory.Research.CallCount);
        _factory.AssertNoSideEffects();
    }

    [Fact]
    public async Task ValidationLabService_Adaptive_LeavesZeroDurableValidationExperiments()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
        var refs = await LoadRefsAsync(db);
        var marker = $"m231a1c-vl-a-{Guid.NewGuid():N}";
        var before = await CountDurableAsync(db);
        var service = scope.ServiceProvider.GetRequiredService<IValidationLabService>();

        var result = await service.CreateExperimentAsync(new CreateValidationExperimentRequest
        {
            Name = marker,
            StrategyCode = StrategyCodes.MomoAdaptiveMultiTimeframeTrendBreakout,
            ExchangeId = refs.ExchangeId,
            SymbolId = refs.SymbolId,
            Timeframe = "15m",
            RequestedStartUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            RequestedEndUtc = new DateTime(2026, 1, 31, 0, 0, 0, DateTimeKind.Utc)
        });

        Assert.False(result.Succeeded);
        Assert.Contains("does not support Validation Laboratory", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, await db.ValidationExperiments.CountAsync(e => e.Name == marker));
        await AssertDurableUnchangedAsync(db, before);
        _factory.AssertNoSideEffects();
    }

    [Fact]
    public async Task ValidationLabService_Range_LeavesZeroDurableValidationExperiments()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
        var refs = await LoadRefsAsync(db);
        var marker = $"m231a1c-vl-r-{Guid.NewGuid():N}";
        var before = await CountDurableAsync(db);
        var service = scope.ServiceProvider.GetRequiredService<IValidationLabService>();

        var result = await service.CreateExperimentAsync(new CreateValidationExperimentRequest
        {
            Name = marker,
            StrategyCode = StrategyCodes.MomoVolatilityRangeReversion,
            ExchangeId = refs.ExchangeId,
            SymbolId = refs.SymbolId,
            Timeframe = "15m",
            RequestedStartUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            RequestedEndUtc = new DateTime(2026, 1, 31, 0, 0, 0, DateTimeKind.Utc)
        });

        Assert.False(result.Succeeded);
        Assert.Contains("does not support Validation Laboratory", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, await db.ValidationExperiments.CountAsync(e => e.Name == marker));
        await AssertDurableUnchangedAsync(db, before);
        _factory.AssertNoSideEffects();
    }

    [Fact]
    public async Task StrategyBenchmarkRunner_StaleArchivedPayload_FailsBeforeBacktestRunner()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
        var refs = await LoadRefsAsync(db);
        var archived = await EnsureArchivedAsync(db, StrategyCode.EmaPullback, "EMA Pullback");
        var marker = $"m231a1c-stale-{Guid.NewGuid():N}";

        var resultsBefore = await db.StrategyBenchmarkResults.CountAsync();
        var run = new StrategyBenchmarkRun
        {
            Name = marker,
            Status = StrategyBenchmarkStatus.Pending,
            ExchangeId = refs.ExchangeId,
            SymbolsJson = StrategyBenchmarkMapper.SerializeList([refs.SymbolName]),
            TimeframesJson = StrategyBenchmarkMapper.SerializeList(["5m"]),
            StrategyIdsJson = StrategyBenchmarkMapper.SerializeList([archived.Id]),
            BenchmarkFromUtc = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            BenchmarkToUtc = new DateTime(2026, 6, 10, 0, 0, 0, DateTimeKind.Utc),
            WarmupFromUtc = new DateTime(2026, 5, 25, 0, 0, 0, DateTimeKind.Utc),
            WarmupToUtc = new DateTime(2026, 5, 31, 0, 0, 0, DateTimeKind.Utc),
            InitialBalance = 10_000m,
            RiskProfileId = refs.RiskProfileId,
            ExecutionMode = ExecutionMode.MarketFill,
            ConfigJson = "{}",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        db.StrategyBenchmarkRuns.Add(run);
        await db.SaveChangesAsync();

        var runner = scope.ServiceProvider.GetRequiredService<IStrategyBenchmarkRunner>();
        await runner.ExecuteAsync(run.Id);

        await db.Entry(run).ReloadAsync();
        Assert.Equal(StrategyBenchmarkStatus.Failed, run.Status);
        Assert.Contains("archived", run.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(resultsBefore, await db.StrategyBenchmarkResults.CountAsync());
        Assert.Equal(0, _factory.BacktestRunner.CallCount);
        Assert.Equal(0, _factory.Queue.EnqueueCount);
        Assert.Equal(0, _factory.Research.CallCount);
        Assert.Equal(0, _factory.Engine.CallCount);

        db.StrategyBenchmarkRuns.Remove(run);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task StrategyService_ArchivedManualEvaluation_FailsBeforeEngine()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
        var refs = await LoadRefsAsync(db);
        var archived = await EnsureArchivedAsync(db, StrategyCode.EmaPullback, "EMA Pullback");

        var open = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var candle = new Candle
        {
            ExchangeId = refs.ExchangeId,
            SymbolId = refs.SymbolId,
            Timeframe = Timeframe.M5,
            OpenTimeUtc = open,
            CloseTimeUtc = open.AddMinutes(5),
            Open = 100m,
            High = 101m,
            Low = 99m,
            Close = 100.5m,
            Volume = 10m,
            IsClosed = true,
            CreatedAtUtc = DateTime.UtcNow
        };
        db.Candles.Add(candle);
        await db.SaveChangesAsync();

        var before = await CountDurableAsync(db);
        var service = scope.ServiceProvider.GetRequiredService<IStrategyService>();

        var result = await service.EvaluateAsync(new StrategyEvaluationRequest
        {
            SymbolId = refs.SymbolId,
            Timeframe = "5m",
            CandleId = candle.Id,
            MarketRegime = "Unknown",
            StrategyIds = [archived.Id]
        });

        Assert.False(result.Succeeded);
        Assert.Contains("archived", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        await AssertDurableUnchangedAsync(db, before);
        Assert.Equal(0, _factory.Engine.CallCount);
        _factory.AssertNoSideEffects();

        db.Candles.Remove(candle);
        await db.SaveChangesAsync();
    }

    private static async Task<Strategy> EnsureArchivedAsync(MomoQuantDbContext db, StrategyCode code, string name)
    {
        var existing = await db.Strategies.FirstOrDefaultAsync(s => s.Code == code);
        if (existing is not null)
        {
            if (existing.IsEnabled)
            {
                existing.IsEnabled = false;
                existing.UpdatedAtUtc = DateTime.UtcNow;
                await db.SaveChangesAsync();
            }

            return existing;
        }

        var created = new Strategy
        {
            Code = code,
            Name = name,
            Description = "Archived for durable rejection tests",
            IsEnabled = false,
            Version = "1.0.0",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        db.Strategies.Add(created);
        await db.SaveChangesAsync();
        return created;
    }

    private static async Task<Refs> LoadRefsAsync(MomoQuantDbContext db)
    {
        var exchange = await db.Exchanges.FirstOrDefaultAsync(e => e.Code == "BINANCE_FUTURES");
        if (exchange is null)
        {
            exchange = new Domain.Exchanges.Exchange
            {
                Code = "BINANCE_FUTURES",
                Name = "Binance Futures",
                BaseUrl = "https://fapi.binance.com",
                WebSocketUrl = "wss://fstream.binance.com",
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };
            db.Exchanges.Add(exchange);
            await db.SaveChangesAsync();
        }

        var symbol = await db.Symbols.FirstOrDefaultAsync(s =>
            s.ExchangeId == exchange.Id && s.SymbolName == "BTCUSDT");
        if (symbol is null)
        {
            symbol = new Domain.Exchanges.Symbol
            {
                ExchangeId = exchange.Id,
                SymbolName = "BTCUSDT",
                BaseAsset = "BTC",
                QuoteAsset = "USDT",
                ContractType = ContractType.Perpetual,
                PricePrecision = 2,
                QuantityPrecision = 3,
                MinQty = 0.001m,
                MinNotional = 5m,
                TickSize = 0.01m,
                StepSize = 0.001m,
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };
            db.Symbols.Add(symbol);
            await db.SaveChangesAsync();
        }

        var risk = await db.RiskProfiles.FirstOrDefaultAsync();
        if (risk is null)
        {
            risk = new Domain.Risk.RiskProfile
            {
                Name = "Default Rejection Test Risk",
                Description = "Created for Milestone 23.1A1C durable rejection tests",
                IsDefault = true,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };
            db.RiskProfiles.Add(risk);
            await db.SaveChangesAsync();
        }

        var paper = await db.PaperAccounts.FirstOrDefaultAsync(a => a.IsActive);
        if (paper is null)
        {
            paper = new Domain.PaperTrading.PaperAccount
            {
                Name = "Rejection Test Paper",
                InitialBalance = 10_000m,
                CurrentBalance = 10_000m,
                CurrentEquity = 10_000m,
                Currency = "USDT",
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };
            db.PaperAccounts.Add(paper);
            await db.SaveChangesAsync();
        }

        return new Refs(exchange.Id, symbol.Id, symbol.SymbolName, risk.Id, paper.Id);
    }

    private static async Task<DurableCounts> CountDurableAsync(MomoQuantDbContext db) =>
        new(
            await db.BacktestRuns.CountAsync(),
            await db.ReplaySessions.CountAsync(),
            await db.PaperTradingSessions.CountAsync(),
            await db.StrategyBenchmarkRuns.CountAsync(),
            await db.ParameterOptimizationRuns.CountAsync(),
            await db.ValidationExperiments.CountAsync());

    private static async Task AssertDurableUnchangedAsync(MomoQuantDbContext db, DurableCounts before)
    {
        var after = await CountDurableAsync(db);
        Assert.Equal(before.BacktestRuns, after.BacktestRuns);
        Assert.Equal(before.ReplaySessions, after.ReplaySessions);
        Assert.Equal(before.PaperTradingSessions, after.PaperTradingSessions);
        Assert.Equal(before.StrategyBenchmarkRuns, after.StrategyBenchmarkRuns);
        Assert.Equal(before.ParameterOptimizationRuns, after.ParameterOptimizationRuns);
        Assert.Equal(before.ValidationExperiments, after.ValidationExperiments);
    }

    private sealed record Refs(long ExchangeId, long SymbolId, string SymbolName, long RiskProfileId, long PaperAccountId);

    private sealed record DurableCounts(
        int BacktestRuns,
        int ReplaySessions,
        int PaperTradingSessions,
        int StrategyBenchmarkRuns,
        int ParameterOptimizationRuns,
        int ValidationExperiments);
}

public sealed class Milestone231A1RejectionFactory : MomoQuantWebApplicationFactory
{
    public TrackingBenchmarkQueue Queue { get; } = new();
    public TrackingResearchExecutor Research { get; } = new();
    public TrackingStrategyEngine Engine { get; } = new();
    public TrackingBacktestRunner BacktestRunner { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IStrategyBenchmarkQueue>();
            services.AddSingleton<IStrategyBenchmarkQueue>(Queue);

            services.RemoveAll<IStrategyResearchBacktestExecutor>();
            services.AddSingleton<IStrategyResearchBacktestExecutor>(Research);

            services.RemoveAll<IStrategyEngine>();
            services.AddSingleton<IStrategyEngine>(Engine);

            // Keep production BacktestRunner for create-path rejection tests, but wrap for worker assertions.
            services.DecorateBacktestRunner(BacktestRunner);
        });
    }

    public void ResetTrackers()
    {
        Queue.Reset();
        Research.Reset();
        Engine.Reset();
        BacktestRunner.Reset();
    }

    public void AssertNoSideEffects()
    {
        Assert.Equal(0, Queue.EnqueueCount);
        Assert.Equal(0, Research.CallCount);
        Assert.Equal(0, Engine.CallCount);
    }
}

public sealed class TrackingBenchmarkQueue : IStrategyBenchmarkQueue
{
    public int EnqueueCount { get; private set; }
    public void Enqueue(long benchmarkRunId) => EnqueueCount++;
    public void Reset() => EnqueueCount = 0;
}

public sealed class TrackingResearchExecutor : IStrategyResearchBacktestExecutor
{
    public int CallCount { get; private set; }

    public Task<StrategyResearchBacktestResult?> RunWindowAsync(
        long exchangeId,
        long symbolId,
        string timeframe,
        DateTime fromUtc,
        DateTime toUtc,
        string strategyCode,
        IReadOnlyDictionary<string, string> parameters,
        long riskProfileId,
        decimal initialBalance,
        StrategyResearchExecutionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        CallCount++;
        return Task.FromResult<StrategyResearchBacktestResult?>(null);
    }

    public void Reset() => CallCount = 0;
}

public sealed class TrackingStrategyEngine : IStrategyEngine
{
    public int CallCount { get; private set; }

    public Task<IReadOnlyList<StrategyEvaluationResult>> EvaluateAsync(
        IReadOnlyCollection<ITradingStrategy> strategies,
        StrategyContext context,
        CancellationToken cancellationToken = default)
    {
        CallCount++;
        return Task.FromResult<IReadOnlyList<StrategyEvaluationResult>>(Array.Empty<StrategyEvaluationResult>());
    }

    public void Reset() => CallCount = 0;
}

public sealed class TrackingBacktestRunner : IBacktestRunner
{
    private IBacktestRunner? _inner;
    public int CallCount { get; private set; }

    public void Attach(IBacktestRunner inner) => _inner = inner;

    public Task<ServiceResult<RunBacktestResponse>> RunAsync(
        RunBacktestRequest request,
        CancellationToken cancellationToken = default)
    {
        CallCount++;
        return _inner!.RunAsync(request, cancellationToken);
    }

    public void Reset() => CallCount = 0;
}

file static class Milestone231A1RejectionServiceCollectionExtensions
{
    public static void DecorateBacktestRunner(this IServiceCollection services, TrackingBacktestRunner tracker)
    {
        // Resolve production runner after other registrations, then wrap once.
        var descriptor = services.LastOrDefault(d => d.ServiceType == typeof(IBacktestRunner));
        if (descriptor is null)
        {
            return;
        }

        services.RemoveAll<IBacktestRunner>();
        services.AddScoped<IBacktestRunner>(sp =>
        {
            IBacktestRunner inner;
            if (descriptor.ImplementationType is not null)
            {
                inner = (IBacktestRunner)ActivatorUtilities.CreateInstance(sp, descriptor.ImplementationType);
            }
            else if (descriptor.ImplementationFactory is not null)
            {
                inner = (IBacktestRunner)descriptor.ImplementationFactory(sp);
            }
            else
            {
                inner = (IBacktestRunner)descriptor.ImplementationInstance!;
            }

            tracker.Attach(inner);
            return tracker;
        });
    }
}
