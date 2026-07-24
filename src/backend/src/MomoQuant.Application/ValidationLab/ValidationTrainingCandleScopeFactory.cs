using MomoQuant.Application.Abstractions;
using MomoQuant.Application.MarketData;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.MarketData;
using MomoQuant.Domain.ValidationLab;

namespace MomoQuant.Application.ValidationLab;

public interface IValidationTrainingCandleScopeFactory
{
    /// <summary>
    /// v2: builds an immutable training scope with exact warm-up + evaluation partitions.
    /// Throws <see cref="ValidationTrainingInsufficientWarmupException"/> when available &lt; required.
    /// </summary>
    Task<IValidationTrainingCandleScope> CreateAsync(
        ValidationTrainingCandleScopeRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Obsolete wrapper — prefer <see cref="CreateAsync"/>. Uses experiment.RequiredWarmupCandles
    /// when a full requirements resolution was not supplied.
    /// </summary>
    [Obsolete("Use CreateAsync(ValidationTrainingCandleScopeRequest) after resolving StrategyExecutionRequirements.")]
    Task<IValidationTrainingCandleScope> CreateForExperimentAsync(
        ValidationExperiment experiment,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Builds an immutable training candle scope from DB candles strictly before ValidationStartUtc.
/// Loads exact warm-up (latest N closed bars before evaluation start) plus evaluation bars.
/// Uses the inner (unscoped) candle repository to avoid recursive boundary checks during bootstrap.
/// </summary>
public sealed class ValidationTrainingCandleScopeFactory : IValidationTrainingCandleScopeFactory
{
    private readonly IUnscopedCandleReader _candles;
    private readonly ValidationScopeFactoryCapability _capability = ValidationScopeFactoryCapability.Create();

    public ValidationTrainingCandleScopeFactory(IUnscopedCandleReader candles) => _candles = candles;

    public async Task<IValidationTrainingCandleScope> CreateAsync(
        ValidationTrainingCandleScopeRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();

        if (!TimeframeParser.TryParse(request.Timeframe, out var timeframe))
        {
            throw new InvalidOperationException($"Unknown timeframe '{request.Timeframe}'.");
        }

        var evalStart = request.TrainingEvaluationStartUtc;
        var evalEndExclusive = request.TrainingEvaluationEndExclusiveUtc;
        var boundary = request.ValidationBoundaryUtc;
        var loadEndExclusive = evalEndExclusive <= boundary ? evalEndExclusive : boundary;
        var requiredWarmup = request.RequiredWarmupCandleCount;

        using (_capability.Activate())
        {
            IReadOnlyList<Candle> warmup;
            if (requiredWarmup > 0)
            {
                warmup = await _candles.GetClosedCandlesBeforeUnscopedAsync(
                    request.SymbolId,
                    timeframe,
                    beforeOpenTimeUtc: evalStart,
                    count: requiredWarmup,
                    cancellationToken);
            }
            else
            {
                warmup = Array.Empty<Candle>();
            }

            // Evaluation: >= start AND < endExclusive AND < boundary
            var evaluation = await _candles.GetCandlesChronologicalUnscopedAsync(
                request.SymbolId,
                timeframe,
                fromUtc: evalStart,
                toUtc: loadEndExclusive,
                warmUpCount: 0,
                cancellationToken);

            evaluation = evaluation
                .Where(c =>
                {
                    var open = DateTime.SpecifyKind(c.OpenTimeUtc, DateTimeKind.Utc);
                    return open >= evalStart
                           && open < loadEndExclusive
                           && open < boundary
                           && c.SymbolId == request.SymbolId;
                })
                .OrderBy(c => c.OpenTimeUtc)
                .ToList();

            warmup = warmup
                .Where(c =>
                {
                    var open = DateTime.SpecifyKind(c.OpenTimeUtc, DateTimeKind.Utc);
                    return open < evalStart
                           && open < boundary
                           && c.IsClosed
                           && c.SymbolId == request.SymbolId;
                })
                .OrderBy(c => c.OpenTimeUtc)
                .ToList();

            var availableWarmup = warmup.Count;
            ValidationWarmupStatus status;
            if (requiredWarmup <= 0)
            {
                status = ValidationWarmupStatus.NotRequired;
            }
            else if (availableWarmup >= requiredWarmup)
            {
                status = ValidationWarmupStatus.Complete;
                // Exact N: keep latest required only (already TAKE N from repo, but clamp).
                if (availableWarmup > requiredWarmup)
                {
                    warmup = warmup.TakeLast(requiredWarmup).ToList();
                    availableWarmup = warmup.Count;
                }
            }
            else
            {
                status = ValidationWarmupStatus.Insufficient;
                throw new ValidationTrainingInsufficientWarmupException(
                    request.ValidationExperimentId,
                    requiredWarmup,
                    availableWarmup,
                    $"Insufficient warm-up candles for validation training experiment {request.ValidationExperimentId}: " +
                    $"available={availableWarmup}, required={requiredWarmup}, status={status}, " +
                    $"requirementsVersion={request.RequirementsVersion}.");
            }

            var combined = warmup.Concat(evaluation).ToList();
            var partition = ValidationTrainingCandleScope.BuildPartition(
                request.ValidationExperimentId,
                request.SymbolId,
                request.SymbolName,
                request.Timeframe,
                requiredWarmup,
                availableWarmup,
                evaluation.Count,
                status,
                evalStart,
                loadEndExclusive,
                boundary,
                request.RequirementsVersion,
                warmup,
                evaluation,
                combined);

            return new ValidationTrainingCandleScope(partition, warmup, evaluation);
        }
    }

#pragma warning disable CS0618
    public Task<IValidationTrainingCandleScope> CreateForExperimentAsync(
        ValidationExperiment experiment,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(experiment);
        if (experiment.TrainingEndUtc is null)
        {
            throw new InvalidOperationException("Training candle scope requires TrainingEndUtc.");
        }

        if (!TimeframeParser.TryGetDurationMinutes(experiment.Timeframe, out var minutes) || minutes <= 0)
        {
            throw new InvalidOperationException($"Unable to resolve timeframe duration for '{experiment.Timeframe}'.");
        }

        var endExclusive = DateTime.SpecifyKind(experiment.TrainingEndUtc.Value, DateTimeKind.Utc)
            .AddMinutes(minutes);
        var request = ValidationTrainingCandleScopeRequest.FromExperimentLegacy(experiment, endExclusive);
        return CreateAsync(request, cancellationToken);
    }
#pragma warning restore CS0618
}

/// <summary>
/// Escape hatch for bootstrap loads that must bypass the ambient training boundary decorator.
/// </summary>
public interface IUnscopedCandleReader
{
    Task<IReadOnlyList<Candle>> GetCandlesChronologicalUnscopedAsync(
        long symbolId,
        Timeframe timeframe,
        DateTime? fromUtc,
        DateTime? toUtc,
        int warmUpCount = 0,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Latest <paramref name="count"/> closed candles with OpenTimeUtc &lt; <paramref name="beforeOpenTimeUtc"/>
    /// (ORDER BY OpenTimeUtc DESC TAKE N, returned ascending).
    /// </summary>
    Task<IReadOnlyList<Candle>> GetClosedCandlesBeforeUnscopedAsync(
        long symbolId,
        Timeframe timeframe,
        DateTime beforeOpenTimeUtc,
        int count,
        CancellationToken cancellationToken = default);
}
