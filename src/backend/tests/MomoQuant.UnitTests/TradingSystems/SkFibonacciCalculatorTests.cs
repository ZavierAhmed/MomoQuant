using MomoQuant.Application.TradingSystems;

namespace MomoQuant.UnitTests.TradingSystems;

public class SkFibonacciCalculatorTests
{
    [Fact]
    public void Retracement_BullishImpulse_CalculatesCorrectly()
    {
        // Impulse from low 100 to high 120.
        Assert.Equal(110m, SkFibonacciCalculator.Retracement(100m, 120m, 0.5m));
        Assert.Equal(112.36m, decimal.Round(SkFibonacciCalculator.Retracement(100m, 120m, 0.382m), 2));
        Assert.Equal(106.66m, decimal.Round(SkFibonacciCalculator.Retracement(100m, 120m, 0.667m), 2));
    }

    [Fact]
    public void Retracement_BearishImpulse_CalculatesCorrectly()
    {
        // Impulse from high 120 down to low 100.
        Assert.Equal(110m, SkFibonacciCalculator.Retracement(120m, 100m, 0.5m));
        // 0.618 retracement of a down-move sits above the low.
        Assert.True(SkFibonacciCalculator.Retracement(120m, 100m, 0.618m) > 100m);
    }

    [Fact]
    public void Extension_ProjectsFromCorrectionPoint()
    {
        // Impulse 100->120 (range 20), correction low 110, 1.0 extension => 130.
        Assert.Equal(130m, SkFibonacciCalculator.Extension(100m, 120m, 110m, 1.0m));
        Assert.Equal(142.36m, decimal.Round(SkFibonacciCalculator.Extension(100m, 120m, 110m, 1.618m), 2));
    }
}
