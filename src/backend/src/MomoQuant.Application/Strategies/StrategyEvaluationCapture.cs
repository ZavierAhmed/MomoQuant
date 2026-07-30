using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Indicators;
using MomoQuant.Domain.MarketData;
using MomoQuant.Domain.Strategies;

namespace MomoQuant.Application.Strategies;

/// <summary>
/// Captures the exact StrategyContext candle series delivered to plugins at evaluation time T.
/// Used by closed-HTF and closed-LTF production-path proofs across Backtest/Replay/Paper/Benchmark/manual paths.
/// </summary>
public interface IStrategyEvaluationCapture
{
    void Capture(StrategyContext context, ITradingStrategy strategy);
}

public sealed class StrategyEvaluationCaptureRecording : IStrategyEvaluationCapture
{
    private readonly List<StrategyEvaluationCaptureRecord> _records = [];

    public IReadOnlyList<StrategyEvaluationCaptureRecord> Records => _records;

    public void Capture(StrategyContext context, ITradingStrategy strategy)
    {
        _records.Add(new StrategyEvaluationCaptureRecord(
            StrategyCode: strategy.Code,
            EvaluatedAtUtc: context.EvaluatedAtUtc,
            ExecutionTimeframe: context.Timeframe,
            HigherTimeframe: context.HigherTimeframe,
            Candles: context.Candles.ToList(),
            HigherTimeframeCandles: context.HigherTimeframeCandles.ToList(),
            ExchangeId: context.ExchangeId,
            SymbolId: context.SymbolId,
            Symbol: context.Symbol,
            MarketRegime: context.MarketRegime,
            CurrentCandleIndex: context.CurrentCandleIndex,
            IndicatorSnapshot: context.IndicatorSnapshot,
            StrategyParameters: new Dictionary<string, string>(context.StrategyParameters)));
    }

    public void Clear() => _records.Clear();
}

public sealed record StrategyEvaluationCaptureRecord(
    StrategyCode StrategyCode,
    DateTime EvaluatedAtUtc,
    Timeframe ExecutionTimeframe,
    Timeframe HigherTimeframe,
    IReadOnlyList<Candle> Candles,
    IReadOnlyList<Candle> HigherTimeframeCandles,
    long? ExchangeId,
    long SymbolId,
    string? Symbol,
    MarketRegime MarketRegime,
    int? CurrentCandleIndex,
    IndicatorSnapshot? IndicatorSnapshot,
    IReadOnlyDictionary<string, string> StrategyParameters);
