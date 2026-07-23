using MomoQuant.Application.Abstractions;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.MarketData;
using MomoQuant.Domain.ValidationLab;

namespace MomoQuant.Application.ValidationLab;

public interface IValidationTrainingCandleScopeFactory
{
    Task<IValidationTrainingCandleScope> CreateForExperimentAsync(
        ValidationExperiment experiment,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Builds an immutable training candle scope from DB candles strictly before ValidationStartUtc.
/// Uses the inner (unscoped) candle repository to avoid recursive boundary checks during bootstrap.
/// </summary>
public sealed class ValidationTrainingCandleScopeFactory : IValidationTrainingCandleScopeFactory
{
    private readonly IUnscopedCandleReader _candles;

    public ValidationTrainingCandleScopeFactory(IUnscopedCandleReader candles) => _candles = candles;

    public async Task<IValidationTrainingCandleScope> CreateForExperimentAsync(
        ValidationExperiment experiment,
        CancellationToken cancellationToken = default)
    {
        if (experiment.ValidationStartUtc is null || experiment.TrainingStartUtc is null)
        {
            throw new InvalidOperationException("Training candle scope requires TrainingStartUtc and ValidationStartUtc.");
        }

        if (!Enum.TryParse<Timeframe>(experiment.Timeframe, true, out var timeframe))
        {
            throw new InvalidOperationException($"Unknown timeframe '{experiment.Timeframe}'.");
        }

        var boundary = DateTime.SpecifyKind(experiment.ValidationStartUtc.Value, DateTimeKind.Utc);
        var from = DateTime.SpecifyKind(experiment.TrainingStartUtc.Value, DateTimeKind.Utc);

        // Load through exclusive boundary so warm-up-adjacent training bars are available,
        // then scope filters strictly OpenTimeUtc < ValidationStartUtc.
        var loaded = await _candles.GetCandlesChronologicalUnscopedAsync(
            experiment.SymbolId,
            timeframe,
            from,
            boundary,
            warmUpCount: 0,
            cancellationToken);

        return new ValidationTrainingCandleScope(
            experiment.Id,
            from,
            boundary,
            loaded);
    }
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
}
