using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Research;
using MomoQuant.Application.StrategyLab;
using MomoQuant.Application.ValidationLab;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.MarketData;

namespace MomoQuant.Persistence.Repositories;

/// <summary>
/// Decorates candle reads: when a Validation Laboratory training scope is ambient,
/// range/index access is enforced and recorded; prohibited access throws
/// <see cref="ValidationDataLeakageException"/> or
/// <see cref="ValidationTrainingUnscopedAccessException"/>.
/// </summary>
public sealed class TrainingBoundaryCandleRepository : ICandleRepository, IUnscopedCandleReader
{
    private readonly CandleRepository _inner;
    private readonly IResearchExecutionContextAccessor? _executionContextAccessor;

    public TrainingBoundaryCandleRepository(
        CandleRepository inner,
        IResearchExecutionContextAccessor? executionContextAccessor = null)
    {
        _inner = inner;
        _executionContextAccessor = executionContextAccessor;
    }

    public Task<IReadOnlyList<Candle>> GetCandlesChronologicalUnscopedAsync(
        long symbolId,
        Timeframe timeframe,
        DateTime? fromUtc,
        DateTime? toUtc,
        int warmUpCount = 0,
        CancellationToken cancellationToken = default)
    {
        // Unscoped bootstrap is only legal while the scope-factory capability token is active.
        if (!ValidationScopeFactoryCapability.IsActive)
        {
            var current = _executionContextAccessor?.Current;
            throw new ValidationTrainingUnscopedAccessException(
                current?.ValidationExperimentId,
                current?.TrainingBoundaryUtc,
                nameof(GetCandlesChronologicalUnscopedAsync),
                "Unscoped candle reads require ValidationScopeFactoryCapability.");
        }

        return _inner.GetCandlesChronologicalAsync(symbolId, timeframe, fromUtc, toUtc, warmUpCount, cancellationToken);
    }

    public Task<IReadOnlyList<Candle>> GetClosedCandlesBeforeUnscopedAsync(
        long symbolId,
        Timeframe timeframe,
        DateTime beforeOpenTimeUtc,
        int count,
        CancellationToken cancellationToken = default)
    {
        if (!ValidationScopeFactoryCapability.IsActive)
        {
            var current = _executionContextAccessor?.Current;
            throw new ValidationTrainingUnscopedAccessException(
                current?.ValidationExperimentId,
                current?.TrainingBoundaryUtc,
                nameof(GetClosedCandlesBeforeUnscopedAsync),
                "Unscoped candle reads require ValidationScopeFactoryCapability.");
        }

        return _inner.GetClosedCandlesBeforeAsync(symbolId, timeframe, beforeOpenTimeUtc, count, cancellationToken);
    }

    public async Task<IReadOnlyList<Candle>> GetCandlesAsync(
        long symbolId,
        Timeframe timeframe,
        DateTime? fromUtc,
        DateTime? toUtc,
        int limit,
        CancellationToken cancellationToken = default)
    {
        GuardUnscopedValidationTraining(nameof(GetCandlesAsync));

        if (ValidationTrainingCandleScopeAmbient.Current is { } scope)
        {
            return scope.GetLimitedEvaluationRange(
                CreateRepositoryRequest(
                    nameof(GetCandlesAsync),
                    ValidationCandleAccessPurpose.RepositoryRange,
                    symbolId: symbolId,
                    timeframe: timeframe,
                    requestedStartUtc: fromUtc,
                    requestedEndUtc: toUtc,
                    requestedCount: limit),
                limit);
        }

        return await _inner.GetCandlesAsync(symbolId, timeframe, fromUtc, toUtc, limit, cancellationToken);
    }

