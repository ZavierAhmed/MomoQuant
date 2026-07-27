using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.StrategyLab;
using MomoQuant.Application.ValidationLab;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.MarketData;
using MomoQuant.Domain.ValidationLab;
using MomoQuant.Persistence;

namespace MomoQuant.IntegrationTests;

/// <summary>Shared experiment/trial/audit fixtures for Milestone 23.0E2C1 MySQL tests.</summary>
internal static class E2C1AuditFixtures
{
    public static async Task<(ValidationExperiment Experiment, ValidationParameterTrial Trial)> CreateExperimentAndTrialAsync(
        MomoQuantDbContext db,
        string nameSuffix)
    {
        var now = DateTime.UtcNow;
        var rawName = $"E2C1-{nameSuffix}-{Guid.NewGuid():N}";
        var experiment = new ValidationExperiment
        {
            Name = rawName.Length <= 300 ? rawName : rawName[..300],
            ExperimentType = ValidationExperimentType.ValidateExistingFrozenConfiguration,
            Status = ValidationExperimentStatus.Draft,
            StrategyCode = "PSBR",
            StrategyVersion = "1.0.0",
            ExchangeId = 1,
            Exchange = "binance",
            SymbolId = 1,
            Symbol = "BTCUSDT",
            Timeframe = "15m",
            RequestedStartUtc = now.AddDays(-10),
            RequestedEndUtc = now,
            SplitRatio = 0.7m,
            CandleDataSnapshotJson = "{}",
            CandleDataFingerprint = "e2c1fix",
            WarmupSnapshotJson = "{}",
            ParameterSearchSpaceSnapshotJson = "{}",
            OptimizationObjectiveSnapshotJson = "{}",
            QualificationProfileSnapshotJson = "{}",
            DraftConfigurationJson = "{}",
            DiagnosticsJson = "[]",
            OverlayResultsJson = "{}",
            ComparisonJson = "{}",
            RegimeComparisonJson = "{}",
            ParameterStabilityJson = "{}",
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RiskBasisVersion = "ValidationRiskBasis/v1",
            ParameterFingerprintVersion = "ValidationParameterFingerprint/v1"
        };
        db.ValidationExperiments.Add(experiment);
        await db.SaveChangesAsync();

        var trial = new ValidationParameterTrial
        {
            ValidationExperimentId = experiment.Id,
            TrialNumber = 1,
            ParameterSnapshotJson = "{}",
            ParameterFingerprint = $"e2c1-{Guid.NewGuid():N}"[..32],
            Status = ValidationTrialStatus.Running,
            StartedAtUtc = now,
            AuditCompletionStatus = ValidationAuditCompletionStatus.NotEvaluated
        };
        db.ValidationParameterTrials.Add(trial);
        await db.SaveChangesAsync();
        return (experiment, trial);
    }

    public static ValidationAuditExecution NewExecution(
        ValidationExperiment experiment,
        ValidationParameterTrial trial,
        Guid? auditExecutionId = null,
        Guid? scopeExecutionId = null,
        int attempt = 1) =>
        new()
        {
            AuditExecutionId = auditExecutionId ?? Guid.NewGuid(),
            ValidationExperimentId = experiment.Id,
            ValidationTrialId = trial.Id,
            TrialNumber = trial.TrialNumber,
            ScopeExecutionId = scopeExecutionId ?? Guid.NewGuid(),
            AttemptNumber = attempt,
            ExecutionToken = Guid.NewGuid().ToString("N"),
            Status = ValidationAuditExecutionStatus.InProgress,
            StartedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            RecoveryStatus = ValidationAuditRecoveryStatus.None,
            LastConfirmedSequence = 0,
            ConfirmedEventCount = 0,
            AuditContractVersion = ValidationAuditExecution.ContractVersionV1,
            RowVersion = 1
        };

    public static async Task CleanupAsync(MomoQuantWebApplicationFactory factory, long experimentId)
    {
        await using var cleanup = factory.Services.CreateAsyncScope();
        var db = cleanup.ServiceProvider.GetRequiredService<MomoQuantDbContext>();

        var auditIds = await db.ValidationAuditExecutions
            .Where(e => e.ValidationExperimentId == experimentId)
            .Select(e => e.AuditExecutionId)
            .ToListAsync();

        if (auditIds.Count > 0)
        {
            await db.ValidationAuditBatches
                .Where(b => auditIds.Contains(b.AuditExecutionId))
                .ExecuteDeleteAsync();
        }

        await db.ValidationAuditExecutions
            .Where(e => e.ValidationExperimentId == experimentId)
            .ExecuteDeleteAsync();
        await db.ValidationCandleAccessAudits
            .Where(a => a.ValidationExperimentId == experimentId)
            .ExecuteDeleteAsync();
        await db.ValidationParameterTrials
            .Where(t => t.ValidationExperimentId == experimentId)
            .ExecuteDeleteAsync();
        await db.ValidationExperiments
            .Where(e => e.Id == experimentId)
            .ExecuteDeleteAsync();
    }
}

/// <summary>
/// Counts manifest-before-event order for FlushCreatesManifestBeforeEventPersistence.
/// </summary>
internal sealed class OrderingAuditBatchRepository : IValidationAuditBatchRepository
{
    private readonly IValidationAuditBatchRepository _inner;

    public OrderingAuditBatchRepository(IValidationAuditBatchRepository inner) => _inner = inner;

    public int ManifestCreateCalls { get; private set; }
    public int EventPersistObservedAfterManifest { get; set; }
    public List<string> CallOrder { get; } = new();

    public Task<ValidationAuditBatch?> GetByAuditBatchIdAsync(Guid auditBatchId, CancellationToken cancellationToken = default) =>
        _inner.GetByAuditBatchIdAsync(auditBatchId, cancellationToken);

