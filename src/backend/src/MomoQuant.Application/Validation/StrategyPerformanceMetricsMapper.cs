using MomoQuant.Application.Backtesting;
using MomoQuant.Application.Validation.Dtos;
using MomoQuant.Domain.Backtesting;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Trades;

namespace MomoQuant.Application.Validation;

public static class StrategyPerformanceMetricsMapper
{
    public static StrategyPerformanceMetricsDto FromBacktestResult(BacktestResult result, decimal initialBalance)
    {
        var closedTrades = result.TotalTrades;
        return new StrategyPerformanceMetricsDto
        {
            NetPnlPercent = result.NetPnlPercent,
            WinRate = result.WinRatePercent,
            ProfitFactor = result.ProfitFactor,
            MaxDrawdownPercent = result.MaxDrawdownPercent,
            TradeCount = closedTrades,
            AverageR = result.AverageRewardRisk,
            Expectancy = result.Expectancy,
            SharpeLikeRatio = null,
            RecoveryFactor = result.MaxDrawdown > 0m ? result.NetPnl / result.MaxDrawdown : result.NetPnl > 0m ? 999m : 0m,
            LargestLoss = result.LargestLoss,
            ConsecutiveLosses = 0
        };
    }

    public static StrategyPerformanceMetricsDto FromContext(BacktestContext context)
    {
        var closedTrades = context.Trades.Where(t => t.Status == TradeStatus.Closed).ToList();
        var winning = closedTrades.Where(t => t.NetPnl > 0).ToList();
        var losing = closedTrades.Where(t => t.NetPnl < 0).ToList();
        var grossProfit = winning.Sum(t => t.NetPnl);
        var grossLoss = losing.Sum(t => Math.Abs(t.NetPnl));
        var netPnl = context.Balance - context.Settings.InitialBalance;
        var winRate = closedTrades.Count > 0 ? (decimal)winning.Count / closedTrades.Count * 100m : 0m;

        return new StrategyPerformanceMetricsDto
        {
            NetPnlPercent = context.Settings.InitialBalance > 0 ? netPnl / context.Settings.InitialBalance * 100m : 0m,
            WinRate = winRate,
            ProfitFactor = grossLoss > 0 ? grossProfit / grossLoss : grossProfit > 0 ? 999m : 0m,
            MaxDrawdownPercent = context.MaxDrawdownPercent,
            TradeCount = closedTrades.Count,
            AverageR = CalculateAverageR(closedTrades),
            Expectancy = closedTrades.Count > 0 ? netPnl / closedTrades.Count : 0m,
            RecoveryFactor = context.MaxDrawdown > 0 ? netPnl / context.MaxDrawdown : netPnl > 0 ? 999m : 0m,
            LargestLoss = losing.Count > 0 ? losing.Min(t => t.NetPnl) : 0m,
            ConsecutiveLosses = CalculateMaxConsecutiveLosses(closedTrades)
        };
    }

    private static decimal CalculateAverageR(IReadOnlyList<Trade> trades)
    {
        var values = new List<decimal>();
        foreach (var trade in trades)
        {
            var risk = Math.Abs(trade.EntryPrice - trade.StopLoss);
            if (risk > 0m) values.Add(trade.NetPnl / risk);
        }

        return values.Count > 0 ? values.Average() : 0m;
    }

    private static int CalculateMaxConsecutiveLosses(IReadOnlyList<Trade> trades)
    {
        var max = 0;
        var current = 0;
        foreach (var trade in trades.OrderBy(t => t.OpenedAtUtc))
        {
            if (trade.NetPnl < 0)
            {
                current++;
                max = Math.Max(max, current);
            }
            else
            {
                current = 0;
            }
        }

        return max;
    }
}