    public async Task<IReadOnlyList<Candle>> GetCandlesChronologicalAsync(
        long symbolId,
        Timeframe timeframe,
        DateTime? fromUtc,
        DateTime? toUtc,
        int warmUpCount = 0,
        CancellationToken cancellationToken = default)
    {
        GuardUnscopedValidationTraining(nameof(GetCandlesChronologicalAsync));

        if (ValidationTrainingCandleScopeAmbient.Current is { } scope)
        {
            var from = fromUtc is null ? scope.SegmentStartUtc : NormalizeUtcInstant(fromUtc.Value);
            var to = toUtc is null ? scope.SegmentEndExclusiveUtc : NormalizeUtcInstant(toUtc.Value);

            var repositoryRequest = CreateRepositoryRequest(
                nameof(GetCandlesChronologicalAsync),
                ValidationCandleAccessPurpose.RepositoryRange,
                symbolId: symbolId,
                timeframe: timeframe,
                requestedStartUtc: from,
                requestedEndUtc: to,
                requestedCount: warmUpCount);
            scope.AuthorizeRepositoryAccess(repositoryRequest);

            if (warmUpCount < 0
                || warmUpCount > 0 && warmUpCount != scope.Partition.RequiredWarmupCandleCount)
            {
                scope.DenyRepositoryAccess(
                    repositoryRequest,
                    ValidationCandlePartitionDenialCodes.WarmupCountMismatch,
                    $"GetCandlesChronologicalAsync warmup count mismatch: expected=0-or-{scope.Partition.RequiredWarmupCandleCount}; actual={warmUpCount}.");
            }

            bool spansWarmup = from < scope.SegmentStartUtc;
            bool spansEvaluation = to > scope.SegmentStartUtc;

            if (spansWarmup && spansEvaluation)
            {
                scope.DenyRepositoryAccess(
                    repositoryRequest,
                    ValidationCandlePartitionDenialCodes.CrossPartitionCompatibilityReadForbidden,
                    $"GetCandlesChronologicalAsync range [{from:O}, {to:O}) spans both warmup and evaluation partitions.");
            }

            var evaluationRequest = CreateRepositoryRequest(
                nameof(GetCandlesChronologicalAsync),
                ValidationCandleAccessPurpose.RepositoryRange,
                symbolId: symbolId,
                timeframe: timeframe,
                requestedStartUtc: from,
                requestedEndUtc: to);
            var eval = scope.GetRepositoryEvaluationRange(evaluationRequest);

            if (warmUpCount <= 0 || eval.Count == 0 && fromUtc is null)
            {
                if (warmUpCount <= 0)
                {
                    return eval;
                }
            }

            if (warmUpCount > 0)
            {
                var before = eval.Count > 0
                    ? eval[0].OpenTimeUtc
                    : NormalizeUtcInstant(fromUtc ?? scope.SegmentStartUtc);
                var warm = scope.GetWarmupBefore(
                    new ValidationWarmupAccessRequest
                    {
                        BeforeOpenTimeUtc = before,
                        Count = warmUpCount,
                        Purpose = ValidationCandleAccessPurpose.WarmupBefore,
                        CallerComponent = nameof(GetCandlesChronologicalAsync)
                    });
                return warm.Count == 0 ? eval : warm.Concat(eval).ToList();
            }

            return eval;
        }

        return await _inner.GetCandlesChronologicalAsync(
            symbolId, timeframe, fromUtc, toUtc, warmUpCount, cancellationToken);
    }

    public Task<Candle?> GetLatestCandleAsync(
        long symbolId,
        Timeframe timeframe,
        CancellationToken cancellationToken = default)
    {
        GuardUnscopedValidationTraining(nameof(GetLatestCandleAsync));

        if (ValidationTrainingCandleScopeAmbient.Current is { } scope)
        {
            return Task.FromResult(scope.GetLatestEvaluationCandle(
                CreateRepositoryRequest(
                    nameof(GetLatestCandleAsync),
                    ValidationCandleAccessPurpose.RepositoryLookup,
                    symbolId: symbolId,
                    timeframe: timeframe)));
        }

        return _inner.GetLatestCandleAsync(symbolId, timeframe, cancellationToken);
    }

    public Task<int> CountCandlesAsync(
        long symbolId,
        Timeframe timeframe,
        CancellationToken cancellationToken = default)
    {
        GuardUnscopedValidationTraining(nameof(CountCandlesAsync));

        if (ValidationTrainingCandleScopeAmbient.Current is { } scope)
        {
            return Task.FromResult(scope.GetCombinedCandleCount(
                CreateRepositoryRequest(
                    nameof(CountCandlesAsync),
                    ValidationCandleAccessPurpose.RepositoryCount,
                    symbolId: symbolId,
                    timeframe: timeframe)));
        }

        return _inner.CountCandlesAsync(symbolId, timeframe, cancellationToken);
    }

