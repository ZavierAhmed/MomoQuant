using MomoQuant.Application.Reports.Dtos;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Trades;

namespace MomoQuant.Application.Reports;

public static class ReportMetrics
{
    public static decimal CalculateWinRate(int winningTrades, int losingTrades, int breakEvenTrades = 0)
    {
        var closedTrades = winningTrades + losingTrades + breakEvenTrades;
        return closedTrades > 0 ? (decimal)winningTrades / closedTrades * 100m : 0m;
    }

    public static decimal CalculateProfitFactor(decimal grossProfit, decimal grossLoss)
    {
        if (grossLoss > 0)
        {
            return grossProfit / grossLoss;
        }

        return grossProfit > 0 ? 999m : 0m;
    }

    public static (int Winning, int Losing, int BreakEven, decimal GrossProfit, decimal GrossLoss) AnalyzeTrades(
        IEnumerable<Trade> trades)
    {
        var closed = trades.Where(trade => trade.Status == TradeStatus.Closed).ToList();
        var winning = closed.Where(trade => trade.NetPnl > 0).ToList();
        var losing = closed.Where(trade => trade.NetPnl < 0).ToList();
        var breakEven = closed.Count - winning.Count - losing.Count;

        return (
            winning.Count,
            losing.Count,
            breakEven,
            winning.Sum(trade => trade.NetPnl),
            losing.Sum(trade => Math.Abs(trade.NetPnl)));
    }

    public static DrawdownReportDto CalculateDrawdown(IReadOnlyList<EquityCurvePointDto> points)
    {
        if (points.Count == 0)
        {
            return new DrawdownReportDto
            {
                MaxDrawdown = 0m,
                MaxDrawdownPercent = 0m,
                DrawdownSeries = []
            };
        }

        var maxDrawdown = points.Max(point => point.Drawdown);
        var maxDrawdownPercent = points.Max(point => point.DrawdownPercent);
        var peakPoint = points.OrderByDescending(point => point.Drawdown).First();

        var drawdownSeries = points
            .Where(point => point.Drawdown > 0)
            .OrderBy(point => point.TimestampUtc)
            .ToList();

        return new DrawdownReportDto
        {
            MaxDrawdown = maxDrawdown,
            MaxDrawdownPercent = maxDrawdownPercent,
            DrawdownStartUtc = drawdownSeries.FirstOrDefault()?.TimestampUtc,
            DrawdownEndUtc = peakPoint.TimestampUtc,
            RecoveryTimeCandles = drawdownSeries.Count > 0 ? drawdownSeries.Count : null,
            DrawdownSeries = drawdownSeries
        };
    }

    public static decimal CalculateAverageRewardRisk(IReadOnlyList<Trade> trades)
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
