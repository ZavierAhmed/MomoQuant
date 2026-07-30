using MomoQuant.Application.Abstractions;
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

/// <summary>Milestone 23.1B1B — factory fail-closed HTF bootstrap and canonical identity.</summary>
public sealed class Milestone231B1BTests
{
    private static readonly DateTime EvalStart = new(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Boundary = new(2026, 2, 3, 0, 0, 0, DateTimeKind.Utc);
    private const long SymbolId = 7;
    private const long ExchangeId = 42;

    [Fact]
    public void CanonicalIdentity_RequiredWhenBoundAuditSet()
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
            StrategyId = 11,
            StrategyCode = null,
            StrategyVersion = MomoAdaptiveMultiTimeframeTrendBreakoutStrategy.Version,
            ExchangeId = ExchangeId,
            BoundScopeExecutionId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            BoundAuditExecutionId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            BoundExecutionToken = "token-b1b",
            BoundAttemptNumber = 1
        };

        var ex = Assert.Throws<ArgumentException>(() => request.ValidateCanonical());
        Assert.Contains("StrategyCode", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CanonicalIdentity_RequiresExchangeId()
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
            StrategyId = 11,
            StrategyCode = StrategyCodes.MomoAdaptiveMultiTimeframeTrendBreakout,
            StrategyVersion = MomoAdaptiveMultiTimeframeTrendBreakoutStrategy.Version,
            ExchangeId = 0,
            BoundScopeExecutionId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            BoundAuditExecutionId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            BoundExecutionToken = "token-b1b",
            BoundAttemptNumber = 1
        };

        var ex = Assert.Throws<ArgumentException>(() => request.ValidateCanonical());
        Assert.Contains("ExchangeId", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CanonicalIdentity_RequiresStrategyVersion()
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
            StrategyId = 11,
            StrategyCode = StrategyCodes.MomoAdaptiveMultiTimeframeTrendBreakout,
            StrategyVersion = null,
            ExchangeId = ExchangeId,
            BoundScopeExecutionId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            BoundAuditExecutionId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            BoundExecutionToken = "token-b1b",
            BoundAttemptNumber = 1
        };

        var ex = Assert.Throws<ArgumentException>(() => request.ValidateCanonical());
        Assert.Contains("StrategyVersion", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OmittedStrategyIdentity_FailsBeforeHtfCandleAccess()
    {
        var reader = new TrackingPoisonedHtfReader(BuildEval(), []);
        var factory = CreateCanonicalFactory(reader);
        var request = CanonicalAdaptiveScopeRequest(requirements: new StrategyExecutionRequirements
        {
            StrategyId = 11,
            StrategyCode = null!,
            StrategyVersion = MomoAdaptiveMultiTimeframeTrendBreakoutStrategy.Version,
            RequiredWarmupCandleCount = 0,
            RequiresHigherTimeframePartition = true,
            RequiredHigherTimeframeApi = "1h",
            HigherTimeframeMappingContractVersion = StrategyHigherTimeframeSupport.AdaptiveHtfMappingContractVersion
        });

        await Assert.ThrowsAsync<ArgumentException>(() => factory.CreateCanonicalAsync(request));
        Assert.Equal(0, reader.HtfLoadCount);
    }

    [Fact]
    public void SpoofedStrategyIdentity_FailsBeforeCandleAccess()
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
            StrategyId = 11,
            StrategyCode = "NOT_A_REAL_STRATEGY_CODE",
            StrategyVersion = MomoAdaptiveMultiTimeframeTrendBreakoutStrategy.Version,
            ExchangeId = ExchangeId,
            BoundScopeExecutionId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            BoundAuditExecutionId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            BoundExecutionToken = "token-b1b",
            BoundAttemptNumber = 1
        };

