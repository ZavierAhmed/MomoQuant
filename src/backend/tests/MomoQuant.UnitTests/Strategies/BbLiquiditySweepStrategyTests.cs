using MomoQuant.Application.Strategies.BbLiquiditySweep;
using MomoQuant.Application.Strategies.BbLiquiditySweep.Dtos;
using MomoQuant.Application.Strategies.Implementations;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.MarketData;

namespace MomoQuant.UnitTests.Strategies;

public class BbLiquiditySweepStrategyTests
{
    [Fact]
    public void Strategies_AreRegistered_WithExpectedCodes()
    {
        var baseStrategy = new BbLiquiditySweepCisdStrategy();
        var rsiStrategy = new BbLiquiditySweepCisdRsiPrimedStrategy();

        Assert.Equal(StrategyCode.BbLiquiditySweepCisd, baseStrategy.Code);
        Assert.Equal(StrategyCode.BbLiquiditySweepCisdRsiPrimed, rsiStrategy.Code);
        Assert.Equal("BB_LIQUIDITY_SWEEP_CISD", baseStrategy.Code.ToCode());
        Assert.Equal("BB_LIQUIDITY_SWEEP_CISD_RSI_PRIMED", rsiStrategy.Code.ToCode());
    }

    [Fact]
    public void LiquidityEngine_ReportsMomoApproximation()
    {
        var engine = new MomoLiquidityLineEngine();
        var info = engine.GetImplementationInfo();

        Assert.False(info.SourceCodeAvailable);
        Assert.Equal("MOMO_APPROXIMATION", info.ImplementationMode);
        Assert.Equal("#itsimpossible", info.ExternalIndicatorName);
    }

    [Fact]
    public void LiquiditySweepDetector_DetectsLongSweepBelowLowerBb()
    {
        var detector = new LiquiditySweepDetector();
        var level = new LiquidityLevelDto
        {
            Id = "sell-1",
            Timeframe = "3m",
            Direction = LiquidityDirection.SellSideLiquidity,
            Price = 100m,
            CreatedAtUtc = DateTime.UtcNow,
            ImplementationMode = "MOMO_APPROXIMATION",
            SourceIndicatorName = "#itsimpossible"
        };
        var bb = new BollingerBandsValueDto
        {
            TimeUtc = DateTime.UtcNow,
            Middle = 101m,
            Upper = 103m,
            Lower = 99m,
            Bandwidth = 4m,
            PercentB = 0.5m
        };
        var candle = BuildCandle(100.5m, 100.2m, 100.4m, 98.5m);

        var signal = detector.DetectLongSweep(candle, level, bb);

        Assert.True(signal.IsValidSweep);
        Assert.True(signal.SweptOutsideBb);
        Assert.True(signal.ClosedBackAcrossLiquidityLine);
    }

    [Fact]
    public void RsiPrimed_DefaultSignalUsesHaClose_AndFilters()
    {
        var indicator = new RsiPrimedChartPrimeIndicator(new FixedFallbackDominantCyclePeriodService(24));
        var candles = BuildTrendingCandles(80, startPrice: 100m, step: -0.2m);
        var series = indicator.CalculateSeries(candles, length: 8, smoothing: 2);

        Assert.NotEmpty(series);
        Assert.All(series.Skip(20), item =>
        {
            Assert.NotNull(item.RsiClose);
            Assert.NotNull(item.HaClose);
            Assert.Equal(RsiPrimedImplementationMode.DominantCycleFallback, item.ImplementationMode);
        });

        var last = series.Last(item => item.SignalValue.HasValue);
        Assert.NotNull(last.SignalValue);
    }

    [Fact]
    public void ChebyshevI_ProducesStableOutputAfterWarmup()
    {
        decimal? previous = null;
        for (var i = 0; i < 30; i++)
        {
            previous = RsiPrimedChartPrimeIndicator.ChebyshevI(50m + i, previous, 5, 0.05m);
        }

        Assert.NotNull(previous);
    }

    private static Candle BuildCandle(decimal open, decimal close, decimal high, decimal low) => new()
    {
        Id = 1,
        SymbolId = 1,
        Open = open,
        Close = close,
        High = high,
        Low = low,
        Volume = 100m,
        OpenTimeUtc = DateTime.UtcNow.AddMinutes(-3),
        CloseTimeUtc = DateTime.UtcNow,
        IsClosed = true
    };

    private static List<Candle> BuildTrendingCandles(int count, decimal startPrice, decimal step)
    {
        var candles = new List<Candle>(count);
        var price = startPrice;
        for (var i = 0; i < count; i++)
        {
            candles.Add(new Candle
            {
                Id = i + 1,
                SymbolId = 1,
                Open = price,
                Close = price + step,
                High = price + Math.Abs(step) + 0.5m,
                Low = price - Math.Abs(step) - 0.5m,
                Volume = 100m,
                OpenTimeUtc = DateTime.UtcNow.AddMinutes(-3 * (count - i)),
                CloseTimeUtc = DateTime.UtcNow.AddMinutes(-3 * (count - i - 1)),
                IsClosed = true
            });
            price += step;
        }

        return candles;
    }
}
