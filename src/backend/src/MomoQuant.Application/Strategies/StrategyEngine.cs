using MomoQuant.Application.Common;
using MomoQuant.Application.MarketData;
using MomoQuant.Application.Strategies.Models;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Strategies;

namespace MomoQuant.Application.Strategies;

public interface IStrategyEngine
{
    Task<IReadOnlyList<StrategyEvaluationResult>> EvaluateAsync(
        IReadOnlyCollection<ITradingStrategy> strategies,
        StrategyContext context,
        CancellationToken cancellationToken = default);
}

public sealed class StrategyEngine : IStrategyEngine
{
    public Task<IReadOnlyList<StrategyEvaluationResult>> EvaluateAsync(
        IReadOnlyCollection<ITradingStrategy> strategies,
        StrategyContext context,
        CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;

        var results = strategies
            .Select(strategy => MapResult(strategy, context, strategy.Evaluate(context)))
            .ToList();

        return Task.FromResult<IReadOnlyList<StrategyEvaluationResult>>(results);
    }

    internal static StrategyEvaluationResult MapResult(
        ITradingStrategy strategy,
        StrategyContext context,
        StrategySignalResult result)
    {
        var skipped = result.SignalType == SignalType.NoTrade;
        return new StrategyEvaluationResult
        {
            StrategyCode = strategy.Code.ToCode(),
            StrategyName = strategy.Name,
            Evaluated = true,
            Skipped = skipped,
            SkipReason = skipped ? result.Reason : null,
            SignalType = result.SignalType,
            Direction = result.Direction,
            Strength = result.SignalType == SignalType.Entry
                ? ConfidenceScoreNormalizer.Normalize(result.Strength)
                : result.Strength,
            ConfidenceContribution = result.SignalType == SignalType.Entry
                ? ConfidenceScoreNormalizer.Normalize(result.ConfidenceContribution)
                : result.ConfidenceContribution,
            EntryPrice = result.EntryPrice,
            SuggestedStopLoss = result.SuggestedStopLoss,
            SuggestedTakeProfit = result.SuggestedTakeProfit,
            Reason = result.Reason,
            Regime = context.MarketRegime.ToString(),
            Timeframe = TimeframeParser.ToApiString(context.Timeframe),
            RawDataJson = result.RawDataJson,
            IsValid = result.SignalType is SignalType.Entry or SignalType.Exit or SignalType.Warning
        };
    }
}
