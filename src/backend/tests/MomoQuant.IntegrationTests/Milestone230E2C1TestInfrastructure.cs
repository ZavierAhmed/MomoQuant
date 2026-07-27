using System.Data.Common;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.StrategyLab;
using MomoQuant.Application.ValidationLab;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.MarketData;
using MomoQuant.Domain.ValidationLab;
using MomoQuant.Persistence;
using MomoQuant.Persistence.Repositories;

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

public sealed class E2C1DbCommandCounter : DbCommandInterceptor
{
    private int _commandCount;

    public int CommandCount => Volatile.Read(ref _commandCount);

    public void Reset() => Interlocked.Exchange(ref _commandCount, 0);

    private void Increment() => Interlocked.Increment(ref _commandCount);

    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result)
    {
        Increment();
        return base.NonQueryExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Increment();
        return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override InterceptionResult<object> ScalarExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result)
    {
        Increment();
        return base.ScalarExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result,
        CancellationToken cancellationToken = default)
    {
        Increment();
        return base.ScalarExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result)
    {
        Increment();
        return base.ReaderExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        Increment();
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }
}

public sealed class E2C1AuditWriteCounters
{
    public int ExecutionCreates { get; set; }
    public int ExecutionUpdates { get; set; }
    public int ManifestCreates { get; set; }
    public int ManifestUpdates { get; set; }
    public int AccessEventPersistCalls { get; set; }
    public int AccessRowsPersisted { get; set; }
    public int ConfirmationReadCalls { get; set; }
    public int FinalizationCalls { get; set; }
}

internal sealed class CountingAuditExecutionRepository : IValidationAuditExecutionRepository
{
    private readonly IValidationAuditExecutionRepository _inner;
    private readonly E2C1AuditWriteCounters _counters;

    public CountingAuditExecutionRepository(IValidationAuditExecutionRepository inner, E2C1AuditWriteCounters counters)
    {
        _inner = inner;
        _counters = counters;
    }

    public Task<ValidationAuditExecution?> GetByAuditExecutionIdAsync(Guid auditExecutionId, CancellationToken cancellationToken = default) =>
        _inner.GetByAuditExecutionIdAsync(auditExecutionId, cancellationToken);

    public Task<IReadOnlyList<ValidationAuditExecution>> GetActiveByTrialIdAsync(long validationTrialId, CancellationToken cancellationToken = default) =>
        _inner.GetActiveByTrialIdAsync(validationTrialId, cancellationToken);

    public Task<IReadOnlyList<ValidationAuditExecution>> GetByTrialIdAsync(long validationTrialId, CancellationToken cancellationToken = default) =>
        _inner.GetByTrialIdAsync(validationTrialId, cancellationToken);

    public Task<ValidationAuditExecution> CreateAndAssignTrialAuthoritativeAsync(
        ValidationAuditExecution execution,
        ValidationParameterTrial trial,
        CancellationToken cancellationToken = default)
    {
        _counters.ExecutionCreates++;
        return _inner.CreateAndAssignTrialAuthoritativeAsync(execution, trial, cancellationToken);
    }

    public Task AddAsync(ValidationAuditExecution execution, CancellationToken cancellationToken = default)
    {
        _counters.ExecutionCreates++;
        return _inner.AddAsync(execution, cancellationToken);
    }

    public Task UpdateAsync(ValidationAuditExecution execution, CancellationToken cancellationToken = default)
    {
        _counters.ExecutionUpdates++;
        return _inner.UpdateAsync(execution, cancellationToken);
    }
}

internal sealed class CountingAuditBatchRepository : IValidationAuditBatchRepository
{
    private readonly IValidationAuditBatchRepository _inner;
    private readonly E2C1AuditWriteCounters _counters;

    public CountingAuditBatchRepository(IValidationAuditBatchRepository inner, E2C1AuditWriteCounters counters)
    {
        _inner = inner;
        _counters = counters;
    }

    public Task<ValidationAuditBatch?> GetByAuditBatchIdAsync(Guid auditBatchId, CancellationToken cancellationToken = default) =>
        _inner.GetByAuditBatchIdAsync(auditBatchId, cancellationToken);

    public Task<IReadOnlyList<ValidationAuditBatch>> GetByAuditExecutionIdAsync(Guid auditExecutionId, CancellationToken cancellationToken = default) =>
        _inner.GetByAuditExecutionIdAsync(auditExecutionId, cancellationToken);

    public Task AddAsync(ValidationAuditBatch batch, CancellationToken cancellationToken = default)
    {
        _counters.ManifestCreates++;
        return _inner.AddAsync(batch, cancellationToken);
    }

    public Task UpdateAsync(ValidationAuditBatch batch, CancellationToken cancellationToken = default)
    {
        _counters.ManifestUpdates++;
        return _inner.UpdateAsync(batch, cancellationToken);
    }

    public async Task<ValidationAuditBatch> GetOrCreateManifestAsync(ValidationAuditBatch proposed, CancellationToken cancellationToken = default)
    {
        _counters.ManifestCreates++;
        return await _inner.GetOrCreateManifestAsync(proposed, cancellationToken);
    }
}

internal sealed class CountingAccessAuditRepository : IValidationCandleAccessAuditRepository
{
    private readonly IValidationCandleAccessAuditRepository _inner;
    private readonly E2C1AuditWriteCounters _counters;

