using MomoQuant.Application.Abstractions;

using MomoQuant.Application.Strategies;

using MomoQuant.Application.Strategies.Implementations;

using MomoQuant.Application.Strategies.MomoAdaptive;
using MomoQuant.Application.Strategies.PriceStructure;

using MomoQuant.Application.ValidationLab;

using MomoQuant.Domain.Constants;

using MomoQuant.Domain.Enums;

using MomoQuant.Domain.MarketData;

using MomoQuant.Domain.ValidationLab;



namespace MomoQuant.UnitTests.Strategies;



/// <summary>Milestone 23.1B1C1 — pre-access factory validation through CreateCanonicalAsync / CreateLtfWarmupBootstrapAsync.</summary>

public sealed class Milestone231B1C1FactoryTests

{

    private static readonly DateTime EvalStart = new(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime Boundary = new(2026, 2, 3, 0, 0, 0, DateTimeKind.Utc);

    private const long SymbolId = 7;

    private const long ExchangeId = 42;

    private const long StrategyId = 11;



    [Fact]

    public async Task UnboundCreateAsync_ThrowsBeforeAnyReaderAccess()

    {

        var reader = new TrackingHtfReader(BuildEval(), [Htf(1, SymbolId, ExchangeId, EvalStart, EvalStart.AddHours(1))]);

        var factory = CreateCanonicalFactory(reader);



#pragma warning disable CS0618

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>

            factory.CreateAsync(BuildLegacyCanonicalRequest()));

#pragma warning restore CS0618



        Assert.Contains("quarantined", ex.Message, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(0, reader.CallCount);

    }



    [Fact]

    public void LtfOnlyWarmupBootstrap_LegacyRequestRejected()

    {

#pragma warning disable CS0618

        var request = BuildLegacyCanonicalRequest();

        request = CopyLegacy(request, ltfOnlyWarmupBootstrap: true);

#pragma warning restore CS0618



        var ex = Assert.Throws<ArgumentException>(() => request.Validate());

        Assert.Contains("LtfOnlyWarmupBootstrap", ex.Message, StringComparison.Ordinal);

    }



    [Fact]

    public void CreateAsync_IsQuarantined()

    {

#pragma warning disable CS0618

        var ex = Assert.Throws<InvalidOperationException>(() =>

            new ValidationTrainingCandleScopeFactory(new TrackingHtfReader(BuildEval(), []))

                .CreateAsync(BuildLegacyCanonicalRequest()).GetAwaiter().GetResult());

#pragma warning restore CS0618

        Assert.Contains("quarantined", ex.Message, StringComparison.OrdinalIgnoreCase);

    }



    [Fact]

    public async Task CreateLtfWarmupBootstrapAsync_CannotLoadHtf()

    {

        var reader = new TrackingHtfReader(BuildEval(), [Htf(1, SymbolId, ExchangeId, EvalStart, EvalStart.AddHours(1))]);

        var factory = new ValidationTrainingCandleScopeFactory(reader);



        await using var scope = await factory.CreateLtfWarmupBootstrapAsync(BuildLtfOnlyRequest());



        Assert.Equal(0, reader.HtfLoadCount);

        Assert.Equal(1, reader.EvalLoadCount);

        Assert.Empty(factory.LastBootstrapAccessEvidence);

        Assert.NotEqual(Guid.Empty, scope.ScopeExecutionId);

    }



    [Fact]

    public async Task CreateForExperimentAsync_IsQuarantined()

    {

        var reader = new TrackingHtfReader(BuildEval(), Array.Empty<Candle>());

        var factory = new ValidationTrainingCandleScopeFactory(reader);



#pragma warning disable CS0618

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>

            factory.CreateForExperimentAsync(BuildExperiment(), CancellationToken.None));

#pragma warning restore CS0618

        Assert.Contains("quarantined", ex.Message, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(0, reader.CallCount);

    }



    [Fact]

    public async Task CreateCanonicalAsync_RequiresRecorderBeforeCandleAccess()

    {

        var reader = new TrackingHtfReader(BuildEval(), [Htf(1, SymbolId, ExchangeId, EvalStart, EvalStart.AddHours(1))]);

        var factory = new ValidationTrainingCandleScopeFactory(reader);



        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>

            factory.CreateCanonicalAsync(BuildCanonicalScopeRequest()));



        Assert.Contains("recorder required", ex.Message, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(0, reader.CallCount);

    }



    [Fact]

    public async Task MissingExperiment_ThrowsBeforeReaderAccess()

    {

        var reader = NewReaderWithHtf();

        var factory = CreateCanonicalFactory(reader);

        var request = new ValidationCanonicalTrainingCandleScopeRequest
        {
            Experiment = null!,
            Requirements = BuildAdaptiveRequirements(),
            AuditExecution = BuildAuditExecution(),
            TrainingEvaluationEndExclusiveUtc = Boundary
        };



        await Assert.ThrowsAsync<ArgumentNullException>(() => factory.CreateCanonicalAsync(request));

        Assert.Equal(0, reader.CallCount);

    }



    [Fact]

    public async Task MissingRequirements_ThrowsBeforeReaderAccess()

    {

        var reader = NewReaderWithHtf();

        var factory = CreateCanonicalFactory(reader);

        var request = new ValidationCanonicalTrainingCandleScopeRequest
        {
            Experiment = BuildExperiment(),
            Requirements = null!,
            AuditExecution = BuildAuditExecution(),
            TrainingEvaluationEndExclusiveUtc = Boundary
        };



        await Assert.ThrowsAsync<ArgumentNullException>(() => factory.CreateCanonicalAsync(request));

        Assert.Equal(0, reader.CallCount);

    }



    [Fact]

    public async Task MissingAuditExecution_ThrowsBeforeReaderAccess()

    {

        var reader = NewReaderWithHtf();

        var factory = CreateCanonicalFactory(reader);

        var request = new ValidationCanonicalTrainingCandleScopeRequest
        {
            Experiment = BuildExperiment(),
            Requirements = BuildAdaptiveRequirements(),
            AuditExecution = null!,
            TrainingEvaluationEndExclusiveUtc = Boundary
        };



        await Assert.ThrowsAsync<ArgumentNullException>(() => factory.CreateCanonicalAsync(request));

        Assert.Equal(0, reader.CallCount);

    }



    [Fact]

    public async Task ExperimentIdMismatch_ZeroReaderCalls()

    {

        var reader = NewReaderWithHtf();

        var factory = CreateCanonicalFactory(reader);

        var audit = BuildAuditExecution();

        audit.ValidationExperimentId = 9999;



        await Assert.ThrowsAsync<ArgumentException>(() =>

            factory.CreateCanonicalAsync(BuildCanonicalScopeRequest(auditExecution: audit)));

        Assert.Equal(0, reader.CallCount);

    }



    [Fact]

    public async Task StrategyIdNotPositive_ZeroReaderCalls()

    {

        var reader = NewReaderWithHtf();

        var factory = CreateCanonicalFactory(reader);

        var requirements = BuildAdaptiveRequirements(strategyId: 0);



        await Assert.ThrowsAsync<ArgumentException>(() =>

            factory.CreateCanonicalAsync(BuildCanonicalScopeRequest(requirements: requirements)));

        Assert.Equal(0, reader.CallCount);

    }



    [Fact]

    public async Task StrategyCodeMismatch_ZeroReaderCalls()

    {

        var reader = NewReaderWithHtf();

        var factory = CreateCanonicalFactory(reader);

        var requirements = BuildAdaptiveRequirements();

        requirements = CopyRequirements(requirements, strategyCode: StrategyCodes.PriceStructureBreakoutRetest);



        await Assert.ThrowsAsync<ArgumentException>(() =>

            factory.CreateCanonicalAsync(BuildCanonicalScopeRequest(requirements: requirements)));

        Assert.Equal(0, reader.CallCount);

    }



    [Fact]

    public async Task StrategyVersionMismatch_ZeroReaderCalls()

    {

        var reader = NewReaderWithHtf();

        var factory = CreateCanonicalFactory(reader);

        var requirements = BuildAdaptiveRequirements();

        requirements = CopyRequirements(requirements, strategyVersion: "not-the-experiment-version");



        await Assert.ThrowsAsync<ArgumentException>(() =>

            factory.CreateCanonicalAsync(BuildCanonicalScopeRequest(requirements: requirements)));

        Assert.Equal(0, reader.CallCount);

    }



    [Fact]

    public async Task UnsupportedStrategyVersion_ZeroReaderCalls()

    {

        var reader = NewReaderWithHtf();

        var factory = CreateCanonicalFactory(reader);

        var experiment = BuildExperiment();

        experiment.StrategyVersion = "v0.0.0-unsupported";

        var requirements = BuildAdaptiveRequirements();

        requirements = CopyRequirements(requirements, strategyVersion: experiment.StrategyVersion);



        await Assert.ThrowsAsync<ArgumentException>(() =>

            factory.CreateCanonicalAsync(BuildCanonicalScopeRequest(experiment: experiment, requirements: requirements)));

        Assert.Equal(0, reader.CallCount);

    }



    [Fact]

    public async Task ExchangeIdNotPositive_ZeroReaderCalls()

    {

        var reader = NewReaderWithHtf();

        var factory = CreateCanonicalFactory(reader);

        var experiment = BuildExperiment();

        experiment.ExchangeId = 0;



        await Assert.ThrowsAsync<ArgumentException>(() =>

            factory.CreateCanonicalAsync(BuildCanonicalScopeRequest(experiment: experiment)));

        Assert.Equal(0, reader.CallCount);

    }



    [Fact]

    public async Task SymbolIdNotPositive_ZeroReaderCalls()

    {

        var reader = NewReaderWithHtf();

        var factory = CreateCanonicalFactory(reader);

        var experiment = BuildExperiment();

        experiment.SymbolId = 0;



        await Assert.ThrowsAsync<ArgumentException>(() =>

            factory.CreateCanonicalAsync(BuildCanonicalScopeRequest(experiment: experiment)));

        Assert.Equal(0, reader.CallCount);

    }



    [Fact]

    public async Task MissingTimeframe_ZeroReaderCalls()

    {

        var reader = NewReaderWithHtf();

        var factory = CreateCanonicalFactory(reader);

        var experiment = BuildExperiment();

        experiment.Timeframe = "";



        await Assert.ThrowsAsync<ArgumentException>(() =>

            factory.CreateCanonicalAsync(BuildCanonicalScopeRequest(experiment: experiment)));

        Assert.Equal(0, reader.CallCount);

    }



    [Fact]

    public async Task RequirementsVersionMissing_ZeroReaderCalls()

    {

        var reader = NewReaderWithHtf();

        var factory = CreateCanonicalFactory(reader);

        var requirements = BuildAdaptiveRequirements();

        requirements = CopyRequirements(requirements, requirementsVersion: "");



        await Assert.ThrowsAsync<ArgumentException>(() =>

            factory.CreateCanonicalAsync(BuildCanonicalScopeRequest(requirements: requirements)));

        Assert.Equal(0, reader.CallCount);

    }



    [Fact]

    public async Task AuditExecutionTokenMissing_ZeroReaderCalls()

    {

        var reader = NewReaderWithHtf();

        var factory = CreateCanonicalFactory(reader);

        var audit = BuildAuditExecution();

        audit.ExecutionToken = "";



        await Assert.ThrowsAsync<ArgumentException>(() =>

            factory.CreateCanonicalAsync(BuildCanonicalScopeRequest(auditExecution: audit)));

        Assert.Equal(0, reader.CallCount);

    }



    [Fact]

    public async Task AuditAttemptNotPositive_ZeroReaderCalls()

    {

        var reader = NewReaderWithHtf();

        var factory = CreateCanonicalFactory(reader);

        var audit = BuildAuditExecution();

        audit.AttemptNumber = 0;



        await Assert.ThrowsAsync<ArgumentException>(() =>

            factory.CreateCanonicalAsync(BuildCanonicalScopeRequest(auditExecution: audit)));

        Assert.Equal(0, reader.CallCount);

    }



    [Fact]

    public async Task AdaptiveHtf_LoadedFromRequirementsRequiredHigherTimeframeApi()

    {

        var htf = Htf(1, SymbolId, ExchangeId, EvalStart, EvalStart.AddHours(1));

        var reader = new TrackingHtfReader(BuildEval(), [htf]);

        var factory = CreateCanonicalFactory(reader);



        await factory.CreateCanonicalAsync(BuildCanonicalScopeRequest());



        Assert.Equal(1, reader.HtfLoadCount);

        Assert.Equal(Timeframe.H1, reader.LastHtfTimeframe);

        Assert.Equal("1h", BuildAdaptiveRequirements().RequiredHigherTimeframeApi);

    }



    [Fact]

    public async Task PriceStructureCanonical_NoHtfLoadWhenPartitionNotRequired()

    {

        var reader = NewReaderWithHtf();

        var factory = CreateCanonicalFactory(reader);

        var experiment = BuildExperiment(

            StrategyCodes.PriceStructureBreakoutRetest,

            PriceStructureBreakoutRetestEvaluator.StrategyVersion);

        var requirements = BuildNonAdaptiveRequirements(

            StrategyCodes.PriceStructureBreakoutRetest,

            PriceStructureBreakoutRetestEvaluator.StrategyVersion);



        await factory.CreateCanonicalAsync(BuildCanonicalScopeRequest(experiment: experiment, requirements: requirements));



        Assert.Equal(0, reader.HtfLoadCount);

        Assert.True(reader.EvalLoadCount >= 1);

    }



    [Fact]

    public async Task RangeReversionCanonical_NoHtfLoadWhenPartitionNotRequired()

    {

        var reader = NewReaderWithHtf();

        var factory = CreateCanonicalFactory(reader);

        var experiment = BuildExperiment(

            StrategyCodes.MomoVolatilityRangeReversion,

            MomoVolatilityRangeReversionStrategy.Version);

        var requirements = BuildNonAdaptiveRequirements(

            StrategyCodes.MomoVolatilityRangeReversion,

            MomoVolatilityRangeReversionStrategy.Version);



        await factory.CreateCanonicalAsync(BuildCanonicalScopeRequest(experiment: experiment, requirements: requirements));



        Assert.Equal(0, reader.HtfLoadCount);

        Assert.True(reader.EvalLoadCount >= 1);

    }



    [Fact]

    public async Task SuccessfulHtfBootstrap_RecordsStableAccessEventIdAndAuditLinkage()

    {

        var factory = CreateCanonicalFactory(NewReaderWithHtf());

        var request = BuildCanonicalScopeRequest();



        await factory.CreateCanonicalAsync(request);

        var firstId = Assert.Single(factory.LastBootstrapAccessEvidence, r => !r.WasDenied).AccessEventId;



        await factory.CreateCanonicalAsync(request);

        var bootstrap = Assert.Single(factory.LastBootstrapAccessEvidence, r => !r.WasDenied);



        Assert.Equal(firstId, bootstrap.AccessEventId);

        Assert.Equal(request.AuditExecution.AuditExecutionId, bootstrap.AuditExecutionId);

        Assert.Equal(request.AuditExecution.ScopeExecutionId, bootstrap.ScopeExecutionId);

        Assert.Equal(1, bootstrap.ScopeSequenceNumber);

    }



    private static TrackingHtfReader NewReaderWithHtf() =>

        new(BuildEval(), [Htf(1, SymbolId, ExchangeId, EvalStart, EvalStart.AddHours(1))]);



    private static ValidationTrainingCandleScopeFactory CreateCanonicalFactory(IUnscopedCandleReader reader) =>

        new(reader, new ValidationCandleAccessRecorder(new NoOpAuditRepository()));



    private static ValidationLtfWarmupBootstrapRequest BuildLtfOnlyRequest() => new()

    {

        ValidationExperimentId = 2311,

        SymbolId = SymbolId,

        SymbolName = "BTCUSDT",

        Timeframe = "5m",

        TrainingEvaluationStartUtc = EvalStart,

        TrainingEvaluationEndExclusiveUtc = Boundary,

        ValidationBoundaryUtc = Boundary,

        RequiredWarmupCandleCount = 0,

        RequirementsVersion = StrategyExecutionRequirements.Version

    };



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



    private static StrategyExecutionRequirements BuildNonAdaptiveRequirements(string strategyCode, string strategyVersion) =>

        new()

        {

            StrategyId = StrategyId,

            StrategyCode = strategyCode,

            StrategyVersion = strategyVersion,

            RequiredWarmupCandleCount = 0,

            RequiresHigherTimeframePartition = false

        };



    private static StrategyExecutionRequirements CopyRequirements(

        StrategyExecutionRequirements source,

        long? strategyId = null,

        string? strategyCode = null,

        string? strategyVersion = null,

        string? requirementsVersion = null) =>

        new()

        {

            StrategyId = strategyId ?? source.StrategyId,

            StrategyCode = strategyCode ?? source.StrategyCode,

            StrategyVersion = strategyVersion ?? source.StrategyVersion,

            RequiredWarmupCandleCount = source.RequiredWarmupCandleCount,

            RequiresHigherTimeframePartition = source.RequiresHigherTimeframePartition,

            RequiredHigherTimeframeApi = source.RequiredHigherTimeframeApi,

            HigherTimeframeMappingContractVersion = source.HigherTimeframeMappingContractVersion,

            RequirementsVersion = requirementsVersion ?? source.RequirementsVersion

        };



    private static ValidationAuditExecution BuildAuditExecution() => new()

    {

        AuditExecutionId = Guid.Parse("22222222-2222-2222-2222-222222222222"),

        ValidationExperimentId = 2311,

        ScopeExecutionId = Guid.Parse("11111111-1111-1111-1111-111111111111"),

        ExecutionToken = "token-b1c1",

        AttemptNumber = 2

    };



    private static ValidationCanonicalTrainingCandleScopeRequest BuildCanonicalScopeRequest(

        ValidationExperiment? experiment = null,

        StrategyExecutionRequirements? requirements = null,

        ValidationAuditExecution? auditExecution = null) =>

        new()

        {

            Experiment = experiment ?? BuildExperiment(),

            Requirements = requirements ?? BuildAdaptiveRequirements(),

            AuditExecution = auditExecution ?? BuildAuditExecution(),

            TrainingEvaluationEndExclusiveUtc = Boundary

        };



#pragma warning disable CS0618

    private static ValidationTrainingCandleScopeRequest BuildLegacyCanonicalRequest() => new()

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

        StrategyId = StrategyId,

        StrategyCode = StrategyCodes.MomoAdaptiveMultiTimeframeTrendBreakout,

        StrategyVersion = MomoAdaptiveMultiTimeframeTrendBreakoutStrategy.Version,

        ExchangeId = ExchangeId,

        BoundScopeExecutionId = Guid.Parse("11111111-1111-1111-1111-111111111111"),

        BoundAuditExecutionId = Guid.Parse("22222222-2222-2222-2222-222222222222"),

        BoundExecutionToken = "token-b1c1",

        BoundAttemptNumber = 2

    };



