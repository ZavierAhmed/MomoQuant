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

        var request = new ValidationDatasetMaterializationRequest
        {
            SymbolId = run.SymbolId,
            SymbolName = string.IsNullOrWhiteSpace(run.Symbol) ? _scope.Partition.SymbolName : run.Symbol,
            Timeframe = run.Timeframe,
            EvaluationFromUtc = DateTime.SpecifyKind(run.FromUtc, DateTimeKind.Utc),
            EvaluationToExclusiveUtc = DateTime.SpecifyKind(run.ToUtc, DateTimeKind.Utc),
            WarmupCandleCount = warmupCandles,
            CallerComponent = _callerComponent
        };

        var dataset = _scope.CreateStrategyLabDataset(request);
        return Task.FromResult(dataset);
    }
}
