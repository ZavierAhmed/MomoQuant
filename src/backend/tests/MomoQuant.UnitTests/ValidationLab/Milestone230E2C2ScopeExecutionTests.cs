using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Common;
using MomoQuant.Application.Research;
using MomoQuant.Application.StrategyLab;
using MomoQuant.Application.ValidationLab;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.MarketData;
using MomoQuant.Domain.ValidationLab;

namespace MomoQuant.UnitTests.ValidationLab;

/// <summary>
/// Milestone 23.0E2C2 — production <see cref="ValidationTrainingScopeExecution"/> body/flush capture
/// without masking and authoritative flush semantics.
/// </summary>
public sealed class Milestone230E2C2ScopeExecutionTests
{
    [Fact]
    public async Task ExecuteTrial_BodyFailsAndFlushSucceeds_PreservesBodyFailure()
    {
        var audits = new FakeCandleAccessAuditRepository();
        var recorder = new ValidationCandleAccessRecorder(audits);
        var factory = new FakeScopeFactory();
        var execution = new ValidationTrainingScopeExecution(factory, recorder);

        var result = await execution.ExecuteTrialAsync(
            factory.Scope,
            trialNumber: 1,
            trialId: 10,
            trialBody: () => throw new InvalidOperationException("trial-body-fail"));

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.BodyException);
        Assert.Null(result.FlushException);
        Assert.True(result.FlushAttempted);
        Assert.Equal(ValidationTrainingFailureCodes.TrialExecutionFailed, result.ToFailureAggregate().PrimaryFailure!.Code);
    }

    [Fact]
    public async Task ExecuteTrial_BodySucceedsAndFlushFails_ReturnsAuditFailure()
    {
        var audits = new FakeCandleAccessAuditRepository();
        var recorder = new FlushFailingRecorder(new ValidationCandleAccessRecorder(audits));
        var factory = new FakeScopeFactory();
        var execution = new ValidationTrainingScopeExecution(factory, recorder);

        _ = factory.Scope.GetRange(
            factory.Scope.SegmentStartUtc,
            factory.Scope.SegmentStartUtc.AddHours(1),
            "AccessBeforeFlushFail");

        var result = await execution.ExecuteTrialAsync(
            factory.Scope,
            trialNumber: 2,
            trialId: 11,
            trialBody: () => Task.CompletedTask);

        Assert.False(result.IsSuccess);
        Assert.Null(result.BodyException);
        Assert.NotNull(result.FlushException);
        Assert.IsType<ValidationAccessEvidencePersistenceException>(result.FlushException!.SourceException);
        Assert.Equal(
            ValidationTrainingFailureCodes.ValidationAccessAuditPersistenceFailed,
            result.ToFailureAggregate().PrimaryFailure!.Code);
    }

    [Fact]
    public async Task ExecuteTrial_BodyAndFlushFail_PreservesBothWithCorrectPrecedence()
    {
        var audits = new FakeCandleAccessAuditRepository();
        var recorder = new FlushFailingRecorder(new ValidationCandleAccessRecorder(audits));
        var factory = new FakeScopeFactory();
        var execution = new ValidationTrainingScopeExecution(factory, recorder);

        var result = await execution.ExecuteTrialAsync(
            factory.Scope,
            trialNumber: 3,
            trialId: 12,
            trialBody: () => throw new InvalidOperationException("trial-body-fail"));

        var aggregate = result.ToFailureAggregate();
        Assert.False(result.IsSuccess);
        Assert.NotNull(result.BodyException);
        Assert.NotNull(result.FlushException);
        Assert.Equal(ValidationTrainingFailureCodes.ValidationAccessAuditPersistenceFailed, aggregate.PrimaryFailure!.Code);
        Assert.Equal(2, aggregate.AllFailures.Count);
        Assert.Contains(aggregate.AllFailures, f => f.Code == ValidationTrainingFailureCodes.TrialExecutionFailed);
        Assert.Contains(aggregate.AllFailures, f => f.Code == ValidationTrainingFailureCodes.ValidationAccessAuditPersistenceFailed);
    }

    [Fact]
    public async Task ExecuteWithScope_BodyAndOuterFlushFail_PreservesBoth()
    {
        var audits = new FakeCandleAccessAuditRepository();
        var recorder = new FlushFailingRecorder(new ValidationCandleAccessRecorder(audits));
        var factory = new FakeScopeFactory();
        var execution = new ValidationTrainingScopeExecution(factory, recorder);
        var experiment = new ValidationExperiment
        {
            Id = 7,
            TrainingStartUtc = factory.Scope.SegmentStartUtc,
            TrainingEndUtc = factory.Scope.ValidationBoundaryUtc.AddHours(-1),
            ValidationStartUtc = factory.Scope.ValidationBoundaryUtc
        };

        var result = await execution.ExecuteWithScopeAsync(
            experiment,
            ValidationTrainingCandleScopeRequest.FromExperimentLegacy(
                experiment,
                trainingEvaluationEndExclusiveUtc: factory.Scope.SegmentEndExclusiveUtc),
            async scope =>
            {
                _ = scope.GetRange(scope.SegmentStartUtc, scope.SegmentStartUtc.AddHours(1), "OuterBodyAccess");
                await ThrowFromNamedOuterBodyHelper();
            });

        var aggregate = result.ToFailureAggregate();
        Assert.False(result.IsSuccess);
        Assert.NotNull(result.BodyException);
        Assert.NotNull(result.FlushException);
        Assert.IsType<InvalidOperationException>(result.BodyException!.SourceException);
        Assert.IsType<ValidationAccessEvidencePersistenceException>(result.FlushException!.SourceException);
        Assert.Equal(ValidationTrainingFailurePhase.OuterScopeFlush, result.FlushPhase);
        Assert.Equal(ValidationTrainingFailureCodes.ValidationAccessAuditPersistenceFailed, aggregate.PrimaryFailure!.Code);
        Assert.Equal(2, aggregate.AllFailures.Count);
        Assert.Contains(aggregate.AllFailures, f => f.Code == ValidationTrainingFailureCodes.TrialExecutionFailed);
        Assert.Contains(aggregate.AllFailures, f => f.Code == ValidationTrainingFailureCodes.ValidationAccessAuditPersistenceFailed);
    }

    [Fact]
    public async Task ExecuteWithScope_ThrowIfFailed_PreservesNamedHelperStackOrigin()
    {
        var audits = new FakeCandleAccessAuditRepository();
        var recorder = new ValidationCandleAccessRecorder(audits);
        var factory = new FakeScopeFactory();
        var execution = new ValidationTrainingScopeExecution(factory, recorder);
        var experiment = new ValidationExperiment
        {
            Id = 8,
            TrainingStartUtc = factory.Scope.SegmentStartUtc,
            TrainingEndUtc = factory.Scope.ValidationBoundaryUtc.AddHours(-1),
            ValidationStartUtc = factory.Scope.ValidationBoundaryUtc
        };

        var result = await execution.ExecuteWithScopeAsync(
            experiment,
            ValidationTrainingCandleScopeRequest.FromExperimentLegacy(
                experiment,
                trainingEvaluationEndExclusiveUtc: factory.Scope.SegmentEndExclusiveUtc),
            async _ => await ThrowFromNamedOuterBodyHelper());

        var thrown = Assert.Throws<InvalidOperationException>(() => result.ThrowIfFailed());
        Assert.Contains(nameof(ThrowFromNamedOuterBodyHelper), thrown.StackTrace);
    }

    private static Task ThrowFromNamedOuterBodyHelper()
    {
        throw new InvalidOperationException("named outer body helper failure");
    }

    [Fact]
    public async Task BoundaryFailureAndFlushFailure_BoundaryIsNotMasked()
    {
        var audits = new FakeCandleAccessAuditRepository();
        var recorder = new FlushFailingRecorder(new ValidationCandleAccessRecorder(audits));
        var factory = new FakeScopeFactory();
        var execution = new ValidationTrainingScopeExecution(factory, recorder);

        var result = await execution.ExecuteTrialAsync(
            factory.Scope,
            trialNumber: 4,
            trialId: 13,
            trialBody: () =>
            {
                _ = factory.Scope.GetByOpenTimeUtc(factory.Scope.ValidationBoundaryUtc, "LeakProbe");
                return Task.CompletedTask;
            });

        var aggregate = result.ToFailureAggregate();
        Assert.False(result.IsSuccess);
        Assert.IsType<ValidationDataLeakageException>(result.BodyException!.SourceException);
        Assert.NotNull(result.FlushException);
        Assert.Equal(ValidationTrainingFailureCodes.ValidationDataLeakage, aggregate.PrimaryFailure!.Code);
        Assert.Equal(2, aggregate.AllFailures.Count);
    }

    [Fact]
    public async Task AuthoritativeFlushAttempt_IsNotRepeatedByBoundaryHandler()
    {
        var audits = new FakeCandleAccessAuditRepository();
        var recorder = new CountingRecorder(new ValidationCandleAccessRecorder(audits));
        var factory = new FakeScopeFactory();
        var execution = new ValidationTrainingScopeExecution(factory, recorder);
        var trials = new FakeTrialRepository();
        var experiments = new FakeExperimentRepository();
        var handler = new ValidationTrainingFailureHandler(
            recorder,
            audits,
            trials,
            experiments,
            new ValidationLeakageAuditor(),
            new FakeOperationStatusService());

        var experiment = new ValidationExperiment
        {
            Id = 9,
            Status = ValidationExperimentStatus.TrainingRunning,
            TrainingStartUtc = factory.Scope.SegmentStartUtc,
            TrainingEndUtc = factory.Scope.ValidationBoundaryUtc,
            ValidationStartUtc = factory.Scope.ValidationBoundaryUtc,
            MaximumTrials = 1,
            DiagnosticsJson = "[]"
        };
        experiments.Items.Add(experiment);
        var trial = new ValidationParameterTrial
        {
            Id = 55,
            ValidationExperimentId = 9,
            TrialNumber = 4,
            Status = ValidationTrialStatus.Running,
            ParameterFingerprint = "fp"
        };
        trials.Items.Add(trial);

        var executionResult = await execution.ExecuteTrialAsync(
            factory.Scope,
            trialNumber: 4,
            trialId: 55,
            trialBody: () =>
            {
                _ = factory.Scope.GetByOpenTimeUtc(factory.Scope.ValidationBoundaryUtc, "LeakProbe");
                return Task.CompletedTask;
            });

        Assert.True(executionResult.FlushAttempted);
        var flushCountBeforeHandler = recorder.FlushCallCount;
        var leakage = Assert.IsType<ValidationDataLeakageException>(executionResult.BodyException!.SourceException);

        await handler.HandleBoundaryFailureAsync(
            experiment,
            trial,
            factory.Scope,
            leakage,
            observedFailures: executionResult.ToFailureAggregate(),
            scopeFlushAlreadyAttempted: executionResult.FlushAttempted);

        Assert.Equal(flushCountBeforeHandler, recorder.FlushCallCount);
        Assert.Equal(ValidationTrainingFailureCodes.ValidationDataLeakage, experiment.PrimaryFailureReason);
    }

    [Fact]
    public async Task BoundScopeFlushFailure_NeverUsesLegacyRecorder()
    {
        var audits = new TrackingCandleAccessAuditRepository();
        var executions = new InMemoryAuditExecutionRepository();
        var batches = new FailingManifestAuditBatchRepository();
        var scopeExecutionId = Guid.NewGuid();
        var auditExecutionId = Guid.NewGuid();
        var execution = new ValidationAuditExecution
        {
            AuditExecutionId = auditExecutionId,
            ValidationExperimentId = 42,
            ValidationTrialId = 1,
            TrialNumber = 1,
            ScopeExecutionId = scopeExecutionId,
            AttemptNumber = 1,
            ExecutionToken = "token-e2c2",
            Status = ValidationAuditExecutionStatus.InProgress,
            StartedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            AuditContractVersion = ValidationAuditExecution.ContractVersionV1,
            RowVersion = 1
        };
        await executions.AddAsync(execution);

        var trials = new InMemoryTrialRepository();
        await trials.AddAsync(new ValidationParameterTrial
        {
            Id = 1,
            ValidationExperimentId = 42,
            TrialNumber = 1,
            ParameterFingerprint = "fp",
            AuthoritativeAuditExecutionId = auditExecutionId,
            Status = ValidationTrialStatus.Running
        });

        var recorder = new ValidationCandleAccessRecorder(
            audits,
            canonicalizer: new ValidationAccessPayloadCanonicalizer(),
            executions,
            batches,
            uow: new NoOpAuditUnitOfWork(),
            hasher: new ValidationAuditPayloadSetHasher(),
            trials);

        var factory = new BoundScopeFactory(scopeExecutionId, auditExecutionId);
        var scopeExecution = new ValidationTrainingScopeExecution(factory, recorder);
        var experiment = new ValidationExperiment
        {
            Id = 42,
            TrainingStartUtc = factory.Scope.SegmentStartUtc,
            TrainingEndUtc = factory.Scope.ValidationBoundaryUtc.AddHours(-1),
            ValidationStartUtc = factory.Scope.ValidationBoundaryUtc
        };

        var request = new ValidationTrainingCandleScopeRequest
        {
            ValidationExperimentId = 42,
            SymbolId = 1,
            SymbolName = "BTCUSDT",
            Timeframe = "15m",
            TrainingEvaluationStartUtc = factory.Scope.SegmentStartUtc,
            TrainingEvaluationEndExclusiveUtc = factory.Scope.SegmentEndExclusiveUtc,
            ValidationBoundaryUtc = factory.Scope.ValidationBoundaryUtc,
            RequiredWarmupCandleCount = 0,
            RequirementsVersion = "test",
            StrategyId = 1,
            StrategyCode = "PSBR",
            StrategyVersion = "1.0.0",
            ExchangeId = 1,
            BoundScopeExecutionId = scopeExecutionId,
            BoundAuditExecutionId = auditExecutionId,
            BoundExecutionToken = "token-e2c2",
            BoundAttemptNumber = 1
        };

        var result = await scopeExecution.ExecuteWithScopeAsync(
            experiment,
            request,
            scope =>
            {
                scope.ActiveTrialId = 1;
                _ = scope.GetRange(scope.SegmentStartUtc, scope.SegmentStartUtc.AddHours(1), "BoundAccess");
                return Task.CompletedTask;
            });

        Assert.False(result.IsSuccess);
        Assert.IsType<ValidationAccessEvidencePersistenceException>(result.FlushException!.SourceException);
        Assert.Equal(1, batches.ManifestCreateCalls);
        Assert.Equal(0, audits.LegacyPersistCalls);
    }

    private static ValidationTrainingCandleScope CreateScope()
    {
        var boundary = new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var start = boundary.AddDays(-2);
        var candles = new List<Candle>
        {
            new()
            {
                OpenTimeUtc = start,
                CloseTimeUtc = start.AddHours(1),
                Open = 1,
                High = 1,
                Low = 1,
                Close = 1,
                Volume = 1
            },
            new()
            {
                OpenTimeUtc = boundary.AddHours(-1),
                CloseTimeUtc = boundary,
                Open = 1,
                High = 1,
                Low = 1,
                Close = 1,
                Volume = 1
            }
        };
        return new ValidationTrainingCandleScope(42, start, boundary, candles);
    }

    private sealed class FakeScopeFactory : IValidationTrainingCandleScopeFactory
    {
        public ValidationTrainingCandleScope Scope { get; } = CreateScope();

        public Task<IValidationTrainingCandleScope> CreateAsync(
            ValidationTrainingCandleScopeRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IValidationTrainingCandleScope>(Scope);

#pragma warning disable CS0618
        public Task<IValidationTrainingCandleScope> CreateForExperimentAsync(
            ValidationExperiment experiment,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IValidationTrainingCandleScope>(Scope);
#pragma warning restore CS0618
    }

    private sealed class FlushFailingRecorder : IValidationCandleAccessRecorder
    {
        private readonly IValidationCandleAccessRecorder _inner;

        public FlushFailingRecorder(IValidationCandleAccessRecorder inner) => _inner = inner;

        public async Task<ValidationAccessBatchPersistResult> FlushAsync(
            IValidationTrainingCandleScope scope,
            CancellationToken cancellationToken = default)
        {
            _ = await _inner.FlushAsync(scope, cancellationToken);
            var eventId = scope.AccessLog.LastOrDefault()?.AccessEventId ?? Guid.NewGuid();
            throw new ValidationAccessEvidencePersistenceException(new ValidationAccessBatchPersistResult
            {
                RequestedEventIds = [eventId],
                MissingEventIds = [eventId],
                CommitStatus = ValidationAccessBatchCommitStatus.FailedPermanent,
                VerificationStatus = ValidationAccessBatchVerificationStatus.FailedPermanent,
                RecoveryStatus = ValidationAccessBatchRecoveryStatus.RetryExhausted,
                CompletedAtUtc = DateTime.UtcNow
            });
        }
    }

    private sealed class CountingRecorder : IValidationCandleAccessRecorder
    {
        private readonly IValidationCandleAccessRecorder _inner;

        public CountingRecorder(IValidationCandleAccessRecorder inner) => _inner = inner;

        public int FlushCallCount { get; private set; }

        public async Task<ValidationAccessBatchPersistResult> FlushAsync(
            IValidationTrainingCandleScope scope,
            CancellationToken cancellationToken = default)
        {
            FlushCallCount++;
            return await _inner.FlushAsync(scope, cancellationToken);
        }
    }

    private sealed class FakeCandleAccessAuditRepository : IValidationCandleAccessAuditRepository
    {
        public List<ValidationCandleAccessAudit> Items { get; } = [];

        public async Task AddRangeAsync(
            IReadOnlyList<ValidationCandleAccessAudit> audits,
            CancellationToken cancellationToken = default) =>
            await AddRangeIdempotentByAccessEventIdAsync(audits, cancellationToken);

        public Task<ValidationAccessBatchPersistResult> AddRangeIdempotentByAccessEventIdAsync(
            IReadOnlyList<ValidationCandleAccessAudit> audits,
            CancellationToken cancellationToken = default)
        {
            var existing = Items.Select(i => i.AccessEventId).ToHashSet();
            var distinct = audits.GroupBy(a => a.AccessEventId).Select(g => g.First()).ToList();
            var requested = distinct.Select(a => a.AccessEventId).ToList();
            var newly = new List<Guid>();
            foreach (var a in distinct)
            {
                if (!existing.Contains(a.AccessEventId))
                {
                    Items.Add(a);
                    newly.Add(a.AccessEventId);
                }
            }

            var canonicalizer = new ValidationAccessPayloadCanonicalizer();
            return Task.FromResult(new ValidationAccessBatchPersistResult
            {
                RequestedEventIds = requested,
                NewlyInsertedEventIds = newly,
                AttemptedEventIds = newly,
                ConfirmedMatchingEventIds = requested,
                ConfirmedPayloadHashes = distinct.ToDictionary(
                    a => a.AccessEventId,
                    a => a.AccessPayloadHash ?? canonicalizer.ComputeSha256(a)),
                CommitStatus = ValidationAccessBatchCommitStatus.CommitSucceeded,
                VerificationStatus = ValidationAccessBatchVerificationStatus.FullyPayloadConfirmed,
                RecoveryStatus = ValidationAccessBatchRecoveryStatus.ConfirmedAfterNormalCommit,
                PersistenceAttemptCount = 1,
                ConfirmationAttemptCount = 1,
                CompletedAtUtc = DateTime.UtcNow
            });
        }

        public Task<IReadOnlyList<ValidationCandleAccessAudit>> GetByExperimentIdAsync(
            long experimentId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ValidationCandleAccessAudit>>(
                Items.Where(a => a.ValidationExperimentId == experimentId).ToList());
    }

    private sealed class TrackingCandleAccessAuditRepository : IValidationCandleAccessAuditRepository
    {
        private readonly FakeCandleAccessAuditRepository _inner = new();

        public int LegacyPersistCalls { get; private set; }

        public Task AddRangeAsync(
            IReadOnlyList<ValidationCandleAccessAudit> audits,
            CancellationToken cancellationToken = default) =>
            _inner.AddRangeAsync(audits, cancellationToken);

        public async Task<ValidationAccessBatchPersistResult> AddRangeIdempotentByAccessEventIdAsync(
            IReadOnlyList<ValidationCandleAccessAudit> audits,
            CancellationToken cancellationToken = default)
        {
            LegacyPersistCalls++;
            return await _inner.AddRangeIdempotentByAccessEventIdAsync(audits, cancellationToken);
        }

        public Task<IReadOnlyList<ValidationCandleAccessAudit>> GetByExperimentIdAsync(
            long experimentId,
            CancellationToken cancellationToken = default) =>
            _inner.GetByExperimentIdAsync(experimentId, cancellationToken);
    }

    private sealed class BoundScopeFactory : IValidationTrainingCandleScopeFactory
    {
        public BoundScopeFactory(Guid scopeExecutionId, Guid auditExecutionId)
        {
            Scope = new BoundTestScope(scopeExecutionId, auditExecutionId);
        }

        public IValidationTrainingCandleScope Scope { get; }

        public Task<IValidationTrainingCandleScope> CreateAsync(
            ValidationTrainingCandleScopeRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Scope);

#pragma warning disable CS0618
        public Task<IValidationTrainingCandleScope> CreateForExperimentAsync(
            ValidationExperiment experiment,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Scope);
#pragma warning restore CS0618
    }

    private sealed class BoundTestScope : IValidationTrainingCandleScope
    {
        private readonly ValidationTrainingCandleScope _inner;

        public BoundTestScope(Guid scopeExecutionId, Guid boundAuditExecutionId)
        {
            ScopeExecutionId = scopeExecutionId;
            BoundAuditExecutionId = boundAuditExecutionId;
            var boundary = new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc);
            var start = boundary.AddDays(-2);
            var candles = new List<Candle>
            {
                new()
                {
                    OpenTimeUtc = start,
                    CloseTimeUtc = start.AddHours(1),
                    Open = 1,
                    High = 1,
                    Low = 1,
                    Close = 1,
                    Volume = 1
                }
            };
            _inner = new ValidationTrainingCandleScope(42, start, boundary, candles, scopeExecutionId);
        }

        public Guid ScopeExecutionId { get; }
        public Guid? BoundAuditExecutionId { get; }
        public string? CorrelationId { get => _inner.CorrelationId; set => _inner.CorrelationId = value; }
        public long? ActiveTrialId { get => _inner.ActiveTrialId; set => _inner.ActiveTrialId = value; }
        public int? ActiveTrialNumber { get => _inner.ActiveTrialNumber; set => _inner.ActiveTrialNumber = value; }
        public IReadOnlyList<ValidationCandleAccessRecord> AccessLog => _inner.AccessLog;
        public long ValidationExperimentId => _inner.ValidationExperimentId;
        public DateTime SegmentStartUtc => _inner.SegmentStartUtc;
        public DateTime SegmentEndExclusiveUtc => _inner.SegmentEndExclusiveUtc;
        public DateTime ValidationBoundaryUtc => _inner.ValidationBoundaryUtc;
        public ValidationCandlePartitionMetadata Partition => _inner.Partition;

        public IReadOnlyList<Candle> GetWarmupBefore(
            DateTime beforeOpenTimeUtc,
            int count,
            ValidationCandleAccessContext context) =>
            _inner.GetWarmupBefore(beforeOpenTimeUtc, count, context);

        public IReadOnlyList<Candle> GetWarmupBefore(ValidationWarmupAccessRequest request) =>
            _inner.GetWarmupBefore(request);

        public IReadOnlyList<Candle> GetEvaluationRange(
            DateTime? fromUtc,
            DateTime? toUtcExclusive,
            ValidationCandleAccessContext context) =>
            _inner.GetEvaluationRange(fromUtc, toUtcExclusive, context);

        public IReadOnlyList<Candle> GetEvaluationRange(ValidationEvaluationAccessRequest request) =>
            _inner.GetEvaluationRange(request);

        public Candle? GetByOpenTimeUtc(DateTime openTimeUtc, ValidationCandleAccessContext context) =>
            _inner.GetByOpenTimeUtc(openTimeUtc, context);

        public Candle? GetByOpenTimeUtc(DateTime openTimeUtc, string callerComponent) =>
            _inner.GetByOpenTimeUtc(openTimeUtc, callerComponent);

        public IReadOnlyList<Candle> GetRange(DateTime? fromUtc, DateTime? toUtcExclusive, string callerComponent) =>
            _inner.GetRange(fromUtc, toUtcExclusive, callerComponent);

        public StrategyLabDataset CreateStrategyLabDataset(ValidationDatasetMaterializationRequest request) =>
            _inner.CreateStrategyLabDataset(request);

        public ValueTask DisposeAsync() => _inner.DisposeAsync();
    }

    private sealed class NoOpAuditUnitOfWork : IValidationAuditUnitOfWork
    {
        public Task ExecuteInTransactionAsync(Func<Task> action, CancellationToken cancellationToken = default) =>
            action();
    }

    private sealed class InMemoryTrialRepository : IValidationParameterTrialRepository
    {
        private readonly List<ValidationParameterTrial> _items = [];

        public Task<ValidationParameterTrial?> GetByExperimentAndFingerprintAsync(
            long experimentId,
            string fingerprint,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_items.FirstOrDefault(t =>
                t.ValidationExperimentId == experimentId && t.ParameterFingerprint == fingerprint));

        public Task<IReadOnlyList<ValidationParameterTrial>> GetByExperimentIdAsync(
            long experimentId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ValidationParameterTrial>>(
                _items.Where(t => t.ValidationExperimentId == experimentId).ToList());

        public Task AddAsync(ValidationParameterTrial trial, CancellationToken cancellationToken = default)
        {
            _items.Add(trial);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(ValidationParameterTrial trial, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task AddRangeAsync(
            IEnumerable<ValidationParameterTrial> trials,
            CancellationToken cancellationToken = default)
        {
            _items.AddRange(trials);
            return Task.CompletedTask;
        }
    }

    private sealed class FailingManifestAuditBatchRepository : IValidationAuditBatchRepository
    {
        public int ManifestCreateCalls { get; private set; }

        public Task<ValidationAuditBatch?> GetByAuditBatchIdAsync(Guid auditBatchId, CancellationToken cancellationToken = default) =>
            Task.FromResult<ValidationAuditBatch?>(null);

        public Task<IReadOnlyList<ValidationAuditBatch>> GetByAuditExecutionIdAsync(
            Guid auditExecutionId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ValidationAuditBatch>>([]);

        public Task AddAsync(ValidationAuditBatch batch, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task UpdateAsync(ValidationAuditBatch batch, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<ValidationAuditBatch> GetOrCreateManifestAsync(
            ValidationAuditBatch proposed,
            CancellationToken cancellationToken = default)
        {
            ManifestCreateCalls++;
            throw new ValidationAccessEvidencePersistenceException(new ValidationAccessBatchPersistResult
            {
                RequestedEventIds = [],
                MissingEventIds = [],
                CommitStatus = ValidationAccessBatchCommitStatus.FailedPermanent,
                VerificationStatus = ValidationAccessBatchVerificationStatus.FailedPermanent,
                RecoveryStatus = ValidationAccessBatchRecoveryStatus.RetryExhausted,
                CompletedAtUtc = DateTime.UtcNow
            });
        }
    }

    private sealed class InMemoryAuditExecutionRepository : IValidationAuditExecutionRepository
    {
        private readonly Dictionary<Guid, ValidationAuditExecution> _items = new();

        public Task<ValidationAuditExecution?> GetByAuditExecutionIdAsync(
            Guid auditExecutionId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_items.GetValueOrDefault(auditExecutionId));

        public Task<IReadOnlyList<ValidationAuditExecution>> GetActiveByTrialIdAsync(
            long validationTrialId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ValidationAuditExecution>>(
                _items.Values.Where(e => e.ValidationTrialId == validationTrialId).ToList());

        public Task<IReadOnlyList<ValidationAuditExecution>> GetByTrialIdAsync(
            long validationTrialId,
            CancellationToken cancellationToken = default) =>
            GetActiveByTrialIdAsync(validationTrialId, cancellationToken);

        public Task<ValidationAuditExecution> CreateAndAssignTrialAuthoritativeAsync(
            ValidationAuditExecution execution,
            ValidationParameterTrial trial,
            CancellationToken cancellationToken = default) =>
            AddAsync(execution, cancellationToken).ContinueWith(_ => execution, cancellationToken);

        public Task AddAsync(ValidationAuditExecution execution, CancellationToken cancellationToken = default)
        {
            _items[execution.AuditExecutionId] = execution;
            return Task.CompletedTask;
        }

        public Task UpdateAsync(ValidationAuditExecution execution, CancellationToken cancellationToken = default)
        {
            _items[execution.AuditExecutionId] = execution;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeTrialRepository : IValidationParameterTrialRepository
    {
        public List<ValidationParameterTrial> Items { get; } = [];

        public Task<ValidationParameterTrial?> GetByExperimentAndFingerprintAsync(
            long experimentId,
            string fingerprint,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.FirstOrDefault(t =>
                t.ValidationExperimentId == experimentId && t.ParameterFingerprint == fingerprint));

        public Task<IReadOnlyList<ValidationParameterTrial>> GetByExperimentIdAsync(
            long experimentId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ValidationParameterTrial>>(
                Items.Where(t => t.ValidationExperimentId == experimentId).ToList());

        public Task AddAsync(ValidationParameterTrial trial, CancellationToken cancellationToken = default)
        {
            Items.Add(trial);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(ValidationParameterTrial trial, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task AddRangeAsync(
            IEnumerable<ValidationParameterTrial> trials,
            CancellationToken cancellationToken = default)
        {
            Items.AddRange(trials);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeExperimentRepository : IValidationExperimentRepository
    {
        public List<ValidationExperiment> Items { get; } = [];

        public Task<ValidationExperiment?> GetByIdAsync(long id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.FirstOrDefault(e => e.Id == id));

        public Task<IReadOnlyList<ValidationExperiment>> GetRecentAsync(
            int limit = 50,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ValidationExperiment>>(Items.Take(limit).ToList());

        public Task<IReadOnlyList<ValidationExperiment>> GetByStrategyFingerprintOverlapAsync(
            string strategyCode,
            string strategyVersion,
            string symbol,
            string timeframe,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ValidationExperiment>>([]);

        public Task AddAsync(ValidationExperiment experiment, CancellationToken cancellationToken = default)
        {
            Items.Add(experiment);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(ValidationExperiment experiment, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class FakeOperationStatusService : IResearchOperationStatusService
    {
        public Task<ResearchOperationStatus?> GetByOperationIdAsync(
            string operationId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<ResearchOperationStatus?>(null);

        public Task<ResearchOperationStatus?> GetForValidationExperimentAsync(
            long experimentId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<ResearchOperationStatus?>(null);

        public Task<ResearchOperationStatus> UpsertValidationTrainingAsync(
            ResearchOperationStatus status,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(status);

        public Task<ResearchOperationStatus> SyncFromValidationTrainingAsync(
            long experimentId,
            string status,
            string stage,
            ValidationTrainingProgressDto progress,
            string? leaseOwner = null,
            string? correlationId = null,
            string? errorCode = null,
            string? userSafeError = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ResearchOperationStatusMapper.FromValidationTraining(
                experimentId, status, stage, progress, leaseOwner, correlationId, errorCode, userSafeError));

        public Task<ServiceResult<ResearchOperationStatus>> AdvanceProgressAsync(
            string operationId,
            decimal percentComplete,
            int completedWorkCount,
            int failedWorkCount,
            string? stage = null,
            string? status = null,
            string? activeWorkItem = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ServiceResult<ResearchOperationStatus>.Fail("n/a"));

        public Task<ServiceResult<ResearchOperationStatus>> HeartbeatAsync(
            string operationId,
            string leaseOwner,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ServiceResult<ResearchOperationStatus>.Fail("n/a"));

        public Task<ResearchOperationStatus?> DetectAndMarkStaleAsync(
            string operationId,
            TimeSpan staleAfter,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<ResearchOperationStatus?>(null);

        public Task<ServiceResult<ResearchOperationStatus>> CancelAsync(
            string operationId,
            string callerIdentity,
            bool callerIsAdmin,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ServiceResult<ResearchOperationStatus>.Fail("n/a"));
    }
}