    private static ValidationTrainingCandleScopeRequest CopyLegacy(

        ValidationTrainingCandleScopeRequest source,

        bool? ltfOnlyWarmupBootstrap = null) =>

        new()

        {

            ValidationExperimentId = source.ValidationExperimentId,

            SymbolId = source.SymbolId,

            SymbolName = source.SymbolName,

            Timeframe = source.Timeframe,

            TrainingEvaluationStartUtc = source.TrainingEvaluationStartUtc,

            TrainingEvaluationEndExclusiveUtc = source.TrainingEvaluationEndExclusiveUtc,

            ValidationBoundaryUtc = source.ValidationBoundaryUtc,

            RequiredWarmupCandleCount = source.RequiredWarmupCandleCount,

            RequirementsVersion = source.RequirementsVersion,

            StrategyId = source.StrategyId,

            StrategyCode = source.StrategyCode,

            StrategyVersion = source.StrategyVersion,

            ExchangeId = source.ExchangeId,

            BoundScopeExecutionId = source.BoundScopeExecutionId,

            BoundAuditExecutionId = source.BoundAuditExecutionId,

            BoundExecutionToken = source.BoundExecutionToken,

            BoundAttemptNumber = source.BoundAttemptNumber,

            LtfOnlyWarmupBootstrap = ltfOnlyWarmupBootstrap ?? source.LtfOnlyWarmupBootstrap

        };

#pragma warning restore CS0618



    private static ValidationExperiment BuildExperiment(

        string? strategyCode = null,

        string? strategyVersion = null) => new()

    {

        Id = 2311,

        SymbolId = SymbolId,

        Symbol = "BTCUSDT",

        Timeframe = "5m",

        ExchangeId = ExchangeId,

        StrategyCode = strategyCode ?? StrategyCodes.MomoAdaptiveMultiTimeframeTrendBreakout,

        StrategyVersion = strategyVersion ?? MomoAdaptiveMultiTimeframeTrendBreakoutStrategy.Version,

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

        public int HtfLoadCount { get; private set; }

        public int EvalLoadCount { get; private set; }

        public Timeframe? LastHtfTimeframe { get; private set; }



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

            if (timeframe == Timeframe.M5)

            {

                EvalLoadCount++;

                return Task.FromResult(_eval);

            }



            HtfLoadCount++;

            LastHtfTimeframe = timeframe;

            return Task.FromResult(_htf);

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
