using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Strategies;
using MomoQuant.Application.Strategies.Implementations;
using MomoQuant.Application.Strategies.MomoAdaptive;
using MomoQuant.Application.ValidationLab;
using MomoQuant.Domain.Constants;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.MarketData;
using MomoQuant.Domain.ValidationLab;

namespace MomoQuant.UnitTests.Strategies;

/// <summary>Milestone 23.1B1C2 — authoritative Trial binding through CreateCanonicalAsync.</summary>
public sealed class Milestone231B1C2TrialBindingTests
{
    private static readonly DateTime EvalStart = new(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Boundary = new(2026, 2, 3, 0, 0, 0, DateTimeKind.Utc);
    private const long SymbolId = 7;
    private const long ExchangeId = 42;
    private const long StrategyId = 11;

    [Fact]
    public void MissingTrial_ValidateAuthoritativeBindings_ThrowsArgumentNullException()
    {
        var request = new ValidationCanonicalTrainingCandleScopeRequest
        {
            Experiment = BuildExperiment(),
            Requirements = BuildAdaptiveRequirements(),
            AuditExecution = BuildAuditExecution(),
            Trial = null!,
            TrainingEvaluationEndExclusiveUtc = Boundary
        };

        Assert.Throws<ArgumentNullException>(() => request.ValidateAuthoritativeBindings());
    }

    [Fact]
    public async Task MissingTrial_CreateCanonicalAsync_ZeroReaderCalls()
    {
        var reader = NewReaderWithHtf();
        var factory = CreateCanonicalFactory(reader);
        var request = new ValidationCanonicalTrainingCandleScopeRequest
        {
            Experiment = BuildExperiment(),
            Requirements = BuildAdaptiveRequirements(),
            AuditExecution = BuildAuditExecution(),
            Trial = null!,
            TrainingEvaluationEndExclusiveUtc = Boundary
        };

        await Assert.ThrowsAsync<ArgumentNullException>(() => factory.CreateCanonicalAsync(request));
        Assert.Equal(0, reader.CallCount);
    }

    [Fact]
    public async Task ZeroTrialId_ZeroReaderCalls()
    {
        var reader = NewReaderWithHtf();
        var factory = CreateCanonicalFactory(reader);
        var trial = BuildTrial();
        trial.Id = 0;

        await Assert.ThrowsAsync<ArgumentException>(() =>
            factory.CreateCanonicalAsync(BuildCanonicalScopeRequest(trial: trial)));
        Assert.Equal(0, reader.CallCount);
    }

    [Fact]
    public async Task WrongTrialExperiment_ZeroReaderCalls()
    {
        var reader = NewReaderWithHtf();
        var factory = CreateCanonicalFactory(reader);
        var trial = BuildTrial();
        trial.ValidationExperimentId = 9999;

        await Assert.ThrowsAsync<ArgumentException>(() =>
            factory.CreateCanonicalAsync(BuildCanonicalScopeRequest(trial: trial)));
        Assert.Equal(0, reader.CallCount);
    }

    [Fact]
    public async Task InvalidTrialNumber_ZeroReaderCalls()
    {
        var reader = NewReaderWithHtf();
        var factory = CreateCanonicalFactory(reader);
        var trial = BuildTrial();
        trial.TrialNumber = 0;

        await Assert.ThrowsAsync<ArgumentException>(() =>
            factory.CreateCanonicalAsync(BuildCanonicalScopeRequest(trial: trial)));
        Assert.Equal(0, reader.CallCount);
    }

    [Fact]
    public async Task WrongAuditTrialId_ZeroReaderCalls()
    {
        var reader = NewReaderWithHtf();
        var factory = CreateCanonicalFactory(reader);
        var audit = BuildAuditExecution();
        audit.ValidationTrialId = 999;

        await Assert.ThrowsAsync<ArgumentException>(() =>
            factory.CreateCanonicalAsync(BuildCanonicalScopeRequest(auditExecution: audit)));
        Assert.Equal(0, reader.CallCount);
    }

    [Fact]
    public async Task WrongAuditTrialNumber_ZeroReaderCalls()
    {
        var reader = NewReaderWithHtf();
        var factory = CreateCanonicalFactory(reader);
        var audit = BuildAuditExecution();
        audit.TrialNumber = 99;
        var trial = BuildTrial(audit);
        trial.TrialNumber = 1;

        await Assert.ThrowsAsync<ArgumentException>(() =>
            factory.CreateCanonicalAsync(BuildCanonicalScopeRequest(auditExecution: audit, trial: trial)));
        Assert.Equal(0, reader.CallCount);
    }

    [Fact]
    public async Task WrongAuthoritativeAuditExecutionId_ZeroReaderCalls()
    {
        var reader = NewReaderWithHtf();
        var factory = CreateCanonicalFactory(reader);
        var trial = BuildTrial();
        trial.AuthoritativeAuditExecutionId = Guid.NewGuid();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            factory.CreateCanonicalAsync(BuildCanonicalScopeRequest(trial: trial)));
        Assert.Equal(0, reader.CallCount);
    }

