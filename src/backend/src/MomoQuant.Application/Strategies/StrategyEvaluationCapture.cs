using MomoQuant.Domain.Enums;
using MomoQuant.Domain.MarketData;
using MomoQuant.Domain.Strategies;

namespace MomoQuant.Application.Strategies;

/// <summary>
/// Captures the exact StrategyContext HTF series delivered to plugins at evaluation time T.
/// Used by closed-HTF production-path proofs across Backtest/Replay/Paper/Benchmark/manual paths.
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
            HigherTimeframeCandles: context.HigherTimeframeCandles.ToList()));
    }

    public void Clear() => _records.Clear();
}

public sealed record StrategyEvaluationCaptureRecord(
    StrategyCode StrategyCode,
    DateTime EvaluatedAtUtc,
    Timeframe ExecutionTimeframe,
    Timeframe HigherTimeframe,
    IReadOnlyList<Candle> HigherTimeframeCandles);
