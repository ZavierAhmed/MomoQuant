using Moq;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Common;
using MomoQuant.Application.Strategies;
using MomoQuant.Application.Strategies.Dtos;
using MomoQuant.Application.ValidationLab;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.MarketData;
using MomoQuant.Domain.Strategies;
using MomoQuant.Domain.StrategyLab;

namespace MomoQuant.UnitTests.ValidationLab;

/// <summary>WP10 — Warm-up boundary unit matrix.</summary>
public sealed class ValidationWarmupBoundaryMatrixTests
{
    private static readonly DateTime EvalStart = new(2024, 3, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Boundary = new(2024, 3, 10, 0, 0, 0, DateTimeKind.Utc);
    private const long SymbolId = 11;

    [Fact]
    public async Task RequiredZero_WarmupStatusNotRequired_DoesNotThrow()
    {
        var eval = BuildEvalCandles(count: 20);
        var reader = new FakeUnscopedReader(warmup: [], evaluation: eval);
        var factory = new ValidationTrainingCandleScopeFactory(reader);
        var request = BaseRequest(requiredWarmup: 0);

        var scope = await factory.CreateAsync(request);
        Assert.Equal(ValidationWarmupStatus.NotRequired, scope.Partition.WarmupStatus);
        Assert.Equal(0, scope.Partition.RequiredWarmupCandleCount);
        Assert.Equal(0, scope.Partition.AvailableWarmupCandleCount);
        Assert.Equal(20, scope.Partition.EvaluationCandleCount);
    }

    [Fact]
    public async Task ExactAvailable_WarmupStatusComplete()
    {
        var warmup = BuildWarmupCandles(count: 100);
        var eval = BuildEvalCandles(count: 30);
        var reader = new FakeUnscopedReader(warmup, eval);
        var factory = new ValidationTrainingCandleScopeFactory(reader);
        var scope = await factory.CreateAsync(BaseRequest(requiredWarmup: 100));

        Assert.Equal(ValidationWarmupStatus.Complete, scope.Partition.WarmupStatus);
        Assert.Equal(100, scope.Partition.AvailableWarmupCandleCount);
        Assert.Equal(100, scope.Partition.RequiredWarmupCandleCount);
    }

    [Fact]
    public async Task OneFewer_ThrowsInsufficientWarmup()
    {
        var warmup = BuildWarmupCandles(count: 99);
        var eval = BuildEvalCandles(count: 10);
        var reader = new FakeUnscopedReader(warmup, eval);
        var factory = new ValidationTrainingCandleScopeFactory(reader);

        var ex = await Assert.ThrowsAsync<ValidationTrainingInsufficientWarmupException>(() =>
            factory.CreateAsync(BaseRequest(requiredWarmup: 100)));

        Assert.Equal(100, ex.RequiredWarmupCandleCount);
        Assert.Equal(99, ex.AvailableWarmupCandleCount);
        Assert.Equal(ValidationTrainingFailureCodes.InsufficientWarmup, ex.ErrorCode);
        Assert.Equal(ValidationWarmupStatus.Insufficient, ex.WarmupStatus);
    }

    [Fact]
    public async Task BoundaryCandle_ExcludedFromEvaluation()
    {
        var warmup = BuildWarmupCandles(count: 5);
        var eval = BuildEvalCandles(count: 10);
        // Inject a candle exactly at the boundary — must never enter the scope.
        eval = eval.Concat([CandleAt(Boundary, 999m, SymbolId)]).ToList();
        var reader = new FakeUnscopedReader(warmup, eval);
        var factory = new ValidationTrainingCandleScopeFactory(reader);
        var scope = await factory.CreateAsync(BaseRequest(requiredWarmup: 5));

        Assert.All(
            scope.GetEvaluationRange(
                EvalStart,
                Boundary,
                ValidationCandleAccessContext.Create("Matrix", ValidationCandleAccessPurpose.EvaluationRange)),
            c => Assert.True(c.OpenTimeUtc < Boundary));

        Assert.Throws<ValidationDataLeakageException>(() =>
            scope.GetByOpenTimeUtc(Boundary, "MatrixBoundary"));
        Assert.DoesNotContain(
            scope.GetEvaluationRange(
                EvalStart,
                Boundary,
                ValidationCandleAccessContext.Create("Matrix", ValidationCandleAccessPurpose.EvaluationRange)),
            c => c.OpenTimeUtc == Boundary);
    }

    [Fact]
    public async Task EvalEndExclusive_ExcludesEndOpen()
    {
        var endExclusive = EvalStart.AddHours(10);
        var warmup = BuildWarmupCandles(count: 3);
        var eval = Enumerable.Range(0, 12)
            .Select(i => CandleAt(EvalStart.AddHours(i), 100m + i, SymbolId))
            .ToList();
        var reader = new FakeUnscopedReader(warmup, eval);
        var factory = new ValidationTrainingCandleScopeFactory(reader);
        var request = new ValidationTrainingCandleScopeRequest
        {
            ValidationExperimentId = 1,
            SymbolId = SymbolId,
            SymbolName = "MATRIXUSDT",
            Timeframe = "1h",
            TrainingEvaluationStartUtc = EvalStart,
            TrainingEvaluationEndExclusiveUtc = endExclusive,
            ValidationBoundaryUtc = Boundary,
            RequiredWarmupCandleCount = 3,
            RequirementsVersion = StrategyExecutionRequirements.Version
        };

        var scope = await factory.CreateAsync(request);
        Assert.Equal(10, scope.Partition.EvaluationCandleCount);

        var range = scope.GetEvaluationRange(
            EvalStart,
            endExclusive,
            ValidationCandleAccessContext.Create("Matrix", ValidationCandleAccessPurpose.EvaluationRange));
        Assert.Equal(10, range.Count);
        Assert.All(range, c => Assert.True(c.OpenTimeUtc < endExclusive));
        Assert.DoesNotContain(range, c => c.OpenTimeUtc == endExclusive);
    }

    [Fact]
    public async Task WrongSymbol_EvaluationEmptyOrSymbolMismatchOnDataset()
    {
        var warmup = BuildWarmupCandles(count: 5, symbolId: 99);
        var eval = BuildEvalCandles(count: 8, symbolId: 99);
        var reader = new FakeUnscopedReader(warmup, eval);
        var factory = new ValidationTrainingCandleScopeFactory(reader);
        // Request asks for SymbolId=11 but reader returns 99 — factory filters by request.SymbolId.
        var scope = await factory.CreateAsync(BaseRequest(requiredWarmup: 0));
        Assert.Equal(0, scope.Partition.EvaluationCandleCount);

        // Warmup required with wrong-symbol store → insufficient.
        var ex = await Assert.ThrowsAsync<ValidationTrainingInsufficientWarmupException>(() =>
            factory.CreateAsync(BaseRequest(requiredWarmup: 5)));
        Assert.Equal(0, ex.AvailableWarmupCandleCount);

        // Dataset symbol mismatch when partition has a symbol and run differs.
        var goodWarmup = BuildWarmupCandles(5);
        var goodEval = BuildEvalCandles(5);
        var goodScope = new ValidationTrainingCandleScope(
            ValidationTrainingCandleScope.BuildPartition(
                1, SymbolId, "MATRIXUSDT", "1h", 5, 5, 5,
                ValidationWarmupStatus.Complete, EvalStart, Boundary, Boundary,
                StrategyExecutionRequirements.Version, goodWarmup, goodEval,
                goodWarmup.Concat(goodEval).ToList()),
            goodWarmup,
            goodEval);

        // Use typed materialization request instead of legacy StrategyLabRun overload
        var materializationRequest = new ValidationDatasetMaterializationRequest
        {
            SymbolId = 999, // Wrong symbol
            SymbolName = "OTHER",
            Timeframe = "1h",
            EvaluationFromUtc = EvalStart,
            EvaluationToExclusiveUtc = EvalStart.AddHours(5),
            WarmupCandleCount = 5,
            CallerComponent = "Matrix"
        };

        Assert.Throws<ValidationCandlePartitionViolationException>(() =>
            goodScope.CreateStrategyLabDataset(materializationRequest));
    }

    [Fact]
    public async Task RequirementsResolver_MatchesStrategyDataRequirementWarmup()
    {
        var strategy = new Strategy
        {
            Id = 5,
            Code = StrategyCode.PriceStructureBreakoutRetest,
            Name = "PSBR",
            Version = "1.0.0",
            IsEnabled = true
        };
        var strategyRepo = new Mock<IStrategyRepository>();
        strategyRepo.Setup(s => s.GetByIdAsync(5, It.IsAny<CancellationToken>())).ReturnsAsync(strategy);

        var requirementService = new Mock<IStrategyDataRequirementService>();
        requirementService.Setup(r => r.GetByStrategyIdAsync(5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceResult<StrategyDataRequirementDto>.Ok(new StrategyDataRequirementDto
            {
                StrategyId = 5,
                StrategyCode = "PRICE_STRUCTURE_BREAKOUT_RETEST",
                StrategyName = "PSBR",
                PreferredExecutionTimeframe = "15m",
                AllowedExecutionTimeframes = ["15m"],
                RequiredDataTimeframes = [],
                OptionalDataTimeframes = [],
                AnchorTimeframes = [],
                HigherTimeframeFilters = [],
                WarmupCandles = 100,
                MinBenchmarkDays = 3,
                RecommendedBenchmarkDays = 10,
                RequiresIndicators = false,
                RequiredIndicators = [],
                RequiredIndicatorTimeframes = [],
                PreferredTimeframes = ["15m"],
                Warnings = []
            }));

        var resolver = new StrategyExecutionRequirementsResolver(requirementService.Object, strategyRepo.Object);
        var result = await resolver.ResolveByStrategyIdAsync(5);
        Assert.True(result.Succeeded);
        Assert.Equal(100, result.Data!.RequiredWarmupCandleCount);
        Assert.Equal(StrategyExecutionRequirements.Version, result.Data.RequirementsVersion);
    }

    private static ValidationTrainingCandleScopeRequest BaseRequest(int requiredWarmup) => new()
    {
        ValidationExperimentId = 1,
        SymbolId = SymbolId,
        SymbolName = "MATRIXUSDT",
        Timeframe = "1h",
        TrainingEvaluationStartUtc = EvalStart,
        TrainingEvaluationEndExclusiveUtc = Boundary,
        ValidationBoundaryUtc = Boundary,
        RequiredWarmupCandleCount = requiredWarmup,
        RequirementsVersion = StrategyExecutionRequirements.Version
    };

    private static List<Candle> BuildWarmupCandles(int count, long symbolId = SymbolId) =>
        Enumerable.Range(0, count)
            .Select(i => CandleAt(EvalStart.AddHours(-(count - i)), 90m + i, symbolId))
            .ToList();

    private static List<Candle> BuildEvalCandles(int count, long symbolId = SymbolId) =>
        Enumerable.Range(0, count)
            .Select(i => CandleAt(EvalStart.AddHours(i), 100m + i, symbolId))
            .Where(c => c.OpenTimeUtc < Boundary)
            .ToList();

    private static Candle CandleAt(DateTime open, decimal px, long symbolId) => new()
    {
        ExchangeId = 1,
        SymbolId = symbolId,
        Timeframe = Timeframe.H1,
        OpenTimeUtc = open,
        CloseTimeUtc = open.AddHours(1),
        Open = px,
        High = px + 1,
        Low = px - 1,
        Close = px,
        Volume = 1m,
        IsClosed = true,
        CreatedAtUtc = DateTime.UtcNow
    };

    private sealed class FakeUnscopedReader : IUnscopedCandleReader
    {
        private readonly IReadOnlyList<Candle> _warmup;
        private readonly IReadOnlyList<Candle> _evaluation;

        public FakeUnscopedReader(IReadOnlyList<Candle> warmup, IReadOnlyList<Candle> evaluation)
        {
            _warmup = warmup;
            _evaluation = evaluation;
        }

        public Task<IReadOnlyList<Candle>> GetCandlesChronologicalUnscopedAsync(
            long symbolId,
            Timeframe timeframe,
            DateTime? fromUtc,
            DateTime? toUtc,
            int warmUpCount = 0,
            CancellationToken cancellationToken = default)
        {
            var slice = _evaluation
                .Where(c =>
                    (!fromUtc.HasValue || c.OpenTimeUtc >= fromUtc.Value) &&
                    (!toUtc.HasValue || c.OpenTimeUtc < toUtc.Value))
                .OrderBy(c => c.OpenTimeUtc)
                .ToList();
            return Task.FromResult<IReadOnlyList<Candle>>(slice);
        }

        public Task<IReadOnlyList<Candle>> GetClosedCandlesBeforeUnscopedAsync(
            long symbolId,
            Timeframe timeframe,
            DateTime beforeOpenTimeUtc,
            int count,
            CancellationToken cancellationToken = default)
        {
            var slice = _warmup
                .Where(c => c.IsClosed && c.OpenTimeUtc < beforeOpenTimeUtc)
                .OrderByDescending(c => c.OpenTimeUtc)
                .Take(count)
                .OrderBy(c => c.OpenTimeUtc)
                .ToList();
            return Task.FromResult<IReadOnlyList<Candle>>(slice);
        }
    }
}
