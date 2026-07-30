using MomoQuant.Application.Strategies;
using MomoQuant.Application.Strategies.Implementations;
using MomoQuant.Application.Strategies.MomoAdaptive;
using MomoQuant.Application.ValidationLab;
using MomoQuant.Domain.Constants;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.MarketData;
using MomoQuant.Domain.Strategies;
using MomoQuant.Domain.ValidationLab;

namespace MomoQuant.UnitTests.Strategies;

/// <summary>Milestone 23.1B1C — pre-audit bootstrap, canonical validation, and HTF integrity closure.</summary>
public sealed class Milestone231B1CFactoryTests
{
    private static readonly DateTime EvalStart = new(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Boundary = new(2026, 2, 3, 0, 0, 0, DateTimeKind.Utc);
    private const long SymbolId = 7;
    private const long ExchangeId = 42;

    [Fact]
    public void ValidateCanonical_RequiresBoundExecutionToken()
    {
        var ex = Assert.Throws<ArgumentException>(() => BuildCanonicalRequest(boundExecutionToken: null).ValidateCanonical());
        Assert.Contains("BoundExecutionToken", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateCanonical_RequiresPositiveBoundAttemptNumber()
    {
        var ex = Assert.Throws<ArgumentException>(() => BuildCanonicalRequest(boundAttemptNumber: 0).ValidateCanonical());
        Assert.Contains("BoundAttemptNumber", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LtfOnlyWarmupBootstrap_RejectsBoundAuditIdentity()
    {
        var request = new ValidationTrainingCandleScopeRequest
        {
            ValidationExperimentId = 2311,
            SymbolId = SymbolId,
            SymbolName = "BTCUSDT",
            Timeframe = "5m",
            TrainingEvaluationStartUtc = EvalStart,
            TrainingEvaluationEndExclusiveUtc = Boundary,
            ValidationBoundaryUtc = Boundary,
            RequiredWarmupCandleCount = 0,
            RequirementsVersion = StrategyExecutionRequirements.Version,
            LtfOnlyWarmupBootstrap = true,
            BoundScopeExecutionId = Guid.NewGuid()
        };
        var ex = Assert.Throws<ArgumentException>(() => request.Validate());
        Assert.Contains("LTF-only warmup bootstrap", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LtfOnlyWarmupBootstrap_CannotLoadHtf()
    {
        var reader = new TrackingHtfReader(BuildEval(), [Htf(1, SymbolId, ExchangeId, EvalStart, EvalStart.AddHours(1))]);
        var factory = new ValidationTrainingCandleScopeFactory(reader);

        await using var scope = await factory.CreateAsync(BuildLtfOnlyRequest());

        Assert.Equal(0, reader.HtfLoadCount);
        Assert.Empty(factory.LastBootstrapAccessEvidence);
        Assert.NotEqual(Guid.Empty, scope.ScopeExecutionId);
    }

    [Fact]
    public async Task CreateForExperimentAsync_IsQuarantined()
    {
        var factory = new ValidationTrainingCandleScopeFactory(
            new TrackingHtfReader(BuildEval(), Array.Empty<Candle>()));
        var experiment = new ValidationExperiment
        {
            Id = 2311,
            SymbolId = SymbolId,
            Symbol = "BTCUSDT",
            Timeframe = "5m",
            ExchangeId = ExchangeId,
            TrainingStartUtc = EvalStart,
            ValidationStartUtc = Boundary
        };

#pragma warning disable CS0618
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            factory.CreateForExperimentAsync(experiment, CancellationToken.None));
#pragma warning restore CS0618
        Assert.Contains("quarantined", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CanonicalBindingMismatch_FailsBeforeHtfCandleAccess()
    {
        var reader = new TrackingHtfReader(BuildEval(), [Htf(1, SymbolId, ExchangeId, EvalStart, EvalStart.AddHours(1))]);
        var factory = new ValidationTrainingCandleScopeFactory(reader);
        var experiment = BuildExperiment();
        var requirements = new StrategyExecutionRequirements
        {
            StrategyId = 99,
            StrategyCode = StrategyCodes.MomoAdaptiveMultiTimeframeTrendBreakout,
            StrategyVersion = MomoAdaptiveMultiTimeframeTrendBreakoutStrategy.Version,
            RequiredWarmupCandleCount = 0
        };
        var request = BuildCanonicalRequest();
        request = new ValidationTrainingCandleScopeRequest
        {
            ValidationExperimentId = request.ValidationExperimentId,
            SymbolId = request.SymbolId,
            SymbolName = request.SymbolName,
            Timeframe = request.Timeframe,
            TrainingEvaluationStartUtc = request.TrainingEvaluationStartUtc,
            TrainingEvaluationEndExclusiveUtc = request.TrainingEvaluationEndExclusiveUtc,
            ValidationBoundaryUtc = request.ValidationBoundaryUtc,
            RequiredWarmupCandleCount = request.RequiredWarmupCandleCount,
            RequirementsVersion = request.RequirementsVersion,
            StrategyId = request.StrategyId,
            StrategyCode = request.StrategyCode,
            StrategyVersion = request.StrategyVersion,
            ExchangeId = request.ExchangeId,
            BoundScopeExecutionId = request.BoundScopeExecutionId,
            BoundAuditExecutionId = request.BoundAuditExecutionId,
            BoundExecutionToken = request.BoundExecutionToken,
            BoundAttemptNumber = request.BoundAttemptNumber,
            CanonicalExperiment = experiment,
            CanonicalRequirements = requirements
        };

        await Assert.ThrowsAsync<ArgumentException>(() => factory.CreateAsync(request));
        Assert.Equal(0, reader.HtfLoadCount);
    }

    [Fact]
    public async Task HtfNonUtcTimestamp_FailsEntireLoad()
    {
        var candle = Htf(1, SymbolId, ExchangeId, EvalStart, EvalStart.AddHours(1));
        candle.OpenTimeUtc = DateTime.SpecifyKind(candle.OpenTimeUtc, DateTimeKind.Local);
        await AssertPoisonedLoadFailsWithCode([candle], ValidationCandlePartitionDenialCodes.HtfInvalidTimestamp);
    }

    [Fact]
    public async Task HtfInvalidCloseBeforeOpen_FailsEntireLoad()
    {
        var candle = Htf(1, SymbolId, ExchangeId, EvalStart, EvalStart);
        await AssertPoisonedLoadFailsWithCode([candle], ValidationCandlePartitionDenialCodes.HtfInvalidCandleRange);
    }

    [Fact]
    public async Task HtfOverlappingCandles_FailsEntireLoad()
    {
        var first = Htf(1, SymbolId, ExchangeId, EvalStart, EvalStart.AddHours(2));
        var overlap = Htf(2, SymbolId, ExchangeId, EvalStart.AddHours(1), EvalStart.AddHours(3));
        await AssertPoisonedLoadFailsWithCode([first, overlap], ValidationCandlePartitionDenialCodes.HtfOverlapping);
    }

    [Fact]
    public async Task HtfUnorderedOpen_FailsEntireLoad()
    {
        var later = Htf(1, SymbolId, ExchangeId, EvalStart.AddHours(2), EvalStart.AddHours(3));
        var earlier = Htf(2, SymbolId, ExchangeId, EvalStart, EvalStart.AddHours(1));
        await AssertPoisonedLoadFailsWithCode([later, earlier], ValidationCandlePartitionDenialCodes.HtfUnordered);
    }

    [Fact]
    public async Task SuccessfulHtfBootstrap_RecordsAuditLinkageAndStableAccessEventId()
    {
        var htf = Htf(1, SymbolId, ExchangeId, EvalStart, EvalStart.AddHours(1));
        var factory = new ValidationTrainingCandleScopeFactory(new TrackingHtfReader(BuildEval(), [htf]));
        var request = BuildCanonicalRequest();

        await factory.CreateAsync(request);
        var firstId = Assert.Single(factory.LastBootstrapAccessEvidence, r => !r.WasDenied).AccessEventId;

        await factory.CreateAsync(request);
        var bootstrap = Assert.Single(factory.LastBootstrapAccessEvidence, r => !r.WasDenied);

        Assert.Equal(firstId, bootstrap.AccessEventId);
        Assert.Equal(Guid.Parse("22222222-2222-2222-2222-222222222222"), bootstrap.AuditExecutionId);
        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), bootstrap.ScopeExecutionId);
        Assert.Equal(1, bootstrap.ScopeSequenceNumber);
    }

    private static ValidationTrainingCandleScopeRequest BuildLtfOnlyRequest() => new()
    {
        ValidationExperimentId = 2311,
        SymbolId = SymbolId,
        SymbolName = "BTCUSDT",
        Timeframe = "5m",
        TrainingEvaluationStartUtc = EvalStart,
        TrainingEvaluationEndExclusiveUtc = Boundary,
        ValidationBoundaryUtc = Boundary,
        RequiredWarmupCandleCount = 0,
        RequirementsVersion = StrategyExecutionRequirements.Version,
        LtfOnlyWarmupBootstrap = true
    };

    private static ValidationTrainingCandleScopeRequest BuildCanonicalRequest(
        string? boundExecutionToken = "token-b1c",
        int? boundAttemptNumber = 2) => new()
    {
        ValidationExperimentId = 2311,
        SymbolId = SymbolId,
        SymbolName = "BTCUSDT",
        Timeframe = "5m",
        TrainingEvaluationStartUtc = EvalStart,
        TrainingEvaluationEndExclusiveUtc = Boundary,
        ValidationBoundaryUtc = Boundary,
        RequiredWarmupCandleCount = 0,
        RequirementsVersion = StrategyExecutionRequirements.Version,
        StrategyId = 11,
        StrategyCode = StrategyCodes.MomoAdaptiveMultiTimeframeTrendBreakout,
        StrategyVersion = MomoAdaptiveMultiTimeframeTrendBreakoutStrategy.Version,
        ExchangeId = ExchangeId,
        BoundScopeExecutionId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
        BoundAuditExecutionId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
        BoundExecutionToken = boundExecutionToken,
        BoundAttemptNumber = boundAttemptNumber
    };

    private static ValidationExperiment BuildExperiment() => new()
    {
        Id = 2311,
        SymbolId = SymbolId,
        Symbol = "BTCUSDT",
        Timeframe = "5m",
        ExchangeId = ExchangeId,
        StrategyCode = StrategyCodes.MomoAdaptiveMultiTimeframeTrendBreakout,
        StrategyVersion = MomoAdaptiveMultiTimeframeTrendBreakoutStrategy.Version,
        TrainingStartUtc = EvalStart,
        ValidationStartUtc = Boundary
    };

    private static async Task AssertPoisonedLoadFailsWithCode(IReadOnlyList<Candle> htf, string expectedCode)
    {
        var reader = new TrackingHtfReader(BuildEval(), htf);
        var factory = new ValidationTrainingCandleScopeFactory(reader);

        var ex = await Assert.ThrowsAsync<ValidationCandlePartitionViolationException>(() =>
            factory.CreateAsync(BuildCanonicalRequest()));
        Assert.Equal(expectedCode, ex.DenialCode);

        var denied = Assert.Single(factory.LastBootstrapAccessEvidence, r => r.WasDenied);
        Assert.Equal(expectedCode, denied.DenialCode);
        Assert.Equal(1, reader.HtfLoadCount);
    }

    private static List<Candle> BuildEval() =>
        Enumerable.Range(0, 24)
            .Select(i =>
            {
                var open = EvalStart.AddMinutes(i * 5);
                return new Candle
                {
                    Id = i + 1,
                    SymbolId = SymbolId,
                    ExchangeId = ExchangeId,
                    Timeframe = Timeframe.M5,
                    OpenTimeUtc = open,
                    CloseTimeUtc = open.AddMinutes(5),
                    Open = 100m,
                    High = 101m,
                    Low = 99m,
                    Close = 100m,
                    Volume = 1m,
                    IsClosed = true,
                    CreatedAtUtc = open
                };
            })
            .ToList();

    private static Candle Htf(long id, long symbolId, long exchangeId, DateTime open, DateTime close) =>
        new()
        {
            Id = id,
            SymbolId = symbolId,
            ExchangeId = exchangeId,
            Timeframe = Timeframe.H1,
            OpenTimeUtc = open,
            CloseTimeUtc = close,
            Open = 100m,
            High = 101m,
            Low = 99m,
            Close = 100.5m,
            Volume = 1m,
            IsClosed = true,
            CreatedAtUtc = open
        };

    private sealed class TrackingHtfReader : IUnscopedCandleReader
    {
        private readonly IReadOnlyList<Candle> _eval;
        private readonly IReadOnlyList<Candle> _htf;

        public int HtfLoadCount { get; private set; }

        public TrackingHtfReader(IReadOnlyList<Candle> eval, IReadOnlyList<Candle> htf)
        {
            _eval = eval;
            _htf = htf;
        }

        public Task<IReadOnlyList<Candle>> GetCandlesChronologicalUnscopedAsync(
            long symbolId,
            Timeframe timeframe,
            DateTime? fromUtc,
            DateTime? toUtc,
            int warmUpCount = 0,
            CancellationToken cancellationToken = default)
        {
            if (timeframe == Timeframe.M5)
            {
                return Task.FromResult(_eval);
            }

            HtfLoadCount++;
            return Task.FromResult(_htf);
        }

        public Task<IReadOnlyList<Candle>> GetClosedCandlesBeforeUnscopedAsync(
            long symbolId,
            Timeframe timeframe,
            DateTime beforeOpenTimeUtc,
            int count,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Candle>>(Array.Empty<Candle>());
    }
}
