using MomoQuant.Application.Backtesting;
using MomoQuant.Application.Common;
using MomoQuant.Application.MarketData;
using MomoQuant.Application.Optimization.Dtos;
using MomoQuant.Application.Strategies;
using MomoQuant.Application.Strategies.Optimization;
using MomoQuant.Application.Validation;
using MomoQuant.Application.Validation.Dtos;
using MomoQuant.Domain.Enums;

namespace MomoQuant.Application.Optimization;

public interface IStrategyResearchBacktestExecutor
{
    Task<StrategyResearchBacktestResult?> RunWindowAsync(
        long exchangeId,
        long symbolId,
        string timeframe,
        DateTime fromUtc,
        DateTime toUtc,
        string strategyCode,
        IReadOnlyDictionary<string, string> parameters,
        long riskProfileId,
        decimal initialBalance,
        StrategyResearchExecutionOptions? options = null,
        CancellationToken cancellationToken = default);
}

public interface IParameterOptimizationScorer
{
    decimal Score(
        StrategyPerformanceMetricsDto training,
        StrategyPerformanceMetricsDto validation,
        string objectivePreset,
        int minTradesTraining,
        int minTradesValidation);
}

public sealed class ParameterOptimizationScorer : IParameterOptimizationScorer
{
    public decimal Score(
        StrategyPerformanceMetricsDto training,
        StrategyPerformanceMetricsDto validation,
        string objectivePreset,
        int minTradesTraining,
        int minTradesValidation)
    {
        var weights = objectivePreset switch
        {
            "LowDrawdown" => (pnl: 15m, pf: 20m, dd: 35m, trades: 10m, exp: 10m, consistency: 10m),
            "ProfitFactor" => (pnl: 15m, pf: 40m, dd: 15m, trades: 10m, exp: 10m, consistency: 10m),
            "MoreTrades" => (pnl: 20m, pf: 15m, dd: 15m, trades: 30m, exp: 10m, consistency: 10m),
            _ => (pnl: 25m, pf: 25m, dd: 20m, trades: 10m, exp: 10m, consistency: 10m)
        };

        var pnlScore = ClampScore(validation.NetPnlPercent, -10m, 30m);
        var pfScore = ClampScore(validation.ProfitFactor, 0m, 3m);
        var ddScore = 100m - ClampScore(validation.MaxDrawdownPercent, 0m, 50m);
        var tradeScore = validation.TradeCount >= minTradesValidation ? 100m : validation.TradeCount * 100m / Math.Max(1, minTradesValidation);
        var expScore = ClampScore(validation.Expectancy, -50m, 100m);
        var consistency = CalculateConsistency(training, validation);

        var composite = (pnlScore * weights.pnl + pfScore * weights.pf + ddScore * weights.dd +
                         tradeScore * weights.trades + expScore * weights.exp + consistency * weights.consistency) / 100m;

        composite -= OverfittingPenalty(training, validation);
        if (training.TradeCount < minTradesTraining) composite -= 20m;
        if (validation.TradeCount < minTradesValidation) composite -= 25m;
        if (validation.TradeCount <= 2) composite -= 30m;
        if (validation.TradeCount == 0) return 0m;

        return Math.Round(Math.Max(0m, composite), 2);
    }

    private static decimal OverfittingPenalty(StrategyPerformanceMetricsDto training, StrategyPerformanceMetricsDto validation)
    {
        var penalty = 0m;
        if (training.NetPnlPercent > 5m && validation.NetPnlPercent <= 0m) penalty += 25m;
        if (training.ProfitFactor > 2m && validation.ProfitFactor < training.ProfitFactor * 0.5m) penalty += 20m;
        if (training.WinRate - validation.WinRate > 25m) penalty += 15m;
        return penalty;
    }

    private static decimal CalculateConsistency(StrategyPerformanceMetricsDto training, StrategyPerformanceMetricsDto validation)
    {
        if (training.ProfitFactor <= 0m) return validation.ProfitFactor >= 1.2m ? 100m : 0m;
        return ClampScore(validation.ProfitFactor / training.ProfitFactor, 0m, 1.2m) * 100m / 1.2m;
    }

    private static decimal ClampScore(decimal value, decimal min, decimal max) =>
        max <= min ? 0m : Math.Max(0m, Math.Min(100m, (value - min) / (max - min) * 100m));
}

public interface IParameterOptimizationService
{
    Task<ServiceResult<ParameterOptimizationResultDto>> RunAsync(RunParameterOptimizationRequest request, long? userId, CancellationToken cancellationToken = default);
    Task<ServiceResult<ParameterOptimizationResultDto>> GetAsync(long runId, CancellationToken cancellationToken = default);
    Task<ServiceResult<bool>> CancelAsync(long runId, CancellationToken cancellationToken = default);
}

public interface IStrategyValidationService
{
    Task<ServiceResult<StrategyValidationResultDto>> RunAsync(RunStrategyValidationRequest request, CancellationToken cancellationToken = default);
}

public interface IStrategyParameterSetService
{
    Task<ServiceResult<IReadOnlyList<StrategyParameterSetDto>>> ListAsync(string? strategyCode, long? symbolId, string? timeframe, CancellationToken cancellationToken = default);
    Task<ServiceResult<StrategyParameterSetDto>> SaveAsync(SaveStrategyParameterSetRequest request, CancellationToken cancellationToken = default);
    Task<ServiceResult<StrategyParameterSetDto>> ApproveAsync(long id, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<string, string>?> GetFrozenParametersAsync(long parameterSetId, CancellationToken cancellationToken = default);
}
