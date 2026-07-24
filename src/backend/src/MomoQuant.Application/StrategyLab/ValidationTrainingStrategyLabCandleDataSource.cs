using MomoQuant.Application.MarketData;
using MomoQuant.Application.ValidationLab;
using MomoQuant.Domain.StrategyLab;

namespace MomoQuant.Application.StrategyLab;

/// <summary>
/// Validation-training candle source. Loads exclusively via
/// <see cref="IValidationTrainingCandleScope.CreateStrategyLabDataset"/> — never reads raw Candles.
/// Never uses BacktestDataLoader, ICandleRepository, coverage import, or DbContext.
/// </summary>
public sealed class ValidationTrainingStrategyLabCandleDataSource : IStrategyLabCandleDataSource
{
    private readonly IValidationTrainingCandleScope _scope;
    private readonly string _callerComponent;

    public ValidationTrainingStrategyLabCandleDataSource(
        IValidationTrainingCandleScope scope,
        string? callerComponent = null)
    {
        _scope = scope ?? throw new ArgumentNullException(nameof(scope));
        _callerComponent = string.IsNullOrWhiteSpace(callerComponent)
            ? "ValidationTrainingStrategyLabCandleDataSource"
            : callerComponent;
    }

    public Task<StrategyLabDataset> LoadAsync(
        StrategyLabRun run,
        int warmupCandles,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!TimeframeParser.TryParse(run.Timeframe, out _))
        {
            throw new InvalidOperationException(TimeframeNormalizer.UnsupportedTimeframeMessage(run.Timeframe));
        }

        var context = ValidationCandleAccessContext.Create(
            _callerComponent,
            ValidationCandleAccessPurpose.StrategyLabDataset);

        var dataset = _scope.CreateStrategyLabDataset(run, warmupCandles, context);
        return Task.FromResult(dataset);
    }
}
