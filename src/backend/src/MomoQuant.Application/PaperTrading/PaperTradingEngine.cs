using MomoQuant.Application.Backtesting;
using MomoQuant.Application.Backtesting.Simulation;

namespace MomoQuant.Application.PaperTrading;

public interface IPaperTradingEngine
{
    Task<PaperTradingDecisionResult?> ProcessNextCandleAsync(
        PaperSessionState state,
        CancellationToken cancellationToken = default);

    Task FinalizeSessionAsync(PaperSessionState state, CancellationToken cancellationToken = default);
}

public sealed class PaperTradingEngine : IPaperTradingEngine
{
    private readonly IBacktestEngine _backtestEngine;
    private readonly IPaperExecutionProvider _executionProvider;

    public PaperTradingEngine(IBacktestEngine backtestEngine, IPaperExecutionProvider executionProvider)
    {
        _backtestEngine = backtestEngine;
        _executionProvider = executionProvider;
    }

    public async Task<PaperTradingDecisionResult?> ProcessNextCandleAsync(
        PaperSessionState state,
        CancellationToken cancellationToken = default)
    {
        if (state.NextEvaluationIndex >= state.Dataset.EvaluationIndices.Count)
        {
            return null;
        }

        var evaluationIndex = state.NextEvaluationIndex;
        var result = state.FrozenStrategyParameters is { Count: > 0 }
            ? await _backtestEngine.ProcessCandleAtIndexWithParametersAsync(
                state.Context,
                state.Dataset,
                state.Strategies,
                state.FrozenStrategyParameters,
                evaluationIndex,
                cancellationToken)
            : await _backtestEngine.ProcessCandleAtIndexAsync(
                state.Context,
                state.Dataset,
                state.Strategies,
                evaluationIndex,
                cancellationToken);

        state.NextEvaluationIndex++;
        state.Session.CurrentCandleIndex = evaluationIndex;
        state.Session.CurrentCandleTimeUtc = result.Candle.CloseTimeUtc;

        var equity = state.Context.CalculateEquity();
        var drawdown = state.Context.PeakEquity - equity;
        var drawdownPercent = state.Context.PeakEquity > 0 ? drawdown / state.Context.PeakEquity * 100m : 0m;

        return new PaperTradingDecisionResult
        {
            Tick = new PaperTradingTick
            {
                Candle = result.Candle,
                EvaluationIndex = evaluationIndex
            },
            ProcessResult = result,
            Balance = state.Context.Balance,
            Equity = equity,
            Drawdown = drawdown,
            DrawdownPercent = drawdownPercent
        };
    }

    public Task FinalizeSessionAsync(PaperSessionState state, CancellationToken cancellationToken = default)
    {
        if (state.Dataset.EvaluationIndices.Count == 0)
        {
            return Task.CompletedTask;
        }

        var lastIndex = state.Dataset.EvaluationIndices[^1];
        var lastCandle = state.Dataset.Candles[lastIndex];
        _executionProvider.SimulatedExecution.FinalizePendingOrders(state.Context, lastCandle, lastIndex);
        _executionProvider.SimulatedExecution.UpdateOpenPositions(state.Context, lastCandle);
        return Task.CompletedTask;
    }
}