    public Task<HashSet<DateTime>> GetExistingOpenTimesAsync(
        long exchangeId,
        long symbolId,
        Timeframe timeframe,
        IReadOnlyCollection<DateTime> openTimesUtc,
        CancellationToken cancellationToken = default)
    {
        GuardUnscopedValidationTraining(nameof(GetExistingOpenTimesAsync));

        if (ValidationTrainingCandleScopeAmbient.Current is { } scope)
        {
            ArgumentNullException.ThrowIfNull(openTimesUtc);
            var normalized = openTimesUtc
                .Select(NormalizeUtcInstant)
                .ToArray();
            var matches = scope.GetExistingImmutableCandles(
                CreateRepositoryRequest(
                    nameof(GetExistingOpenTimesAsync),
                    ValidationCandleAccessPurpose.RepositoryLookup,
                    exchangeId: exchangeId,
                    symbolId: symbolId,
                    timeframe: timeframe,
                    requestedStartUtc: normalized.Length > 0 ? normalized.Min() : null,
                    requestedEndUtc: normalized.Length > 0 ? normalized.Max() : null,
                    requestedCount: normalized.Length),
                normalized);
            return Task.FromResult(matches.Select(c => c.OpenTimeUtc).ToHashSet());
        }

        return _inner.GetExistingOpenTimesAsync(exchangeId, symbolId, timeframe, openTimesUtc, cancellationToken);
    }

    public Task AddRangeAsync(IReadOnlyCollection<Candle> candles, CancellationToken cancellationToken = default)
    {
        GuardUnscopedValidationTraining(nameof(AddRangeAsync));
        if (ValidationTrainingCandleScopeAmbient.Current is { } scope)
        {
            scope.DenyRepositoryAccess(
                CreateRepositoryRequest(
                    nameof(AddRangeAsync),
                    ValidationCandleAccessPurpose.RepositoryWrite,
                    requestedCount: candles?.Count),
                ValidationCandlePartitionDenialCodes.ValidationTrainingWriteForbidden,
                "Validation-training candle writes are forbidden: operation=AddRangeAsync.");
        }

        return _inner.AddRangeAsync(
            candles ?? throw new ArgumentNullException(nameof(candles)),
            cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        GuardUnscopedValidationTraining(nameof(SaveChangesAsync));
        if (ValidationTrainingCandleScopeAmbient.Current is { } scope)
        {
            scope.DenyRepositoryAccess(
                CreateRepositoryRequest(nameof(SaveChangesAsync), ValidationCandleAccessPurpose.RepositoryWrite),
                ValidationCandlePartitionDenialCodes.ValidationTrainingWriteForbidden,
                "Validation-training candle writes are forbidden: operation=SaveChangesAsync.");
        }

        return _inner.SaveChangesAsync(cancellationToken);
    }

    public async Task<Candle?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        GuardUnscopedValidationTraining(nameof(GetByIdAsync));

        if (ValidationTrainingCandleScopeAmbient.Current is { } scope)
        {
            return scope.GetByImmutableId(
                CreateRepositoryRequest(
                    nameof(GetByIdAsync),
                    ValidationCandleAccessPurpose.RepositoryLookup,
                    requestedCount: 1),
                id);
        }

        return await _inner.GetByIdAsync(id, cancellationToken);
    }

    public async Task<IReadOnlyList<Candle>> GetRecentCandlesAsync(
        long symbolId,
        Timeframe timeframe,
        DateTime beforeOrAtOpenTimeUtc,
        int count,
        CancellationToken cancellationToken = default)
    {
        GuardUnscopedValidationTraining(nameof(GetRecentCandlesAsync));

        if (ValidationTrainingCandleScopeAmbient.Current is { } scope)
        {
            var ts = NormalizeUtcInstant(beforeOrAtOpenTimeUtc);
            return scope.GetRecentEvaluationCandles(
                CreateRepositoryRequest(
                    nameof(GetRecentCandlesAsync),
                    ValidationCandleAccessPurpose.RepositoryRecent,
                    symbolId: symbolId,
                    timeframe: timeframe,
                    requestedStartUtc: scope.SegmentStartUtc,
                    requestedEndUtc: ts,
                    requestedCount: count),
                ts,
                count);
        }

        return await _inner.GetRecentCandlesAsync(symbolId, timeframe, beforeOrAtOpenTimeUtc, count, cancellationToken);
    }

