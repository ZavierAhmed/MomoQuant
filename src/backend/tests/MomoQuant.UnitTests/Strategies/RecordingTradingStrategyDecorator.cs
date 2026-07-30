using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Strategies;

namespace MomoQuant.UnitTests.Strategies;

/// <summary>
/// Captures the actual StrategyContext and StrategySignalResult delivered through StrategyLabRunner (Milestone 23.1B1C1).
/// </summary>
internal sealed class RecordingTradingStrategyDecorator : ITradingStrategy
{
    private readonly ITradingStrategy _inner;

    public RecordingTradingStrategyDecorator(ITradingStrategy inner) => _inner = inner;

    public List<(StrategyContext Context, StrategySignalResult Result)> Evaluations { get; } = [];

    public StrategyCode Code => _inner.Code;

    public string Name => _inner.Name;

    public string Description => _inner.Description;

    public IReadOnlyCollection<MarketRegime> SupportedRegimes => _inner.SupportedRegimes;

    public IReadOnlyCollection<Timeframe> SupportedTimeframes => _inner.SupportedTimeframes;

    public StrategySignalResult Evaluate(StrategyContext context)
    {
        var clonedContext = CloneContext(context);
        var result = _inner.Evaluate(context);
        Evaluations.Add((clonedContext, result));
        return result;
    }

    internal static StrategyContext CloneContext(StrategyContext context) =>
        new()
        {
            TradingSessionId = context.TradingSessionId,
            ExchangeId = context.ExchangeId,
            SymbolId = context.SymbolId,
            Symbol = context.Symbol,
            Timeframe = context.Timeframe,
            HigherTimeframe = context.HigherTimeframe,
            HigherTimeframeCandles = context.HigherTimeframeCandles.ToList(),
            MarketRegime = context.MarketRegime,
            Candles = context.Candles.ToList(),
            IndicatorSnapshot = context.IndicatorSnapshot,
            RecentIndicatorSnapshots = context.RecentIndicatorSnapshots.ToList(),
            StrategyParameters = new Dictionary<string, string>(context.StrategyParameters),
            EvaluatedAtUtc = context.EvaluatedAtUtc,
            CurrentCandleIndex = context.CurrentCandleIndex
        };
}
