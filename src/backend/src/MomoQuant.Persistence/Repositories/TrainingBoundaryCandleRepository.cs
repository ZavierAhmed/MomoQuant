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
            var range = scope.GetEvaluationRange(
                fromUtc,
                toUtc,
                ValidationCandleAccessContext.Create(nameof(GetCandlesAsync), ValidationCandleAccessPurpose.RepositoryRange));
            return limit > 0 ? range.Take(limit).ToList() : range;
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
            var from = fromUtc is null ? scope.SegmentStartUtc : DateTime.SpecifyKind(fromUtc.Value, DateTimeKind.Utc);
            var to = toUtc is null ? scope.SegmentEndExclusiveUtc : DateTime.SpecifyKind(toUtc.Value, DateTimeKind.Utc);

            bool spansWarmup = from < scope.SegmentStartUtc;
            bool spansEvaluation = to > scope.SegmentStartUtc;

            if (spansWarmup && spansEvaluation)
            {
                throw new ValidationCandlePartitionViolationException(
                    scope.ValidationExperimentId,
                    scope.ScopeExecutionId,
                    scope.ValidationBoundaryUtc,
                    from,
                    to,
                    null,
                    scope.SegmentStartUtc,
                    scope.SegmentEndExclusiveUtc,
                    ValidationCandlePartitionDenialCodes.CrossPartitionCompatibilityReadForbidden,
                    $"GetCandlesChronologicalAsync range [{from:O}, {to:O}) spans both warmup and evaluation partitions.",
                    nameof(GetCandlesChronologicalAsync));
            }

            var eval = scope.GetEvaluationRange(
                from,
                to,
                ValidationCandleAccessContext.Create(
                    nameof(GetCandlesChronologicalAsync),
                    ValidationCandleAccessPurpose.RepositoryRange));

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
                    : DateTime.SpecifyKind(fromUtc ?? scope.SegmentStartUtc, DateTimeKind.Utc);
                var warm = scope.GetWarmupBefore(
                    before,
                    warmUpCount,
                    ValidationCandleAccessContext.Create(
                        nameof(GetCandlesChronologicalAsync),
                        ValidationCandleAccessPurpose.WarmupBefore));
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
            var range = scope.GetEvaluationRange(
                scope.SegmentStartUtc,
                scope.SegmentEndExclusiveUtc,
                ValidationCandleAccessContext.Create(
                    nameof(GetLatestCandleAsync),
                    ValidationCandleAccessPurpose.RepositoryLookup));
            var last = range.LastOrDefault();
            return Task.FromResult(last);
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
            _ = scope.GetEvaluationRange(
                scope.SegmentStartUtc,
                scope.SegmentEndExclusiveUtc,
                ValidationCandleAccessContext.Create(
                    nameof(CountCandlesAsync),
                    ValidationCandleAccessPurpose.RepositoryCount));
            return Task.FromResult(scope.Partition.TotalCandleCount);
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
            foreach (var t in openTimesUtc)
            {
                if (DateTime.SpecifyKind(t, DateTimeKind.Utc) >= scope.ValidationBoundaryUtc)
                {
                    _ = scope.GetByOpenTimeUtc(
                        t,
                        ValidationCandleAccessContext.Create(
                            nameof(GetExistingOpenTimesAsync),
                            ValidationCandleAccessPurpose.ByOpenTime));
                }
            }

            var allowed = openTimesUtc
                .Select(t => DateTime.SpecifyKind(t, DateTimeKind.Utc))
                .Where(t => t < scope.ValidationBoundaryUtc)
                .ToHashSet();
            return Task.FromResult(allowed);
        }

        return _inner.GetExistingOpenTimesAsync(exchangeId, symbolId, timeframe, openTimesUtc, cancellationToken);
    }

    public Task AddRangeAsync(IReadOnlyCollection<Candle> candles, CancellationToken cancellationToken = default) =>
        _inner.AddRangeAsync(candles, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _inner.SaveChangesAsync(cancellationToken);

    public async Task<Candle?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        GuardUnscopedValidationTraining(nameof(GetByIdAsync));

        var candle = await _inner.GetByIdAsync(id, cancellationToken);
        if (candle is null)
        {
            return null;
        }

        if (ValidationTrainingCandleScopeAmbient.Current is { } scope)
        {
            return scope.GetByOpenTimeUtc(
                candle.OpenTimeUtc,
                ValidationCandleAccessContext.Create(nameof(GetByIdAsync), ValidationCandleAccessPurpose.ByOpenTime));
        }

        return candle;
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
            var ts = DateTime.SpecifyKind(beforeOrAtOpenTimeUtc, DateTimeKind.Utc);
            
            if (ts >= scope.ValidationBoundaryUtc)
            {
                _ = scope.GetByOpenTimeUtc(
                    ts,
                    ValidationCandleAccessContext.Create(
                        nameof(GetRecentCandlesAsync),
                        ValidationCandleAccessPurpose.ByOpenTime));
            }

            if (ts >= scope.SegmentStartUtc && ts < scope.SegmentEndExclusiveUtc)
            {
                var evalRange = scope.GetEvaluationRange(
                    scope.SegmentStartUtc,
                    scope.SegmentEndExclusiveUtc,
                    ValidationCandleAccessContext.Create(
                        nameof(GetRecentCandlesAsync),
                        ValidationCandleAccessPurpose.RepositoryRecent));
                return evalRange.Where(c => c.OpenTimeUtc <= ts).TakeLast(count).ToList();
            }

            if (ts < scope.SegmentStartUtc)
            {
                var warmupRange = scope.GetWarmupBefore(
                    scope.SegmentStartUtc,
                    scope.Partition.RequiredWarmupCandleCount,
                    ValidationCandleAccessContext.Create(
                        nameof(GetRecentCandlesAsync),
                        ValidationCandleAccessPurpose.RepositoryRecent));
                return warmupRange.Where(c => c.OpenTimeUtc <= ts).TakeLast(count).ToList();
            }

            return Array.Empty<Candle>();
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
            var range = scope.GetEvaluationRange(
                fromUtc,
                toUtc,
                ValidationCandleAccessContext.Create(
                    nameof(GetOpenTimesInRangeAsync),
                    ValidationCandleAccessPurpose.RepositoryRange));
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

        if (ValidationTrainingCandleScopeAmbient.Current is not null)
        {
            // Training scope is immutable and duplicate-free by construction.
            _ = ValidationTrainingCandleScopeAmbient.Current.GetEvaluationRange(
                fromUtc,
                toUtc,
                ValidationCandleAccessContext.Create(
                    nameof(CountDuplicateKeysInRangeAsync),
                    ValidationCandleAccessPurpose.RepositoryRange));
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
}