    public Task<IReadOnlyList<ValidationAuditBatch>> GetByAuditExecutionIdAsync(Guid auditExecutionId, CancellationToken cancellationToken = default) =>
        _inner.GetByAuditExecutionIdAsync(auditExecutionId, cancellationToken);

    public Task AddAsync(ValidationAuditBatch batch, CancellationToken cancellationToken = default)
    {
        CallOrder.Add("AddAsync");
        return _inner.AddAsync(batch, cancellationToken);
    }

    public Task UpdateAsync(ValidationAuditBatch batch, CancellationToken cancellationToken = default) =>
        _inner.UpdateAsync(batch, cancellationToken);

    public async Task<ValidationAuditBatch> GetOrCreateManifestAsync(
        ValidationAuditBatch proposed,
        CancellationToken cancellationToken = default)
    {
        ManifestCreateCalls++;
        CallOrder.Add("GetOrCreateManifestAsync");
        return await _inner.GetOrCreateManifestAsync(proposed, cancellationToken);
    }
}

internal sealed class OrderingAccessAuditRepository : IValidationCandleAccessAuditRepository
{
    private readonly IValidationCandleAccessAuditRepository _inner;
    private readonly OrderingAuditBatchRepository _batches;

    public OrderingAccessAuditRepository(
        IValidationCandleAccessAuditRepository inner,
        OrderingAuditBatchRepository batches)
    {
        _inner = inner;
        _batches = batches;
    }

    public Task AddRangeAsync(
        IReadOnlyList<ValidationCandleAccessAudit> audits,
        CancellationToken cancellationToken = default) =>
        _inner.AddRangeAsync(audits, cancellationToken);

    public async Task<ValidationAccessBatchPersistResult> AddRangeIdempotentByAccessEventIdAsync(
        IReadOnlyList<ValidationCandleAccessAudit> audits,
        CancellationToken cancellationToken = default)
    {
        _batches.CallOrder.Add("AddRangeIdempotentByAccessEventIdAsync");
        if (_batches.ManifestCreateCalls > 0)
        {
            _batches.EventPersistObservedAfterManifest++;
        }

        return await _inner.AddRangeIdempotentByAccessEventIdAsync(audits, cancellationToken);
    }

    public Task<IReadOnlyList<ValidationCandleAccessAudit>> GetByExperimentIdAsync(
        long experimentId,
        CancellationToken cancellationToken = default) =>
        _inner.GetByExperimentIdAsync(experimentId, cancellationToken);
}

/// <summary>Minimal scope with injectable access log for flush-order tests.</summary>
internal sealed class FakeTrainingCandleScope : IValidationTrainingCandleScope
{
    private readonly List<ValidationCandleAccessRecord> _log;

    public FakeTrainingCandleScope(
        long experimentId,
        Guid scopeExecutionId,
        long? trialId,
        IEnumerable<ValidationCandleAccessRecord> records,
        Guid? boundAuditExecutionId = null)
    {
        ValidationExperimentId = experimentId;
        ScopeExecutionId = scopeExecutionId;
        BoundAuditExecutionId = boundAuditExecutionId;
        ActiveTrialId = trialId;
        _log = records.ToList();
        var now = DateTime.UtcNow;
        SegmentStartUtc = now.AddDays(-5);
        SegmentEndExclusiveUtc = now;
        ValidationBoundaryUtc = now;
        Partition = new ValidationCandlePartitionMetadata
        {
            ValidationExperimentId = experimentId,
            RequiredWarmupCandleCount = 0,
            AvailableWarmupCandleCount = 0,
            EvaluationCandleCount = 0,
            TotalCandleCount = 0,
            WarmupStatus = ValidationWarmupStatus.NotRequired,
            TrainingEvaluationStartUtc = SegmentStartUtc,
            TrainingEvaluationEndExclusiveUtc = SegmentEndExclusiveUtc,
            ValidationBoundaryUtc = ValidationBoundaryUtc,
            SymbolId = 1,
            SymbolName = "BTCUSDT",
            Timeframe = "15m",
            RequirementsVersion = "test"
        };
    }

    public Guid ScopeExecutionId { get; }
    public Guid? BoundAuditExecutionId { get; }
    public string? CorrelationId { get; set; }
    public long? ActiveTrialId { get; set; }
    public int? ActiveTrialNumber { get; set; }
    public IReadOnlyList<ValidationCandleAccessRecord> AccessLog => _log;
    public long ValidationExperimentId { get; }
    public DateTime SegmentStartUtc { get; }
    public DateTime SegmentEndExclusiveUtc { get; }
    public DateTime ValidationBoundaryUtc { get; }
    public ValidationCandlePartitionMetadata Partition { get; }

    public IReadOnlyList<Candle> GetWarmupBefore(DateTime beforeOpenTimeUtc, int count, ValidationCandleAccessContext context) => [];
    public IReadOnlyList<Candle> GetWarmupBefore(ValidationWarmupAccessRequest request) => [];
    public IReadOnlyList<Candle> GetEvaluationRange(DateTime? fromUtc, DateTime? toUtcExclusive, ValidationCandleAccessContext context) => [];
    public IReadOnlyList<Candle> GetEvaluationRange(ValidationEvaluationAccessRequest request) => [];
    public Candle? GetByOpenTimeUtc(DateTime openTimeUtc, ValidationCandleAccessContext context) => null;
    public Candle? GetByOpenTimeUtc(DateTime openTimeUtc, string callerComponent) => null;
    public IReadOnlyList<Candle> GetRange(DateTime? fromUtc, DateTime? toUtcExclusive, string callerComponent) => [];
    public StrategyLabDataset CreateStrategyLabDataset(ValidationDatasetMaterializationRequest request) =>
        throw new NotSupportedException();
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
