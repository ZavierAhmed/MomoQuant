using Moq;
using Microsoft.EntityFrameworkCore;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Backtesting;
using MomoQuant.Application.MarketData;
using MomoQuant.Application.Research;
using MomoQuant.Application.Risk;
using MomoQuant.Application.Strategies;
using MomoQuant.Application.StrategyLab;
using MomoQuant.Application.StrategyLab.Confidence;
using MomoQuant.Application.ValidationLab;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Exchanges;
using MomoQuant.Domain.Indicators;
using MomoQuant.Domain.MarketData;
using MomoQuant.Domain.Strategies;
using MomoQuant.Domain.StrategyLab;

namespace MomoQuant.UnitTests.StrategyLab;

public class StrategyLabExecutionContext230CTests
{
    [Fact]
    public void ForGeneralResearch_SetsPurposeAndAllowsCoverage()
    {
        var ctx = StrategyLabExecutionContext.ForGeneralResearch();
        Assert.Equal(ExecutionPurpose.GeneralResearch, ctx.ExecutionPurpose);
        Assert.True(ctx.AllowCoverageImport);
        Assert.False(string.IsNullOrWhiteSpace(ctx.CorrelationId));
        Assert.Null(ctx.CandleDataSource);
    }

    [Fact]
    public void ForValidationTraining_RequiresScopedSourceAndForbidsCoverage()
    {
        var scope = CreateScope();
        var source = new ValidationTrainingStrategyLabCandleDataSource(scope);
        var boundary = scope.ValidationBoundaryUtc;
        var ctx = StrategyLabExecutionContext.ForValidationTraining(
            10,
            20,
            3,
            boundary,
            source,
            "Test");

        Assert.Equal(ExecutionPurpose.ValidationTraining, ctx.ExecutionPurpose);
        Assert.False(ctx.AllowCoverageImport);
        Assert.Same(source, ctx.CandleDataSource);
        Assert.Equal(10, ctx.ValidationExperimentId);
        Assert.Equal(20, ctx.ValidationTrialId);
        Assert.Equal(3, ctx.ValidationTrialNumber);
        Assert.Equal(boundary, ctx.TrainingBoundaryUtc);
    }

    [Fact]
    public async Task Runner_ValidationTraining_MissingSource_FailsClosed()
    {
        var runner = CreateMinimalRunner(out var runRepo);
        runRepo.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateRun(1));

        var ctx = new StrategyLabExecutionContext
        {
            ExecutionPurpose = ExecutionPurpose.ValidationTraining,
            ValidationExperimentId = 9,
            ValidationTrialNumber = 1,
            TrainingBoundaryUtc = DateTime.UtcNow,
            CandleDataSource = null,
            AllowCoverageImport = false,
            CallerComponent = "Test",
            CorrelationId = "corr-1"
        };

