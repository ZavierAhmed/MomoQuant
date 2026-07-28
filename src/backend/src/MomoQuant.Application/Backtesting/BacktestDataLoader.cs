using MomoQuant.Application.Abstractions;
using MomoQuant.Application.MarketData;
using MomoQuant.Application.Research;
using MomoQuant.Application.StrategyLab;
using MomoQuant.Application.ValidationLab;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Indicators;
using MomoQuant.Domain.MarketData;
using MomoQuant.Domain.Strategies;

namespace MomoQuant.Application.Backtesting;

public interface IBacktestDataLoader
{
    Task<BacktestDataset?> LoadSymbolTimeframeAsync(
        long exchangeId,
        long symbolId,
        Timeframe timeframe,
        DateTime fromUtc,
        DateTime toUtc,
        int warmUpCount,
        CancellationToken cancellationToken = default);

    Task<BacktestDataset?> LoadSymbolTimeframeAsync(
        long exchangeId,
        long symbolId,
        Timeframe timeframe,
        DateTime fromUtc,
        DateTime toUtc,
        int warmUpCount,
        string? contractVersion,
        CancellationToken cancellationToken = default);
}

public sealed class BacktestDataset
{
    public required long SymbolId { get; init; }
    public required string SymbolName { get; init; }
    public required Timeframe Timeframe { get; init; }
    public required IReadOnlyList<Candle> Candles { get; init; }
    public required IReadOnlyDictionary<long, IndicatorSnapshot> IndicatorSnapshots { get; init; }
    public required IReadOnlyList<int> EvaluationIndices { get; init; }

    /// <summary>
    /// Preloaded higher-timeframe candle series keyed by HTF timeframe for efficient per-candle slicing.
    /// </summary>
    public IReadOnlyDictionary<Timeframe, IReadOnlyList<Candle>> HigherTimeframeSeriesByTimeframe { get; init; }
        = new Dictionary<Timeframe, IReadOnlyList<Candle>>();
}

public sealed class BacktestDataLoader : IBacktestDataLoader
{
    private const int DefaultWarmUpCount = 600;

    private readonly ICandleRepository _candleRepository;
    private readonly IIndicatorSnapshotRepository _indicatorSnapshotRepository;
    private readonly ISymbolRepository _symbolRepository;
    private readonly IResearchExecutionContextAccessor? _executionContextAccessor;

    public BacktestDataLoader(
        ICandleRepository candleRepository,
        IIndicatorSnapshotRepository indicatorSnapshotRepository,
        ISymbolRepository symbolRepository,
        IResearchExecutionContextAccessor? executionContextAccessor = null)
    {
        _candleRepository = candleRepository;
        _indicatorSnapshotRepository = indicatorSnapshotRepository;
        _symbolRepository = symbolRepository;
        _executionContextAccessor = executionContextAccessor;
    }

    public async Task<BacktestDataset?> LoadSymbolTimeframeAsync(
        long exchangeId,
        long symbolId,
        Timeframe timeframe,
        DateTime fromUtc,
        DateTime toUtc,
        int warmUpCount,
        CancellationToken cancellationToken = default) =>
        await LoadSymbolTimeframeAsync(
            exchangeId,
            symbolId,
            timeframe,
            fromUtc,
            toUtc,
            warmUpCount,
            StrategyLabCandleLoadContractVersions.LegacyV1,
            cancellationToken);

    public async Task<BacktestDataset?> LoadSymbolTimeframeAsync(
        long exchangeId,
        long symbolId,
        Timeframe timeframe,
        DateTime fromUtc,
        DateTime toUtc,
        int warmUpCount,
        string? contractVersion,
        CancellationToken cancellationToken = default)
    {
        GuardAgainstUnscopedValidationTraining();

        var symbol = await _symbolRepository.GetByIdAsync(symbolId, cancellationToken);
        if (symbol is null || symbol.ExchangeId != exchangeId)
        {
            return null;
        }

        if (contractVersion is not null &&
            contractVersion != StrategyLabCandleLoadContractVersions.LegacyV1 &&
            contractVersion != StrategyLabCandleLoadContractVersions.ExactExclusiveV2)
        {
            throw new InvalidOperationException(
                $"Unknown CandleLoadContractVersion '{contractVersion}'. Expected null, '{StrategyLabCandleLoadContractVersions.LegacyV1}', or '{StrategyLabCandleLoadContractVersions.ExactExclusiveV2}'.");
        }

        var version = contractVersion ?? StrategyLabCandleLoadContractVersions.LegacyV1;
        var warmUp = version == StrategyLabCandleLoadContractVersions.ExactExclusiveV2
            ? warmUpCount
            : Math.Max(warmUpCount, DefaultWarmUpCount);

        var candles = await _candleRepository.GetCandlesChronologicalAsync(
            symbolId,
            timeframe,
            fromUtc,
            toUtc,
            warmUpCount: warmUp,
            cancellationToken);

        if (candles.Count == 0)
        {
            return null;
        }

        var evaluationIndices = candles
            .Select((candle, index) => (candle, index))
            .Where(item => StrategyLabCandleLoadContract.ContainsEvaluationOpenTime(
                version,
                item.candle.OpenTimeUtc,
                fromUtc,
                toUtc))
            .Select(item => item.index)
            .ToList();

        if (evaluationIndices.Count == 0)
        {
            return null;
        }

        var candleIds = candles.Select(candle => candle.Id).ToList();
        var snapshots = await _indicatorSnapshotRepository.GetByCandleIdsAsync(
            symbolId,
            timeframe,
            candleIds,
            cancellationToken);

        return new BacktestDataset
        {
            SymbolId = symbolId,
            SymbolName = symbol.SymbolName,
            Timeframe = timeframe,
            Candles = candles,
            IndicatorSnapshots = snapshots,
            EvaluationIndices = evaluationIndices
        };
    }

    private void GuardAgainstUnscopedValidationTraining()
    {
        var current = _executionContextAccessor?.Current;
        if (current?.ExecutionPurpose != ExecutionPurpose.ValidationTraining)
        {
            return;
        }

        // Scope factory may activate capability during immutable-scope bootstrap only.
        if (ValidationScopeFactoryCapability.IsActive)
        {
            return;
        }

        throw new ValidationTrainingUnscopedAccessException(
            current.ValidationExperimentId,
            current.TrainingBoundaryUtc,
            nameof(BacktestDataLoader),
            "Unscoped BacktestDataLoader access is forbidden during ValidationTraining.");
    }
}