    [Fact]
    public async Task WrongAuditAttemptNumberWhenAuthoritative_ZeroReaderCalls()
    {
        var reader = NewReaderWithHtf();
        var factory = CreateCanonicalFactory(reader);
        var audit = BuildAuditExecution();
        var trial = BuildTrial(audit);
        trial.AuditAttemptNumber = 3;

        await Assert.ThrowsAsync<ArgumentException>(() =>
            factory.CreateCanonicalAsync(BuildCanonicalScopeRequest(auditExecution: audit, trial: trial)));
        Assert.Equal(0, reader.CallCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task NonPositiveAuditAttemptNumberWhenAuthoritative_ZeroReaderCalls(int attemptNumber)
    {
        var reader = NewReaderWithHtf();
        var factory = CreateCanonicalFactory(reader);
        var audit = BuildAuditExecution();
        var trial = BuildTrial(audit);
        trial.AuditAttemptNumber = attemptNumber;

        await Assert.ThrowsAsync<ArgumentException>(() =>
            factory.CreateCanonicalAsync(BuildCanonicalScopeRequest(auditExecution: audit, trial: trial)));
        Assert.Equal(0, reader.CallCount);
    }

    [Fact]
    public async Task NonTrialExecutionType_ZeroReaderCalls()
    {
        var reader = NewReaderWithHtf();
        var factory = CreateCanonicalFactory(reader);
        var audit = BuildAuditExecution();
        audit.ExecutionType = ValidationAuditExecutionType.ScopeFinalization;

        await Assert.ThrowsAsync<ArgumentException>(() =>
            factory.CreateCanonicalAsync(BuildCanonicalScopeRequest(auditExecution: audit)));
        Assert.Equal(0, reader.CallCount);
    }

    [Fact]
    public async Task SupersededAudit_ZeroReaderCalls()
    {
        var reader = NewReaderWithHtf();
        var factory = CreateCanonicalFactory(reader);
        var audit = BuildAuditExecution();
        audit.Status = ValidationAuditExecutionStatus.Superseded;

        await Assert.ThrowsAsync<ArgumentException>(() =>
            factory.CreateCanonicalAsync(BuildCanonicalScopeRequest(auditExecution: audit)));
        Assert.Equal(0, reader.CallCount);
    }

    [Fact]
    public async Task FailedAudit_ZeroReaderCalls()
    {
        var reader = NewReaderWithHtf();
        var factory = CreateCanonicalFactory(reader);
        var audit = BuildAuditExecution();
        audit.Status = ValidationAuditExecutionStatus.Failed;

        await Assert.ThrowsAsync<ArgumentException>(() =>
            factory.CreateCanonicalAsync(BuildCanonicalScopeRequest(auditExecution: audit)));
        Assert.Equal(0, reader.CallCount);
    }

    private static TrackingHtfReader NewReaderWithHtf() =>
        new(BuildEval(), [Htf(1, SymbolId, ExchangeId, EvalStart, EvalStart.AddHours(1))]);

    private static ValidationTrainingCandleScopeFactory CreateCanonicalFactory(IUnscopedCandleReader reader) =>
        new(reader, new ValidationCandleAccessRecorder(new NoOpAuditRepository()));

    private static StrategyExecutionRequirements BuildAdaptiveRequirements(long strategyId = StrategyId, int warmup = 0) =>
        new()
        {
            StrategyId = strategyId,
            StrategyCode = StrategyCodes.MomoAdaptiveMultiTimeframeTrendBreakout,
            StrategyVersion = MomoAdaptiveMultiTimeframeTrendBreakoutStrategy.Version,
            RequiredWarmupCandleCount = warmup,
            RequiresHigherTimeframePartition = true,
            RequiredHigherTimeframeApi = "1h",
            HigherTimeframeMappingContractVersion = StrategyHigherTimeframeSupport.AdaptiveHtfMappingContractVersion
        };

    private static ValidationAuditExecution BuildAuditExecution() => new()
    {
        AuditExecutionId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
        ValidationExperimentId = 2311,
        ValidationTrialId = 1,
        TrialNumber = 1,
        ScopeExecutionId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
        ExecutionToken = "token-b1c2",
        AttemptNumber = 2,
        ExecutionType = ValidationAuditExecutionType.Trial,
        Status = ValidationAuditExecutionStatus.InProgress
    };

    private static ValidationParameterTrial BuildTrial(
        ValidationAuditExecution? auditExecution = null,
        ValidationExperiment? experiment = null) =>
        new()
        {
            Id = 1,
            ValidationExperimentId = experiment?.Id ?? 2311,
            TrialNumber = auditExecution?.TrialNumber ?? 1,
            AuthoritativeAuditExecutionId = auditExecution?.AuditExecutionId ?? Guid.Parse("22222222-2222-2222-2222-222222222222"),
            AuditAttemptNumber = auditExecution?.AttemptNumber ?? 2,
            ParameterFingerprint = "test",
            ParameterSnapshotJson = "{}"
        };

    private static ValidationCanonicalTrainingCandleScopeRequest BuildCanonicalScopeRequest(
        ValidationExperiment? experiment = null,
        StrategyExecutionRequirements? requirements = null,
        ValidationAuditExecution? auditExecution = null,
        ValidationParameterTrial? trial = null)
    {
        var audit = auditExecution ?? BuildAuditExecution();
        var exp = experiment ?? BuildExperiment();
        return new()
        {
            Experiment = exp,
            Requirements = requirements ?? BuildAdaptiveRequirements(),
            AuditExecution = audit,
            Trial = trial ?? BuildTrial(audit, exp),
            TrainingEvaluationEndExclusiveUtc = Boundary
        };
    }

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

    private sealed class NoOpAuditRepository : IValidationCandleAccessAuditRepository
    {
        public Task AddRangeAsync(IReadOnlyList<ValidationCandleAccessAudit> audits, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<ValidationAccessBatchPersistResult> AddRangeIdempotentByAccessEventIdAsync(
            IReadOnlyList<ValidationCandleAccessAudit> audits,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ValidationAccessBatchPersistResult
            {
                RequestedEventIds = audits.Select(a => a.AccessEventId).ToList(),
                CommitStatus = ValidationAccessBatchCommitStatus.CommitSucceeded,
                VerificationStatus = ValidationAccessBatchVerificationStatus.FullyPayloadConfirmed,
                CompletedAtUtc = DateTime.UtcNow
            });

        public Task<IReadOnlyList<ValidationCandleAccessAudit>> GetByExperimentIdAsync(
            long experimentId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ValidationCandleAccessAudit>>([]);
    }

    private sealed class TrackingHtfReader : IUnscopedCandleReader
    {
        private readonly IReadOnlyList<Candle> _eval;
        private readonly IReadOnlyList<Candle> _htf;

        public int CallCount { get; private set; }

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
            CallCount++;
            return Task.FromResult(timeframe == Timeframe.M5 ? _eval : _htf);
        }

        public Task<IReadOnlyList<Candle>> GetClosedCandlesBeforeUnscopedAsync(
            long symbolId,
            Timeframe timeframe,
            DateTime beforeOpenTimeUtc,
            int count,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult<IReadOnlyList<Candle>>(Array.Empty<Candle>());
        }
    }
}