        await Assert.ThrowsAsync<ValidationTrainingDataSourceMissingException>(() =>
            runner.ExecuteAsync(1, ctx));
    }

    [Fact]
    public async Task Runner_ValidationTraining_CoverageAllowed_FailsClosed()
    {
        var runner = CreateMinimalRunner(out var runRepo);
        runRepo.Setup(r => r.GetByIdAsync(2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateRun(2));

        var scope = CreateScope();
        var ctx = new StrategyLabExecutionContext
        {
            ExecutionPurpose = ExecutionPurpose.ValidationTraining,
            ValidationExperimentId = 9,
            ValidationTrialNumber = 1,
            TrainingBoundaryUtc = scope.ValidationBoundaryUtc,
            CandleDataSource = new ValidationTrainingStrategyLabCandleDataSource(scope),
            AllowCoverageImport = true,
            CallerComponent = "Test",
            CorrelationId = "corr-2"
        };

        await Assert.ThrowsAsync<ValidationTrainingCoverageImportForbiddenException>(() =>
            runner.ExecuteAsync(2, ctx));
    }

    [Fact]
    public async Task BacktestDataLoader_Unscoped_Fails_WhenValidationTrainingActive()
    {
        var accessor = new ResearchExecutionContextAccessor();
        var loader = new BacktestDataLoader(
            Mock.Of<ICandleRepository>(),
            Mock.Of<IIndicatorSnapshotRepository>(),
            Mock.Of<ISymbolRepository>(),
            accessor);

        using (accessor.Enter(new StrategyLabExecutionContext
        {
            ExecutionPurpose = ExecutionPurpose.ValidationTraining,
            ValidationExperimentId = 1,
            ValidationTrialNumber = 1,
            TrainingBoundaryUtc = DateTime.UtcNow,
            AllowCoverageImport = false,
            CallerComponent = "Test",
            CorrelationId = "c",
            CandleDataSource = Mock.Of<IStrategyLabCandleDataSource>()
        }))
        {
            await Assert.ThrowsAsync<ValidationTrainingUnscopedAccessException>(() =>
                loader.LoadSymbolTimeframeAsync(
                    1, 1, Timeframe.M5,
                    DateTime.UtcNow.AddDays(-1),
                    DateTime.UtcNow,
                    10));
        }
    }

    [Fact]
    public async Task BacktestDataLoader_Normal_OutsideValidationTraining()
    {
        var symbol = new Symbol
        {
            Id = 1,
            ExchangeId = 1,
            SymbolName = "BTCUSDT",
            BaseAsset = "BTC",
            QuoteAsset = "USDT",
            IsActive = true
        };
        var symbolRepo = new Mock<ISymbolRepository>();
        symbolRepo.Setup(s => s.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(symbol);

        var candles = new List<Candle>
        {
            new()
            {
                Id = 1,
                SymbolId = 1,
                Timeframe = Timeframe.M5,
                OpenTimeUtc = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                CloseTimeUtc = new DateTime(2024, 1, 1, 0, 5, 0, DateTimeKind.Utc),
                Open = 1, High = 1, Low = 1, Close = 1, Volume = 1
            }
        };
        var candleRepo = new Mock<ICandleRepository>();
        candleRepo.Setup(c => c.GetCandlesChronologicalAsync(
                1, Timeframe.M5,
                It.IsAny<DateTime>(), It.IsAny<DateTime>(),
                It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(candles);

        var indicators = new Mock<IIndicatorSnapshotRepository>();
        indicators.Setup(i => i.GetByCandleIdsAsync(
                It.IsAny<long>(), It.IsAny<Timeframe>(), It.IsAny<IReadOnlyList<long>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<long, IndicatorSnapshot>());

        var accessor = new ResearchExecutionContextAccessor();
        var loader = new BacktestDataLoader(candleRepo.Object, indicators.Object, symbolRepo.Object, accessor);

        using (accessor.Enter(StrategyLabExecutionContext.ForGeneralResearch()))
        {
            var dataset = await loader.LoadSymbolTimeframeAsync(
                1, 1, Timeframe.M5,
                candles[0].OpenTimeUtc,
                candles[0].OpenTimeUtc.AddMinutes(5),
                0);
            Assert.NotNull(dataset);
            Assert.Single(dataset!.Candles);
        }
    }

    [Fact]
    public async Task ValidationTrainingDataSource_LoadsOnlyFromScope_AndRecordsAccess()
    {
        var scope = CreateScope();
        var source = new ValidationTrainingStrategyLabCandleDataSource(scope, "UnitTest");
        var run = CreateRun(5);
        run.FromUtc = scope.SegmentStartUtc;
        run.ToUtc = scope.ValidationBoundaryUtc;

        var dataset = await source.LoadAsync(run, warmupCandles: 0);
        Assert.NotEmpty(dataset.Candles);
        Assert.All(dataset.Candles, c => Assert.True(c.OpenTimeUtc < scope.ValidationBoundaryUtc));
        Assert.Contains(scope.AccessLog, a => !a.WasDenied && a.CallerComponent == "UnitTest");
    }

    [Fact]
    public async Task RepositoryGuard_NormalOutsideValidation_AllowsScopedReads()
    {
        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<MomoQuant.Persistence.MomoQuantDbContext>()
            .UseInMemoryDatabase($"m230c-repo-{Guid.NewGuid():N}")
            .Options;
        await using var db = new MomoQuant.Persistence.MomoQuantDbContext(options);
        var open = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        db.Candles.Add(new Candle
        {
            Id = 1,
            ExchangeId = 1,
            SymbolId = 1,
            Timeframe = Timeframe.M5,
            OpenTimeUtc = open,
            CloseTimeUtc = open.AddMinutes(5),
            Open = 1, High = 1, Low = 1, Close = 1, Volume = 1,
            IsClosed = true,
            CreatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var accessor = new ResearchExecutionContextAccessor();
        var repo = new MomoQuant.Persistence.Repositories.TrainingBoundaryCandleRepository(
            new MomoQuant.Persistence.Repositories.CandleRepository(db),
            accessor);

        using (accessor.Enter(StrategyLabExecutionContext.ForGeneralResearch()))
        {
            var candles = await repo.GetCandlesChronologicalAsync(
                1, Timeframe.M5, open, open.AddMinutes(5), warmUpCount: 0);
            Assert.Single(candles);
        }
    }

    [Fact]
    public async Task RepositoryGuard_ValidationTrainingWithoutScope_FailsClosed()
    {
        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<MomoQuant.Persistence.MomoQuantDbContext>()
            .UseInMemoryDatabase($"m230c-repo-fail-{Guid.NewGuid():N}")
            .Options;
        await using var db = new MomoQuant.Persistence.MomoQuantDbContext(options);
        var accessor = new ResearchExecutionContextAccessor();
        var repo = new MomoQuant.Persistence.Repositories.TrainingBoundaryCandleRepository(
            new MomoQuant.Persistence.Repositories.CandleRepository(db),
            accessor);

        using (accessor.Enter(new StrategyLabExecutionContext
        {
            ExecutionPurpose = ExecutionPurpose.ValidationTraining,
            ValidationExperimentId = 7,
            ValidationTrialNumber = 1,
            TrainingBoundaryUtc = DateTime.UtcNow,
            AllowCoverageImport = false,
            CallerComponent = "Test",
            CorrelationId = "c",
            CandleDataSource = Mock.Of<IStrategyLabCandleDataSource>()
        }))
        {
            await Assert.ThrowsAsync<ValidationTrainingUnscopedAccessException>(() =>
                repo.GetCandlesChronologicalAsync(
                    1, Timeframe.M5,
                    DateTime.UtcNow.AddDays(-1),
                    DateTime.UtcNow,
                    warmUpCount: 0));
        }
    }

    private static ValidationTrainingCandleScope CreateScope()
    {
        var boundary = new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var start = boundary.AddDays(-2);
        var candles = new List<Candle>
        {
            new()
            {
                Id = 1,
                SymbolId = 1,
                Timeframe = Timeframe.H1,
                OpenTimeUtc = start,
                CloseTimeUtc = start.AddHours(1),
                Open = 1, High = 1, Low = 1, Close = 1, Volume = 1
            },
            new()
            {
                Id = 2,
                SymbolId = 1,
                Timeframe = Timeframe.H1,
                OpenTimeUtc = boundary.AddHours(-1),
                CloseTimeUtc = boundary,
                Open = 1, High = 1, Low = 1, Close = 1, Volume = 1
            }
        };
        return new ValidationTrainingCandleScope(42, start, boundary, candles);
    }

    private static StrategyLabRun CreateRun(long id) => new()
    {
        Id = id,
        Name = $"t-{id}",
        StrategyCode = "DONCHIAN_BREAKOUT",
        StrategyVersion = "1.0.0",
        ExchangeId = 1,
        SymbolId = 1,
        Symbol = "TESTUSDT",
        Timeframe = "1h",
        FromUtc = new DateTime(2024, 5, 30, 0, 0, 0, DateTimeKind.Utc),
        ToUtc = new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc),
        ExecutionMode = StrategyLabExecutionMode.RawStrategy,
        ParametersJson = "{}",
        InitialBalance = 10_000m,
        FeeSettingsJson = "{}",
        SlippageSettingsJson = "{}",
        Status = StrategyLabRunStatus.Created,
        CreatedAtUtc = DateTime.UtcNow
    };

    private static StrategyLabRunner CreateMinimalRunner(out Mock<IStrategyLabRunRepository> runRepo)
    {
        runRepo = new Mock<IStrategyLabRunRepository>();
        runRepo.Setup(r => r.UpdateAsync(It.IsAny<StrategyLabRun>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var strategyRepo = new Mock<IStrategyRepository>();
        strategyRepo.Setup(s => s.GetByCodeAsync(It.IsAny<StrategyCode>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Strategy
            {
                Id = 1,
                Code = StrategyCode.DonchianBreakout,
                Name = "Donchian",
                Version = "1.0.0",
                IsEnabled = true
            });

        var registry = new Mock<IStrategyRegistry>();
        registry.Setup(r => r.GetByCode(It.IsAny<StrategyCode>()))
            .Returns(Mock.Of<ITradingStrategy>());

        return new StrategyLabRunner(
            runRepo.Object,
            Mock.Of<IStrategyResearchCandidateRepository>(),
            Mock.Of<IBacktestDataLoader>(),
            strategyRepo.Object,
            registry.Object,
            Mock.Of<IStrategyDataRequirementService>(),
            Mock.Of<IHistoricalCandleCoverageService>(),
            Mock.Of<IRiskRuleRepository>(),
            Mock.Of<IRiskProfileRepository>(),
            new PositionSizingService(),
            Mock.Of<ICandidateConfidenceScorer>());
    }
}
