using MomoQuant.Domain.Backtesting;
using MomoQuant.Domain.Trades;

namespace MomoQuant.Application.Backtesting;

public interface IBacktestMetricsCalculator
{
    BacktestResult Calculate(BacktestContext context, long backtestRunId);

    IReadOnlyList<BacktestStrategyResult> CalculateStrategyBreakdown(BacktestContext context, long backtestRunId);

    IReadOnlyList<BacktestSymbolResult> CalculateSymbolBreakdown(BacktestContext context, long backtestRunId);
}

public sealed class BacktestMetricsCalculator : IBacktestMetricsCalculator
{
    public BacktestResult Calculate(BacktestContext context, long backtestRunId)
    {
        var closedTrades = context.Trades.Where(trade => trade.Status == Domain.Enums.TradeStatus.Closed).ToList();
        var winningTrades = closedTrades.Where(trade => trade.NetPnl > 0).ToList();
        var losingTrades = closedTrades.Where(trade => trade.NetPnl < 0).ToList();
        var breakEvenTrades = closedTrades.Count - winningTrades.Count - losingTrades.Count;

        var grossProfit = winningTrades.Sum(trade => trade.NetPnl);
        var grossLoss = losingTrades.Sum(trade => Math.Abs(trade.NetPnl));
        var netPnl = context.Balance - context.Settings.InitialBalance;
        var winRatePercent = closedTrades.Count > 0
            ? (decimal)winningTrades.Count / closedTrades.Count * 100m
            : 0m;

        return new BacktestResult
        {
            BacktestRunId = backtestRunId,
            InitialBalance = context.Settings.InitialBalance,
            FinalBalance = context.Balance,
            NetPnl = netPnl,
            NetPnlPercent = context.Settings.InitialBalance > 0 ? netPnl / context.Settings.InitialBalance * 100m : 0m,
            GrossProfit = grossProfit,
            GrossLoss = grossLoss,
            GrossPnl = grossProfit - grossLoss,
            TotalFees = context.TotalFees,
            TotalSlippage = context.TotalSlippage,
            MaxDrawdown = context.MaxDrawdown,
            MaxDrawdownPercent = context.MaxDrawdownPercent,
            TotalTrades = closedTrades.Count,
            WinningTrades = winningTrades.Count,
            LosingTrades = losingTrades.Count,
            BreakEvenTrades = breakEvenTrades,
            WinRate = winRatePercent,
            WinRatePercent = winRatePercent,
            ProfitFactor = grossLoss > 0 ? grossProfit / grossLoss : grossProfit > 0 ? 999m : 0m,
            Expectancy = closedTrades.Count > 0 ? netPnl / closedTrades.Count : 0m,
            AverageWin = winningTrades.Count > 0 ? winningTrades.Average(trade => trade.NetPnl) : 0m,
            AverageLoss = losingTrades.Count > 0 ? losingTrades.Average(trade => trade.NetPnl) : 0m,
            LargestWin = winningTrades.Count > 0 ? winningTrades.Max(trade => trade.NetPnl) : 0m,
            LargestLoss = losingTrades.Count > 0 ? losingTrades.Min(trade => trade.NetPnl) : 0m,
            AverageRewardRisk = CalculateAverageRewardRisk(closedTrades),
            TotalSignals = context.TotalSignals,
            ApprovedSignals = context.ApprovedSignals,
            RejectedSignals = context.RejectedSignals,
            MissedOrders = context.MissedOrders,
            FilledOrders = context.FilledOrders,
            CancelledOrders = context.CancelledOrders,
            CreatedAtUtc = DateTime.UtcNow
        };
    }

    public IReadOnlyList<BacktestStrategyResult> CalculateStrategyBreakdown(BacktestContext context, long backtestRunId) =>
        context.StrategyStats.Values.Select(stats =>
        {
            var winRate = stats.TotalTrades > 0 ? (decimal)stats.WinningTrades / stats.TotalTrades * 100m : 0m;
            return new BacktestStrategyResult
            {
                BacktestRunId = backtestRunId,
                StrategyCode = stats.StrategyCode,
                TotalSignals = stats.TotalSignals,
                ApprovedSignals = stats.ApprovedSignals,
                RejectedSignals = stats.RejectedSignals,
                TotalTrades = stats.TotalTrades,
                WinningTrades = stats.WinningTrades,
                LosingTrades = stats.LosingTrades,
                NetPnl = stats.NetPnl,
                WinRatePercent = winRate,
                ProfitFactor = stats.GrossLoss > 0 ? stats.GrossProfit / stats.GrossLoss : stats.GrossProfit > 0 ? 999m : 0m,
                MaxDrawdownPercent = stats.MaxDrawdownPercent,
                AverageConfidenceScore = stats.ConfidenceCount > 0 ? stats.ConfidenceTotal / stats.ConfidenceCount : 0m,
                CreatedAtUtc = DateTime.UtcNow
            };
        }).ToList();

    public IReadOnlyList<BacktestSymbolResult> CalculateSymbolBreakdown(BacktestContext context, long backtestRunId) =>
        context.SymbolStats.Values.Select(stats =>
        {
            var winRate = stats.TotalTrades > 0 ? (decimal)stats.WinningTrades / stats.TotalTrades * 100m : 0m;
            return new BacktestSymbolResult
            {
                BacktestRunId = backtestRunId,
                SymbolId = stats.SymbolId,
                Symbol = stats.Symbol,
                Timeframe = stats.Timeframe,
                TotalTrades = stats.TotalTrades,
                WinningTrades = stats.WinningTrades,
                LosingTrades = stats.LosingTrades,
                NetPnl = stats.NetPnl,
                WinRatePercent = winRate,
                ProfitFactor = stats.GrossLoss > 0 ? stats.GrossProfit / stats.GrossLoss : stats.GrossProfit > 0 ? 999m : 0m,
                MaxDrawdownPercent = stats.MaxDrawdownPercent,
                TotalFees = stats.TotalFees,
                MissedOrders = stats.MissedOrders,
                CreatedAtUtc = DateTime.UtcNow
            };
        }).ToList();

    private static decimal CalculateAverageRewardRisk(IReadOnlyList<Trade> trades)
    {
        var values = trades
            .Where(trade => trade.StopLoss != trade.EntryPrice)
            .Select(trade =>
            {
                var risk = Math.Abs(trade.EntryPrice - trade.StopLoss);
                var reward = Math.Abs(trade.TakeProfit - trade.EntryPrice);
                return risk > 0 ? reward / risk : 0m;
            })
            .Where(value => value > 0)
            .ToList();

        return values.Count > 0 ? values.Average() : 0m;
    }
}