    public async Task<IReadOnlyList<DateTime>> GetOpenTimesInRangeAsync(
        long exchangeId,
        long symbolId,
        Timeframe timeframe,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default)
    {
        GuardUnscopedValidationTraining(nameof(GetOpenTimesInRangeAsync));

        if (ValidationTrainingCandleScopeAmbient.Current is { } scope)
        {
            var range = scope.GetRepositoryEvaluationRange(
                CreateRepositoryRequest(
                    nameof(GetOpenTimesInRangeAsync),
                    ValidationCandleAccessPurpose.RepositoryRange,
                    exchangeId: exchangeId,
                    symbolId: symbolId,
                    timeframe: timeframe,
                    requestedStartUtc: fromUtc,
                    requestedEndUtc: toUtc));
            return range.Select(c => c.OpenTimeUtc).ToList();
        }

        return await _inner.GetOpenTimesInRangeAsync(exchangeId, symbolId, timeframe, fromUtc, toUtc, cancellationToken);
    }

    public Task<int> CountDuplicateKeysInRangeAsync(
        long exchangeId,
        long symbolId,
        Timeframe timeframe,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default)
    {
        GuardUnscopedValidationTraining(nameof(CountDuplicateKeysInRangeAsync));

        if (ValidationTrainingCandleScopeAmbient.Current is { } scope)
        {
            // Training scope is immutable and duplicate-free by construction.
            _ = scope.GetRepositoryEvaluationRange(
                CreateRepositoryRequest(
                    nameof(CountDuplicateKeysInRangeAsync),
                    ValidationCandleAccessPurpose.RepositoryRange,
                    exchangeId: exchangeId,
                    symbolId: symbolId,
                    timeframe: timeframe,
                    requestedStartUtc: fromUtc,
                    requestedEndUtc: toUtc));
            return Task.FromResult(0);
        }

        return _inner.CountDuplicateKeysInRangeAsync(exchangeId, symbolId, timeframe, fromUtc, toUtc, cancellationToken);
    }

    private void GuardUnscopedValidationTraining(string callerComponent)
    {
        if (_executionContextAccessor?.IsValidationTrainingActive != true)
        {
            return;
        }

        if (ValidationTrainingCandleScopeAmbient.Current is not null)
        {
            // Ambient training scope is the authorized secondary path.
            return;
        }

        if (ValidationScopeFactoryCapability.IsActive)
        {
            return;
        }

        var current = _executionContextAccessor.Current!;
        throw new ValidationTrainingUnscopedAccessException(
            current.ValidationExperimentId,
            current.TrainingBoundaryUtc,
            callerComponent,
            $"Unscoped candle repository access via {callerComponent} is forbidden during ValidationTraining.");
    }

    private static ValidationRepositoryAccessRequest CreateRepositoryRequest(
        string callerComponent,
        ValidationCandleAccessPurpose purpose,
        long? exchangeId = null,
        long? symbolId = null,
        Timeframe? timeframe = null,
        DateTime? requestedStartUtc = null,
        DateTime? requestedEndUtc = null,
        int? requestedCount = null) =>
        new()
        {
            CallerComponent = callerComponent,
            Purpose = purpose,
            RequestExchangeId = exchangeId,
            RequestSymbolId = symbolId,
            RequestTimeframe = timeframe,
            RequestedStartUtc = requestedStartUtc is null
                ? null
                : NormalizeUtcInstant(requestedStartUtc.Value),
            RequestedEndUtc = requestedEndUtc is null
                ? null
                : NormalizeUtcInstant(requestedEndUtc.Value),
            RequestedCandleCount = requestedCount
        };

    private static DateTime NormalizeUtcInstant(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}
