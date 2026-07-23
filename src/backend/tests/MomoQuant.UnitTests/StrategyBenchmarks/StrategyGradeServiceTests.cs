using MomoQuant.Application.StrategyBenchmarks;

namespace MomoQuant.UnitTests.StrategyBenchmarks;

public class StrategyGradeServiceTests
{
    private readonly StrategyGradeService _service = new();

    [Fact]
    public void Grade_ReturnsNa_WhenNoTrades()
    {
        var grade = _service.Grade(new StrategyBenchmarkMetrics
        {
            TotalTrades = 0
        });

        Assert.Equal("N/A", grade.Grade);
        Assert.Equal(0m, grade.Score);
    }

    [Fact]
    public void Grade_ReturnsHighGrade_ForStrongMetrics()
    {
        var grade = _service.Grade(new StrategyBenchmarkMetrics
        {
            NetPnlPercent = 12m,
            MaxDrawdownPercent = 2m,
            ProfitFactor = 1.8m,
            WinRatePercent = 58m,
            TotalTrades = 20,
            TotalSignals = 25,
            ApprovedSignals = 20,
            RejectedSignals = 2,
            MissedOrders = 1
        });

        Assert.True(grade.Score >= 70m);
        Assert.Contains(grade.Grade, ["A+", "A", "B+", "B"]);
        Assert.Contains(grade.Strengths, item => item.Contains("Positive", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Grade_AddsSmallSampleWarning()
    {
        var grade = _service.Grade(new StrategyBenchmarkMetrics
        {
            NetPnlPercent = 3m,
            MaxDrawdownPercent = 1m,
            ProfitFactor = 1.2m,
            WinRatePercent = 50m,
            TotalTrades = 3
        });

        Assert.Contains(grade.Warnings, item => item.Contains("Sample size", StringComparison.OrdinalIgnoreCase));
    }
}
