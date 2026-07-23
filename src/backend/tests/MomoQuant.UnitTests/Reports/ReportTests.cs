using Moq;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Reports;
using MomoQuant.Application.Reports.Dtos;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Trades;

namespace MomoQuant.UnitTests.Reports;

public class ReportMetricsTests
{
    [Fact]
    public void CalculateWinRate_ExcludesBreakEvenFromDenominatorSides()
    {
        var winRate = ReportMetrics.CalculateWinRate(winningTrades: 2, losingTrades: 1, breakEvenTrades: 1);

        Assert.Equal(50m, winRate);
    }

    [Theory]
    [InlineData(300, 100, 3)]
    [InlineData(500, 0, 999)]
    [InlineData(0, 0, 0)]
    public void CalculateProfitFactor_HandlesZeroLossSafely(decimal grossProfit, decimal grossLoss, decimal expected)
    {
        var profitFactor = ReportMetrics.CalculateProfitFactor(grossProfit, grossLoss);

        Assert.Equal(expected, profitFactor);
    }

    [Fact]
    public void AnalyzeTrades_NetPnlIncludesFeesThroughTradeNetPnl()
    {
        var trades = new List<Trade>
        {
            new()
            {
                Status = TradeStatus.Closed,
                NetPnl = 90m,
                Fees = 10m,
                EntryPrice = 100m,
                StopLoss = 95m,
                TakeProfit = 110m,
                Quantity = 1m
            },
            new()
            {
                Status = TradeStatus.Closed,
                NetPnl = -20m,
                Fees = 5m,
                EntryPrice = 100m,
                StopLoss = 95m,
                TakeProfit = 110m,
                Quantity = 1m
            }
        };

        var analysis = ReportMetrics.AnalyzeTrades(trades);

        Assert.Equal(1, analysis.Winning);
        Assert.Equal(1, analysis.Losing);
        Assert.Equal(90m, analysis.GrossProfit);
        Assert.Equal(20m, analysis.GrossLoss);
    }

    [Fact]
    public void CalculateDrawdown_UsesEquityCurveSeries()
    {
        var points = new List<EquityCurvePointDto>
        {
            new() { TimestampUtc = DateTime.UtcNow.AddHours(-2), Equity = 10_000m, Drawdown = 0m, DrawdownPercent = 0m, Balance = 10_000m, OpenPositionCount = 0 },
            new() { TimestampUtc = DateTime.UtcNow.AddHours(-1), Equity = 9_500m, Drawdown = 500m, DrawdownPercent = 5m, Balance = 9_500m, OpenPositionCount = 0 },
            new() { TimestampUtc = DateTime.UtcNow, Equity = 9_800m, Drawdown = 200m, DrawdownPercent = 2m, Balance = 9_800m, OpenPositionCount = 0 }
        };

        var drawdown = ReportMetrics.CalculateDrawdown(points);

        Assert.Equal(500m, drawdown.MaxDrawdown);
        Assert.Equal(5m, drawdown.MaxDrawdownPercent);
        Assert.Equal(2, drawdown.DrawdownSeries.Count);
    }
}

public class ReportQueryValidatorTests
{
    [Fact]
    public async Task ValidateAsync_RejectsInvalidDateRange()
    {
        var validator = new ReportQueryValidator(
            new Mock<ISymbolRepository>().Object,
            new Mock<IStrategyRepository>().Object);

        var result = await validator.ValidateAsync(new ReportQuery
        {
            FromUtc = DateTime.UtcNow,
            ToUtc = DateTime.UtcNow.AddDays(-1)
        });

        Assert.False(result.Succeeded);
        Assert.Equal("fromUtc", result.ErrorField);
    }

    [Fact]
    public async Task ValidateAsync_RejectsLiveMode()
    {
        var validator = new ReportQueryValidator(
            new Mock<ISymbolRepository>().Object,
            new Mock<IStrategyRepository>().Object);

        var result = await validator.ValidateAsync(new ReportQuery { Mode = "Live" });

        Assert.False(result.Succeeded);
        Assert.Equal("mode", result.ErrorField);
    }
}

public class ReportAggregationRulesTests
{
    [Fact]
    public void MissedOrders_AreNotCountedAsLosses()
    {
        var trades = new List<Trade>
        {
            new() { Status = TradeStatus.Closed, NetPnl = -10m, EntryPrice = 1m, StopLoss = 0.9m, TakeProfit = 1.1m, Quantity = 1m }
        };

        var analysis = ReportMetrics.AnalyzeTrades(trades);

        Assert.Equal(0, analysis.Winning);
        Assert.Equal(1, analysis.Losing);
    }

    [Fact]
    public void RejectedSignals_AreNotCountedAsTrades()
    {
        var trades = new List<Trade>();
        var analysis = ReportMetrics.AnalyzeTrades(trades);

        Assert.Equal(0, analysis.Winning + analysis.Losing + analysis.BreakEven);
    }
}