        var ex = Assert.Throws<ArgumentException>(() => request.ValidateCanonical());
        Assert.Contains("StrategyCode", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AdaptiveMappedHtf_LoadedFromBoundRequirements_NotOptionalCode()
    {
        var htf = Htf(1, SymbolId, ExchangeId, EvalStart, EvalStart.AddHours(1));
        var reader = new TrackingPoisonedHtfReader(BuildEval(), [htf]);
        var factory = CreateCanonicalFactory(reader);

        await factory.CreateCanonicalAsync(CanonicalAdaptiveScopeRequest());

        Assert.Equal(1, reader.HtfLoadCount);
        Assert.Equal(Timeframe.H1, reader.LastHtfTimeframe);
        Assert.Equal(EvalStart, reader.LastFromUtc);
        Assert.Equal(Boundary, reader.LastToUtc);
        Assert.Equal(Math.Max(200, 0), reader.LastWarmUpCount);
    }

    [Fact]
    public async Task MixedValidAndWrongExchange_FailsEntireLoad()
    {
        var valid = Htf(1, SymbolId, ExchangeId, EvalStart, EvalStart.AddHours(1));
        var poison = Htf(2, SymbolId, 99, EvalStart.AddHours(1), EvalStart.AddHours(2));
        await AssertPoisonedLoadFailsWithCode([valid, poison], ValidationCandlePartitionDenialCodes.HtfWrongExchange);
    }

    [Fact]
    public async Task MixedValidAndWrongTimeframe_FailsEntireLoad()
    {
        var valid = Htf(1, SymbolId, ExchangeId, EvalStart, EvalStart.AddHours(1));
        var poison = new Candle
        {
            Id = 2,
            SymbolId = SymbolId,
            ExchangeId = ExchangeId,
            Timeframe = Timeframe.H4,
            OpenTimeUtc = EvalStart.AddHours(1),
            CloseTimeUtc = EvalStart.AddHours(2),
            Open = 100m,
            High = 101m,
            Low = 99m,
            Close = 100.5m,
            Volume = 1m,
            IsClosed = true,
            CreatedAtUtc = EvalStart.AddHours(1)
        };
        await AssertPoisonedLoadFailsWithCode([valid, poison], ValidationCandlePartitionDenialCodes.HtfWrongTimeframe);
    }

    [Fact]
    public async Task MixedValidAndOpenHtf_FailsEntireLoad()
    {
        var valid = Htf(1, SymbolId, ExchangeId, EvalStart, EvalStart.AddHours(1));
        var poison = Htf(2, SymbolId, ExchangeId, EvalStart.AddHours(1), EvalStart.AddHours(2));
        poison.IsClosed = false;
        await AssertPoisonedLoadFailsWithCode([valid, poison], ValidationCandlePartitionDenialCodes.HtfOpenCandle);
    }

    [Fact]
    public async Task MixedValidAndPostBoundaryClose_FailsEntireLoad()
    {
        var valid = Htf(1, SymbolId, ExchangeId, EvalStart, EvalStart.AddHours(1));
        var poison = Htf(2, SymbolId, ExchangeId, Boundary, Boundary.AddHours(1));
        await AssertPoisonedLoadFailsWithCode([valid, poison], ValidationCandlePartitionDenialCodes.HtfCloseBeyondBoundary);
    }

    [Fact]
    public async Task DuplicateHtf_FailsEntireLoad()
    {
        var first = Htf(1, SymbolId, ExchangeId, EvalStart, EvalStart.AddHours(1));
        var dup = Htf(2, SymbolId, ExchangeId, EvalStart, EvalStart.AddHours(1));
        await AssertPoisonedLoadFailsWithCode([first, dup], ValidationCandlePartitionDenialCodes.HtfDuplicate);
    }

    [Fact]
    public async Task MixedValidAndPoison_RecordsRawReturnedCount_NotSilentSubset()
    {
        var valid = Htf(1, SymbolId, ExchangeId, EvalStart, EvalStart.AddHours(1));
        var poison = Htf(2, 99, ExchangeId, EvalStart.AddHours(1), EvalStart.AddHours(2));
        var reader = new TrackingPoisonedHtfReader(BuildEval(), [valid, poison]);
        Assert.Equal(2, reader.LastReturnedHtfCount);
        var factory = CreateCanonicalFactory(reader);

        await Assert.ThrowsAsync<ValidationCandlePartitionViolationException>(() =>
            factory.CreateCanonicalAsync(CanonicalAdaptiveScopeRequest()));

        var denied = Assert.Single(factory.LastBootstrapAccessEvidence, r => r.WasDenied);
        Assert.Equal(2, denied.ReturnedCandleCount);
        Assert.Equal(1, reader.HtfLoadCount);
    }

    [Fact]
    public async Task FactoryUsesOnlyUnscopedReader_NotGeneralRepository()
    {
        var htf = Htf(1, SymbolId, ExchangeId, EvalStart, EvalStart.AddHours(1));
        var reader = new TrackingPoisonedHtfReader(BuildEval(), [htf]);
        var factory = CreateCanonicalFactory(reader);

        await factory.CreateCanonicalAsync(CanonicalAdaptiveScopeRequest());

        Assert.Equal(1, reader.EvalLoadCount);
        Assert.Equal(1, reader.HtfLoadCount);
        Assert.Equal(0, reader.BeforeLoadCount);
    }

    [Fact]
    public void CoverageImport_RemainsForbiddenOnTrainingScopeExecution()
    {
        var context = new MomoQuant.Application.StrategyLab.StrategyLabExecutionContext
        {
            ExecutionPurpose = MomoQuant.Application.StrategyLab.ExecutionPurpose.ValidationTraining,
            ValidationExperimentId = 1,
            TrainingBoundaryUtc = Boundary,
            AllowCoverageImport = false,
            CallerComponent = "ValidationTrainingScopeExecution",
            CorrelationId = Guid.NewGuid().ToString("N")
        };

        Assert.False(context.AllowCoverageImport);
    }

    private async Task AssertPoisonedLoadFailsWithCode(IReadOnlyList<Candle> poisonedHtf, string expectedCode)
    {
        var reader = new TrackingPoisonedHtfReader(BuildEval(), poisonedHtf);
        Assert.Equal(poisonedHtf.Count, reader.LastReturnedHtfCount);
        var factory = CreateCanonicalFactory(reader);

        var ex = await Assert.ThrowsAsync<ValidationCandlePartitionViolationException>(() =>
            factory.CreateCanonicalAsync(CanonicalAdaptiveScopeRequest()));

        Assert.Equal(expectedCode, ex.DenialCode);
        Assert.Contains(factory.LastBootstrapAccessEvidence, r => r.WasDenied && r.DenialCode == expectedCode);
    }

    [Fact]
    public async Task MixedValidAndWrongSymbol_FailsEntireLoad()
    {
        var valid = Htf(1, SymbolId, ExchangeId, EvalStart, EvalStart.AddHours(1));
        var poison = Htf(2, 99, ExchangeId, EvalStart.AddHours(1), EvalStart.AddHours(2));
        var reader = new PoisonedHtfReader(BuildEval(), [valid, poison]);
        var factory = CreateCanonicalFactory(reader);

        var ex = await Assert.ThrowsAsync<ValidationCandlePartitionViolationException>(() =>
            factory.CreateCanonicalAsync(CanonicalAdaptiveScopeRequest()));

        Assert.Equal(ValidationCandlePartitionDenialCodes.HtfWrongSymbol, ex.DenialCode);
        var denied = Assert.Single(factory.LastBootstrapAccessEvidence, r => r.WasDenied);
        Assert.Equal(ValidationCandleAccessPurpose.FactoryBootstrapHtfLoad, denied.AccessPurpose);
        Assert.Equal(ExchangeId, denied.RequestExchangeId);
    }

    [Fact]
    public async Task UnorderedRawHtf_FailsWithoutFactorySorting()
    {
        var second = Htf(1, SymbolId, ExchangeId, EvalStart.AddHours(1), EvalStart.AddHours(2));
        var first = Htf(2, SymbolId, ExchangeId, EvalStart, EvalStart.AddHours(1));
        var reader = new PoisonedHtfReader(BuildEval(), [second, first]);
        var factory = CreateCanonicalFactory(reader);

        var ex = await Assert.ThrowsAsync<ValidationCandlePartitionViolationException>(() =>
            factory.CreateCanonicalAsync(CanonicalAdaptiveScopeRequest()));

        Assert.Equal(ValidationCandlePartitionDenialCodes.HtfUnordered, ex.DenialCode);
        Assert.Contains(factory.LastBootstrapAccessEvidence, r => r.WasDenied);
    }

    [Fact]
    public async Task EmptyMappedHtf_FailsClosed()
    {
        var reader = new PoisonedHtfReader(BuildEval(), []);
        var factory = CreateCanonicalFactory(reader);

        var ex = await Assert.ThrowsAsync<ValidationCandlePartitionViolationException>(() =>
            factory.CreateCanonicalAsync(CanonicalAdaptiveScopeRequest()));

        Assert.Equal(ValidationCandlePartitionDenialCodes.MissingPartitionHtf, ex.DenialCode);
        Assert.Contains(factory.LastBootstrapAccessEvidence, r =>
            r.WasDenied && r.DenialCode == ValidationCandlePartitionDenialCodes.MissingPartitionHtf);
    }

    [Fact]
    public async Task BootstrapEvidence_RecordsExactRequestSemantics()
    {
        var htf = Htf(1, SymbolId, ExchangeId, EvalStart, EvalStart.AddHours(1));
        var reader = new PoisonedHtfReader(BuildEval(), [htf]);
        var factory = CreateCanonicalFactory(reader);
        var request = CanonicalAdaptiveScopeRequest();
        var scope = await factory.CreateCanonicalAsync(request);

        var bootstrap = Assert.Single(scope.AccessLog, r =>
            r.AccessPurpose == ValidationCandleAccessPurpose.FactoryBootstrapHtfLoad && !r.WasDenied);
        Assert.Equal(EvalStart, bootstrap.RequestedStartUtc);
        Assert.Equal(Boundary, bootstrap.RequestedEndUtc);
        Assert.Equal(Math.Max(200, request.Requirements.RequiredWarmupCandleCount), bootstrap.RequestedCandleCount);
        Assert.Equal(SymbolId, bootstrap.RequestSymbolId);
        Assert.Equal(ExchangeId, bootstrap.RequestExchangeId);
        Assert.Equal("1h", bootstrap.RequestTimeframeApi);
        Assert.Equal(StrategyCodes.MomoAdaptiveMultiTimeframeTrendBreakout, bootstrap.RequestStrategyCode);
        Assert.Equal(MomoAdaptiveMultiTimeframeTrendBreakoutStrategy.Version, bootstrap.RequestStrategyVersion);
        Assert.Equal(request.AuditExecution.AuditExecutionId, bootstrap.AuditExecutionId);
        Assert.Equal(request.AuditExecution.ScopeExecutionId, bootstrap.ScopeExecutionId);
        Assert.StartsWith("BootstrapHTF:", bootstrap.DatasetPartition, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeniedBootstrap_RecordedBeforeThrow()
    {
        var reader = new PoisonedHtfReader(BuildEval(), []);
        var factory = CreateCanonicalFactory(reader);

        await Assert.ThrowsAsync<ValidationCandlePartitionViolationException>(() =>
            factory.CreateCanonicalAsync(CanonicalAdaptiveScopeRequest()));

        var denied = Assert.Single(factory.LastBootstrapAccessEvidence);
        Assert.True(denied.WasDenied);
        Assert.False(string.IsNullOrWhiteSpace(denied.DenialReason));
        Assert.Equal(ValidationCandleAccessPurpose.FactoryBootstrapHtfLoad, denied.AccessPurpose);
    }

    private static ValidationTrainingCandleScopeFactory CreateCanonicalFactory(IUnscopedCandleReader reader) =>
        new(reader, new NoOpValidationCandleAccessRecorder());

    private sealed class NoOpValidationCandleAccessRecorder : IValidationCandleAccessRecorder
    {
        public Task<ValidationAccessBatchPersistResult> FlushAsync(
            IValidationTrainingCandleScope scope,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ValidationAccessBatchPersistResult
            {
                RequestedEventIds = [],
                CommitStatus = ValidationAccessBatchCommitStatus.CommitSucceeded,
                VerificationStatus = ValidationAccessBatchVerificationStatus.FullyPayloadConfirmed,
                CompletedAtUtc = DateTime.UtcNow
            });
    }
    private static ValidationCanonicalTrainingCandleScopeRequest CanonicalAdaptiveScopeRequest(
        StrategyExecutionRequirements? requirements = null) =>
        new()
        {
            Experiment = BuildExperiment(),
            Requirements = requirements ?? BuildAdaptiveRequirements(),
            AuditExecution = new ValidationAuditExecution
            {
                AuditExecutionId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                ValidationExperimentId = 2311,
                ValidationTrialId = 1,
                TrialNumber = 1,
                ScopeExecutionId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                ExecutionToken = "token-b1b",
                AttemptNumber = 1,
                ExecutionType = ValidationAuditExecutionType.Trial,
                Status = ValidationAuditExecutionStatus.InProgress
            },
            Trial = new ValidationParameterTrial
            {
                Id = 1,
                ValidationExperimentId = 2311,
                TrialNumber = 1,
                AuthoritativeAuditExecutionId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                AuditAttemptNumber = 1,
                ParameterFingerprint = "test",
                ParameterSnapshotJson = "{}"
            },
            TrainingEvaluationEndExclusiveUtc = Boundary
        };

    private static StrategyExecutionRequirements BuildAdaptiveRequirements(long strategyId = 11, int warmup = 0) =>
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

#pragma warning disable CS0618
    private static ValidationTrainingCandleScopeRequest CanonicalAdaptiveRequest() =>
        Copy(CanonicalRequest(),
            strategyCode: StrategyCodes.MomoAdaptiveMultiTimeframeTrendBreakout,
            strategyVersion: MomoAdaptiveMultiTimeframeTrendBreakoutStrategy.Version,
            strategyId: 11,
            requiredWarmup: 0);
#pragma warning restore CS0618

    private static ValidationTrainingCandleScopeRequest Copy(
        ValidationTrainingCandleScopeRequest source,
        long? strategyId = null,
        string? strategyCode = null,
        string? strategyVersion = null,
        int? requiredWarmup = null) =>
        new()
        {
            ValidationExperimentId = source.ValidationExperimentId,
            SymbolId = source.SymbolId,
            SymbolName = source.SymbolName,
            Timeframe = source.Timeframe,
            TrainingEvaluationStartUtc = source.TrainingEvaluationStartUtc,
            TrainingEvaluationEndExclusiveUtc = source.TrainingEvaluationEndExclusiveUtc,
            ValidationBoundaryUtc = source.ValidationBoundaryUtc,
            RequiredWarmupCandleCount = requiredWarmup ?? source.RequiredWarmupCandleCount,
            RequirementsVersion = source.RequirementsVersion,
            StrategyId = strategyId ?? source.StrategyId,
            StrategyCode = strategyCode ?? source.StrategyCode,
            StrategyVersion = strategyVersion ?? source.StrategyVersion,
            ExchangeId = source.ExchangeId,
            BoundScopeExecutionId = source.BoundScopeExecutionId,
            BoundAuditExecutionId = source.BoundAuditExecutionId,
            BoundExecutionToken = source.BoundExecutionToken,
            BoundAttemptNumber = source.BoundAttemptNumber
        };

    private static ValidationTrainingCandleScopeRequest CanonicalRequest() => new()
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
        BoundExecutionToken = "token-b1b",
        BoundAttemptNumber = 1
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

    private class TrackingPoisonedHtfReader : IUnscopedCandleReader
    {
        private readonly IReadOnlyList<Candle> _eval;
        private readonly IReadOnlyList<Candle> _htf;

        public int EvalLoadCount { get; private set; }
        public int HtfLoadCount { get; private set; }
        public int BeforeLoadCount { get; private set; }
        public Timeframe? LastHtfTimeframe { get; private set; }
        public DateTime? LastFromUtc { get; private set; }
        public DateTime? LastToUtc { get; private set; }
        public int LastWarmUpCount { get; private set; }
        public int LastReturnedHtfCount => _htf.Count;

        public TrackingPoisonedHtfReader(IReadOnlyList<Candle> eval, IReadOnlyList<Candle> htf)
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
                EvalLoadCount++;
                return Task.FromResult(_eval);
            }

            HtfLoadCount++;
            LastHtfTimeframe = timeframe;
            LastFromUtc = fromUtc;
            LastToUtc = toUtc;
            LastWarmUpCount = warmUpCount;
            return Task.FromResult(_htf);
        }

        public Task<IReadOnlyList<Candle>> GetClosedCandlesBeforeUnscopedAsync(
            long symbolId,
            Timeframe timeframe,
            DateTime beforeOpenTimeUtc,
            int count,
            CancellationToken cancellationToken = default)
        {
            BeforeLoadCount++;
            return Task.FromResult<IReadOnlyList<Candle>>(Array.Empty<Candle>());
        }
    }

    private sealed class PoisonedHtfReader : TrackingPoisonedHtfReader
    {
        public PoisonedHtfReader(IReadOnlyList<Candle> eval, IReadOnlyList<Candle> htf)
            : base(eval, htf)
        {
        }
    }

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
}
