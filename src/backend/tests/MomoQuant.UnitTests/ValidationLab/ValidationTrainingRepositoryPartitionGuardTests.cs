using Microsoft.EntityFrameworkCore;
using MomoQuant.Application.Strategies;
using MomoQuant.Application.ValidationLab;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.MarketData;
using MomoQuant.Persistence;
using MomoQuant.Persistence.Repositories;

namespace MomoQuant.UnitTests.ValidationLab;

public sealed class ValidationTrainingRepositoryPartitionGuardTests
{
    private const long ExchangeId = 7;
    private const long SymbolId = 11;
    private static readonly DateTime EvaluationStart = new(2044, 1, 1, 1, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime EvaluationEnd = EvaluationStart.AddMinutes(45);
    private static readonly DateTime ValidationBoundary = EvaluationEnd.AddMinutes(15);

    [Fact]
    public async Task WrongSymbol_DeniedBeforeScopeDataOrInnerAccess()
    {
        await using var fixture = CreateFixture();
        using var ambient = ValidationTrainingCandleScopeAmbient.Enter(fixture.Scope);

        var calls = new Func<Task>[]
        {
            async () => _ = await fixture.Repository.GetCandlesAsync(SymbolId + 1, Timeframe.M15, EvaluationStart, EvaluationEnd, 10),
            async () => _ = await fixture.Repository.GetCandlesChronologicalAsync(SymbolId + 1, Timeframe.M15, EvaluationStart, EvaluationEnd),
            async () => _ = await fixture.Repository.GetLatestCandleAsync(SymbolId + 1, Timeframe.M15),
            async () => _ = await fixture.Repository.CountCandlesAsync(SymbolId + 1, Timeframe.M15),
            async () => _ = await fixture.Repository.GetExistingOpenTimesAsync(ExchangeId, SymbolId + 1, Timeframe.M15, [EvaluationStart]),
            async () => _ = await fixture.Repository.GetRecentCandlesAsync(SymbolId + 1, Timeframe.M15, EvaluationStart, 1),
            async () => _ = await fixture.Repository.GetOpenTimesInRangeAsync(ExchangeId, SymbolId + 1, Timeframe.M15, EvaluationStart, EvaluationEnd),
            async () => _ = await fixture.Repository.CountDuplicateKeysInRangeAsync(ExchangeId, SymbolId + 1, Timeframe.M15, EvaluationStart, EvaluationEnd)
        };

        foreach (var call in calls)
        {
            var ex = await Assert.ThrowsAsync<ValidationCandlePartitionViolationException>(call);
            Assert.Equal(ValidationCandlePartitionDenialCodes.SymbolMismatch, ex.DenialCode);
        }

        Assert.Equal(calls.Length, fixture.Scope.AccessLog.Count);
        Assert.All(fixture.Scope.AccessLog, record =>
        {
            Assert.True(record.WasDenied);
            Assert.Equal(SymbolId + 1, record.RequestSymbolId);
            Assert.Contains($"actual={SymbolId + 1}", record.DenialReason, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task WrongTimeframe_DeniedBeforeScopeDataOrInnerAccess()
    {
        await using var fixture = CreateFixture();
        using var ambient = ValidationTrainingCandleScopeAmbient.Enter(fixture.Scope);

        var calls = new Func<Task>[]
        {
            async () => _ = await fixture.Repository.GetCandlesAsync(SymbolId, Timeframe.H1, EvaluationStart, EvaluationEnd, 10),
            async () => _ = await fixture.Repository.GetCandlesChronologicalAsync(SymbolId, Timeframe.H1, EvaluationStart, EvaluationEnd),
            async () => _ = await fixture.Repository.GetLatestCandleAsync(SymbolId, Timeframe.H1),
            async () => _ = await fixture.Repository.CountCandlesAsync(SymbolId, Timeframe.H1),
            async () => _ = await fixture.Repository.GetExistingOpenTimesAsync(ExchangeId, SymbolId, Timeframe.H1, [EvaluationStart]),
            async () => _ = await fixture.Repository.GetRecentCandlesAsync(SymbolId, Timeframe.H1, EvaluationStart, 1),
            async () => _ = await fixture.Repository.GetOpenTimesInRangeAsync(ExchangeId, SymbolId, Timeframe.H1, EvaluationStart, EvaluationEnd),
            async () => _ = await fixture.Repository.CountDuplicateKeysInRangeAsync(ExchangeId, SymbolId, Timeframe.H1, EvaluationStart, EvaluationEnd)
        };

        foreach (var call in calls)
        {
            var ex = await Assert.ThrowsAsync<ValidationCandlePartitionViolationException>(call);
            Assert.Equal(ValidationCandlePartitionDenialCodes.TimeframeMismatch, ex.DenialCode);
        }

        Assert.Equal(calls.Length, fixture.Scope.AccessLog.Count);
        Assert.All(fixture.Scope.AccessLog, record => Assert.Equal("1h", record.RequestTimeframeApi));
    }

    [Fact]
    public async Task WrongExchange_DeniedBeforeScopeDataOrInnerAccess()
    {
        await using var fixture = CreateFixture();
        using var ambient = ValidationTrainingCandleScopeAmbient.Enter(fixture.Scope);

        var calls = new Func<Task>[]
        {
            async () => _ = await fixture.Repository.GetExistingOpenTimesAsync(ExchangeId + 1, SymbolId, Timeframe.M15, [EvaluationStart]),
            async () => _ = await fixture.Repository.GetOpenTimesInRangeAsync(ExchangeId + 1, SymbolId, Timeframe.M15, EvaluationStart, EvaluationEnd),
            async () => _ = await fixture.Repository.CountDuplicateKeysInRangeAsync(ExchangeId + 1, SymbolId, Timeframe.M15, EvaluationStart, EvaluationEnd)
        };

        foreach (var call in calls)
        {
            var ex = await Assert.ThrowsAsync<ValidationCandlePartitionViolationException>(call);
            Assert.Equal("EXCHANGE_MISMATCH", ex.DenialCode);
        }

        Assert.Equal(calls.Length, fixture.Scope.AccessLog.Count);
        Assert.All(fixture.Scope.AccessLog, record => Assert.Equal(ExchangeId + 1, record.RequestExchangeId));
    }

    [Fact]
    public async Task GetById_AmbientScopeNeverQueriesInnerRepository()
    {
        await using var fixture = CreateFixture(disposeInnerContext: true);
        using var ambient = ValidationTrainingCandleScopeAmbient.Enter(fixture.Scope);

        var found = await fixture.Repository.GetByIdAsync(3);
        var missing = await fixture.Repository.GetByIdAsync(999_999);

        Assert.Equal(3, found?.Id);
        Assert.Null(missing);
        Assert.Equal(2, fixture.Scope.AccessLog.Count);
        Assert.Equal([1, 0], fixture.Scope.AccessLog.Select(record => record.ReturnedCandleCount).ToArray());
    }

    [Fact]
    public async Task GetExistingOpenTimes_ReturnsOnlyImmutableMembers()
    {
        await using var fixture = CreateFixture();
        using var ambient = ValidationTrainingCandleScopeAmbient.Enter(fixture.Scope);
        var gap = EvaluationStart.AddMinutes(5);

        var existing = await fixture.Repository.GetExistingOpenTimesAsync(
            ExchangeId,
            SymbolId,
            Timeframe.M15,
            [
                EvaluationStart.AddMinutes(-15),
                EvaluationStart,
                EvaluationStart.AddMinutes(15).ToLocalTime(),
                gap,
                EvaluationEnd,
                ValidationBoundary
            ]);

        Assert.Equal(
            [EvaluationStart.AddMinutes(-15), EvaluationStart, EvaluationStart.AddMinutes(15)],
            existing.OrderBy(x => x).ToArray());
        var evidence = Assert.Single(fixture.Scope.AccessLog);
        Assert.Equal(3, evidence.ReturnedCandleCount);
        Assert.Equal("Combined", evidence.DatasetPartition);
    }

    [Fact]
    public async Task CountCandles_CombinedCountHasCombinedEvidence()
    {
        await using var fixture = CreateFixture();
        using var ambient = ValidationTrainingCandleScopeAmbient.Enter(fixture.Scope);

        var count = await fixture.Repository.CountCandlesAsync(SymbolId, Timeframe.M15);

        Assert.Equal(5, count);
        var evidence = Assert.Single(fixture.Scope.AccessLog);
        Assert.Equal("Combined", evidence.DatasetPartition);
        Assert.Equal(5, evidence.ReturnedCandleCount);
        Assert.Equal(ValidationTrainingCandleScope.ComputeContentFingerprint(fixture.AllCandles), evidence.CandleContentFingerprint);
    }

    [Fact]
    public async Task GetCandles_LimitZeroReturnsEmptyWithExactEvidence()
    {
        await using var fixture = CreateFixture();
        using var ambient = ValidationTrainingCandleScopeAmbient.Enter(fixture.Scope);

        var result = await fixture.Repository.GetCandlesAsync(SymbolId, Timeframe.M15, EvaluationStart, EvaluationEnd, 0);

        Assert.Empty(result);
        var evidence = Assert.Single(fixture.Scope.AccessLog);
        Assert.Equal(0, evidence.RequestedCandleCount);
        Assert.Equal(0, evidence.ReturnedCandleCount);
        Assert.Null(evidence.CandleContentFingerprint);
    }

    [Fact]
    public async Task LimitedLatestAndRecentReads_AuditExactlyReturnedCandles()
    {
        await using var fixture = CreateFixture();
        using var ambient = ValidationTrainingCandleScopeAmbient.Enter(fixture.Scope);

        var limited = await fixture.Repository.GetCandlesAsync(SymbolId, Timeframe.M15, EvaluationStart, EvaluationEnd, 2);
        var latest = await fixture.Repository.GetLatestCandleAsync(SymbolId, Timeframe.M15);
        var recent = await fixture.Repository.GetRecentCandlesAsync(SymbolId, Timeframe.M15, EvaluationStart.AddMinutes(30), 2);

        Assert.Equal([3L, 4L], limited.Select(c => c.Id).ToArray());
        Assert.Equal(5, latest?.Id);
        Assert.Equal([4L, 5L], recent.Select(c => c.Id).ToArray());

        var expected = new IReadOnlyList<Candle>[] { limited, [latest!], recent };
        Assert.Equal(3, fixture.Scope.AccessLog.Count);
        for (var i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i].Count, fixture.Scope.AccessLog[i].ReturnedCandleCount);
            Assert.Equal(ValidationTrainingCandleScope.ComputeContentFingerprint(expected[i]), fixture.Scope.AccessLog[i].CandleContentFingerprint);
            Assert.Equal("EvaluationPartial", fixture.Scope.AccessLog[i].DatasetPartition);
        }
    }

    [Fact]
    public async Task ChronologicalWarmupAndEvaluation_EmitSeparateExactEvidence()
    {
        await using var fixture = CreateFixture();
        using var ambient = ValidationTrainingCandleScopeAmbient.Enter(fixture.Scope);

        var result = await fixture.Repository.GetCandlesChronologicalAsync(
            SymbolId,
            Timeframe.M15,
            EvaluationStart,
            EvaluationEnd,
            warmUpCount: 2);

        Assert.Equal(fixture.AllCandles.Select(candle => candle.Id), result.Select(candle => candle.Id));
        Assert.Equal(2, fixture.Scope.AccessLog.Count);

        var evaluation = fixture.Scope.AccessLog[0];
        Assert.Equal("Evaluation", evaluation.DatasetPartition);
        Assert.Null(evaluation.RequestedCandleCount);
        Assert.Equal(3, evaluation.ReturnedCandleCount);
        Assert.Equal(
            ValidationTrainingCandleScope.ComputeContentFingerprint(fixture.AllCandles.Skip(2).ToArray()),
            evaluation.CandleContentFingerprint);

        var warmup = fixture.Scope.AccessLog[1];
        Assert.Equal("Warmup", warmup.DatasetPartition);
        Assert.Equal(2, warmup.RequestedCandleCount);
        Assert.Equal(2, warmup.ReturnedCandleCount);
        Assert.Equal(
            ValidationTrainingCandleScope.ComputeContentFingerprint(fixture.AllCandles.Take(2).ToArray()),
            warmup.CandleContentFingerprint);
    }

    [Fact]
    public async Task RecentRead_CannotSwitchIntoWarmup()
    {
        await using var fixture = CreateFixture();
        using var ambient = ValidationTrainingCandleScopeAmbient.Enter(fixture.Scope);

        var ex = await Assert.ThrowsAsync<ValidationCandlePartitionViolationException>(() =>
            fixture.Repository.GetRecentCandlesAsync(SymbolId, Timeframe.M15, EvaluationStart.AddMinutes(-1), 1));

        Assert.Equal(ValidationCandlePartitionDenialCodes.EvaluationRequestBeforeEvaluationStart, ex.DenialCode);
        Assert.True(Assert.Single(fixture.Scope.AccessLog).WasDenied);
    }

    [Fact]
    public void CompatibilityWarmupSubrange_ReturnsOnlyRequestedMembers()
    {
        using var fixture = CreateFixture();
        var from = EvaluationStart.AddMinutes(-15);

        var result = fixture.Scope.GetRange(from, EvaluationStart, nameof(CompatibilityWarmupSubrange_ReturnsOnlyRequestedMembers));

        Assert.Equal([2L], result.Select(c => c.Id).ToArray());
        var evidence = Assert.Single(fixture.Scope.AccessLog);
        Assert.Equal(from, evidence.RequestedStartUtc);
        Assert.Equal(EvaluationStart, evidence.RequestedEndUtc);
        Assert.Equal("Warmup", evidence.DatasetPartition);
        Assert.Equal(ValidationTrainingCandleScope.ComputeContentFingerprint(result), evidence.CandleContentFingerprint);
    }

    [Fact]
    public async Task ReversedRange_IsDenied()
    {
        await using var fixture = CreateFixture();
        using var ambient = ValidationTrainingCandleScopeAmbient.Enter(fixture.Scope);

        var ex = await Assert.ThrowsAsync<ValidationCandlePartitionViolationException>(() =>
            fixture.Repository.GetCandlesAsync(SymbolId, Timeframe.M15, EvaluationStart.AddMinutes(30), EvaluationStart.AddMinutes(15), 5));

        Assert.Equal(ValidationCandlePartitionDenialCodes.PartitionRangeInvalid, ex.DenialCode);
        Assert.True(Assert.Single(fixture.Scope.AccessLog).WasDenied);
    }

    [Fact]
    public void TypedEvaluationReversedRange_IsDenied()
    {
        using var fixture = CreateFixture();

        var ex = Assert.Throws<ValidationCandlePartitionViolationException>(() =>
            fixture.Scope.GetEvaluationRange(new ValidationEvaluationAccessRequest
            {
                FromUtc = EvaluationStart.AddMinutes(30),
                ToExclusiveUtc = EvaluationStart.AddMinutes(15),
                AllowPartial = true,
                Purpose = ValidationCandleAccessPurpose.EvaluationPartial,
                CallerComponent = nameof(TypedEvaluationReversedRange_IsDenied)
            }));

        Assert.Equal(ValidationCandlePartitionDenialCodes.PartitionRangeInvalid, ex.DenialCode);
        Assert.True(Assert.Single(fixture.Scope.AccessLog).WasDenied);
    }

    [Fact]
    public async Task ZeroLengthRange_ReturnsExplicitEmptyEvidence()
    {
        await using var fixture = CreateFixture();
        using var ambient = ValidationTrainingCandleScopeAmbient.Enter(fixture.Scope);
        var at = EvaluationStart.AddMinutes(15);

        var result = await fixture.Repository.GetCandlesAsync(SymbolId, Timeframe.M15, at, at, 4);

        Assert.Empty(result);
        var evidence = Assert.Single(fixture.Scope.AccessLog);
        Assert.Equal(at, evidence.RequestedStartUtc);
        Assert.Equal(at, evidence.RequestedEndUtc);
        Assert.Equal(0, evidence.ReturnedCandleCount);
        Assert.Null(evidence.CandleContentFingerprint);
    }

    [Fact]
    public async Task PostSegmentRange_IsDeniedNotClipped()
    {
        await using var fixture = CreateFixture();
        using var ambient = ValidationTrainingCandleScopeAmbient.Enter(fixture.Scope);
        var after = EvaluationEnd.AddMinutes(1);

        var ex = await Assert.ThrowsAsync<ValidationCandlePartitionViolationException>(() =>
            fixture.Repository.GetCandlesAsync(SymbolId, Timeframe.M15, after, after, 1));

        Assert.Equal(ValidationCandlePartitionDenialCodes.EvaluationRequestAfterEvaluationEnd, ex.DenialCode);
        Assert.True(Assert.Single(fixture.Scope.AccessLog).WasDenied);
    }

    [Fact]
    public async Task ValidationTrainingWrites_DenyBeforeInnerRepository()
    {
        await using var fixture = CreateFixture(disposeInnerContext: true);
        using var ambient = ValidationTrainingCandleScopeAmbient.Enter(fixture.Scope);

        var add = await Assert.ThrowsAsync<ValidationCandlePartitionViolationException>(() =>
            fixture.Repository.AddRangeAsync([CreateCandle(100, EvaluationStart, SymbolId, Timeframe.M15)]));
        var save = await Assert.ThrowsAsync<ValidationCandlePartitionViolationException>(() =>
            fixture.Repository.SaveChangesAsync());

        Assert.Equal("VALIDATION_TRAINING_WRITE_FORBIDDEN", add.DenialCode);
        Assert.Equal("VALIDATION_TRAINING_WRITE_FORBIDDEN", save.DenialCode);
        Assert.Equal(2, fixture.Scope.AccessLog.Count);
        Assert.All(fixture.Scope.AccessLog, record => Assert.True(record.WasDenied));
    }

    [Fact]
    public async Task CrossPartitionDenial_IsRecorded()
    {
        await using var fixture = CreateFixture();
        using var ambient = ValidationTrainingCandleScopeAmbient.Enter(fixture.Scope);

        var ex = await Assert.ThrowsAsync<ValidationCandlePartitionViolationException>(() =>
            fixture.Repository.GetCandlesChronologicalAsync(
                SymbolId,
                Timeframe.M15,
                EvaluationStart.AddMinutes(-15),
                EvaluationStart.AddMinutes(15)));

        Assert.Equal(ValidationCandlePartitionDenialCodes.CrossPartitionCompatibilityReadForbidden, ex.DenialCode);
        var evidence = Assert.Single(fixture.Scope.AccessLog);
        Assert.True(evidence.WasDenied);
        Assert.Equal(nameof(TrainingBoundaryCandleRepository.GetCandlesChronologicalAsync), evidence.CallerComponent.Split(':')[0]);
    }

    private static Fixture CreateFixture(bool disposeInnerContext = false)
    {
        var options = new DbContextOptionsBuilder<MomoQuantDbContext>()
            .UseInMemoryDatabase($"b1c4-{Guid.NewGuid():N}")
            .Options;
        var db = new MomoQuantDbContext(options);
        var repository = new TrainingBoundaryCandleRepository(new CandleRepository(db));
        var all = new List<Candle>
        {
            CreateCandle(1, EvaluationStart.AddMinutes(-30), SymbolId, Timeframe.M15),
            CreateCandle(2, EvaluationStart.AddMinutes(-15), SymbolId, Timeframe.M15),
            CreateCandle(3, EvaluationStart, SymbolId, Timeframe.M15),
            CreateCandle(4, EvaluationStart.AddMinutes(15), SymbolId, Timeframe.M15),
            CreateCandle(5, EvaluationStart.AddMinutes(30), SymbolId, Timeframe.M15)
        };
        var warmup = all.Take(2).ToList();
        var evaluation = all.Skip(2).ToList();
        var partition = ValidationTrainingCandleScope.BuildPartition(
            validationExperimentId: 230104,
            symbolId: SymbolId,
            symbolName: "B1C4USDT",
            timeframe: "15m",
            requiredWarmup: warmup.Count,
            availableWarmup: warmup.Count,
            evaluationCount: evaluation.Count,
            status: ValidationWarmupStatus.Complete,
            evalStart: EvaluationStart,
            evalEndExclusive: EvaluationEnd,
            boundary: ValidationBoundary,
            requirementsVersion: StrategyExecutionRequirements.Version,
            warmup: warmup,
            evaluation: evaluation,
            combined: all,
            exchangeId: ExchangeId);
        var scope = new ValidationTrainingCandleScope(partition, warmup, evaluation, exchangeId: ExchangeId);

        if (disposeInnerContext)
        {
            db.Dispose();
        }

        return new Fixture(repository, scope, db, all, disposeInnerContext);
    }

    private static Candle CreateCandle(long id, DateTime open, long symbolId, Timeframe timeframe) => new()
    {
        Id = id,
        ExchangeId = ExchangeId,
        SymbolId = symbolId,
        Timeframe = timeframe,
        OpenTimeUtc = open,
        CloseTimeUtc = open.AddMinutes(15),
        Open = 100 + id,
        High = 101 + id,
        Low = 99 + id,
        Close = 100.5m + id,
        Volume = 10 + id,
        IsClosed = true,
        CreatedAtUtc = DateTime.UtcNow
    };

    private sealed class Fixture : IAsyncDisposable, IDisposable
    {
        private readonly bool _contextAlreadyDisposed;

        public Fixture(
            TrainingBoundaryCandleRepository repository,
            ValidationTrainingCandleScope scope,
            MomoQuantDbContext db,
            IReadOnlyList<Candle> allCandles,
            bool contextAlreadyDisposed)
        {
            Repository = repository;
            Scope = scope;
            Db = db;
            AllCandles = allCandles;
            _contextAlreadyDisposed = contextAlreadyDisposed;
        }

        public TrainingBoundaryCandleRepository Repository { get; }
        public ValidationTrainingCandleScope Scope { get; }
        public MomoQuantDbContext Db { get; }
        public IReadOnlyList<Candle> AllCandles { get; }

        public void Dispose()
        {
            Scope.DisposeAsync().AsTask().GetAwaiter().GetResult();
            if (!_contextAlreadyDisposed)
            {
                Db.Dispose();
            }
        }

        public async ValueTask DisposeAsync()
        {
            await Scope.DisposeAsync();
            if (!_contextAlreadyDisposed)
            {
                await Db.DisposeAsync();
            }
        }
    }
}