    public CountingAccessAuditRepository(IValidationCandleAccessAuditRepository inner, E2C1AuditWriteCounters counters)
    {
        _inner = inner;
        _counters = counters;
    }

    public Task AddRangeAsync(IReadOnlyList<ValidationCandleAccessAudit> audits, CancellationToken cancellationToken = default) =>
        _inner.AddRangeAsync(audits, cancellationToken);

    public async Task<ValidationAccessBatchPersistResult> AddRangeIdempotentByAccessEventIdAsync(
        IReadOnlyList<ValidationCandleAccessAudit> audits,
        CancellationToken cancellationToken = default)
    {
        _counters.AccessEventPersistCalls++;
        _counters.AccessRowsPersisted += audits.Count;
        return await _inner.AddRangeIdempotentByAccessEventIdAsync(audits, cancellationToken);
    }

    public Task<IReadOnlyList<ValidationCandleAccessAudit>> GetByExperimentIdAsync(long experimentId, CancellationToken cancellationToken = default) =>
        _inner.GetByExperimentIdAsync(experimentId, cancellationToken);
}

internal sealed class CountingAuditFinalizer : IValidationAuditExecutionFinalizer
{
    private readonly IValidationAuditExecutionFinalizer _inner;
    private readonly E2C1AuditWriteCounters _counters;

    public CountingAuditFinalizer(IValidationAuditExecutionFinalizer inner, E2C1AuditWriteCounters counters)
    {
        _inner = inner;
        _counters = counters;
    }

    public Task<ValidationAuditExecutionCompletionResult> CompleteAsync(
        Guid auditExecutionId,
        long finalExpectedSequence,
        CancellationToken cancellationToken = default)
    {
        _counters.FinalizationCalls++;
        return _inner.CompleteAsync(auditExecutionId, finalExpectedSequence, cancellationToken);
    }
}

internal sealed class CountingConfirmationReader : IValidationAccessAuditConfirmationReader
{
    private readonly IValidationAccessAuditConfirmationReader _inner;
    private readonly E2C1AuditWriteCounters _counters;

    public CountingConfirmationReader(
        IValidationAccessAuditConfirmationReader inner,
        E2C1AuditWriteCounters counters)
    {
        _inner = inner;
        _counters = counters;
    }

    public bool UsesFreshContext => _inner.UsesFreshContext;

    public async Task<IReadOnlyList<ValidationCandleAccessAudit>> ReadAsync(
        IReadOnlyCollection<Guid> accessEventIds,
        CancellationToken cancellationToken)
    {
        _counters.ConfirmationReadCalls++;
        return await _inner.ReadAsync(accessEventIds, cancellationToken);
    }
}

public sealed class E2C1InstrumentationFactory : MomoQuantWebApplicationFactory
{
    public E2C1AuditWriteCounters Counters { get; } = new();
    public E2C1DbCommandCounter DbCommandCounter { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton(Counters);
            services.AddSingleton(DbCommandCounter);

            services.RemoveAll<DbContextOptions<MomoQuantDbContext>>();
            services.AddDbContext<MomoQuantDbContext>((sp, options) =>
            {
                var configuration = sp.GetRequiredService<IConfiguration>();
                var connectionString = configuration.GetConnectionString("DefaultConnection")
                    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' was not found.");
                options.UseMySql(
                        connectionString,
                        ServerVersion.Parse(PersistenceConstants.MySqlServerVersion))
                    .AddInterceptors(sp.GetRequiredService<E2C1DbCommandCounter>());
            });

            services.RemoveAll<IValidationAuditExecutionRepository>();
            services.AddScoped<IValidationAuditExecutionRepository>(sp =>
                new CountingAuditExecutionRepository(
                    ActivatorUtilities.CreateInstance<ValidationAuditExecutionRepository>(sp),
                    sp.GetRequiredService<E2C1AuditWriteCounters>()));

            services.RemoveAll<IValidationAuditBatchRepository>();
            services.AddScoped<IValidationAuditBatchRepository>(sp =>
                new CountingAuditBatchRepository(
                    ActivatorUtilities.CreateInstance<ValidationAuditBatchRepository>(sp),
                    sp.GetRequiredService<E2C1AuditWriteCounters>()));

            services.RemoveAll<IValidationCandleAccessAuditRepository>();
            services.AddScoped<IValidationCandleAccessAuditRepository>(sp =>
                new CountingAccessAuditRepository(
                    ActivatorUtilities.CreateInstance<ValidationCandleAccessAuditRepository>(sp),
                    sp.GetRequiredService<E2C1AuditWriteCounters>()));

            services.RemoveAll<IValidationAccessAuditConfirmationReader>();
            services.AddScoped<IValidationAccessAuditConfirmationReader>(sp =>
                new CountingConfirmationReader(
                    ActivatorUtilities.CreateInstance<ValidationAccessAuditConfirmationReader>(sp),
                    sp.GetRequiredService<E2C1AuditWriteCounters>()));

            services.RemoveAll<IValidationAuditExecutionFinalizer>();
            services.AddScoped<IValidationAuditExecutionFinalizer>(sp =>
                new CountingAuditFinalizer(
                    ActivatorUtilities.CreateInstance<ValidationAuditExecutionFinalizer>(sp),
                    sp.GetRequiredService<E2C1AuditWriteCounters>()));
        });
    }
}
