using MomoQuant.Application.Options;
using MomoQuant.Application.TradingSystems;
using MomoQuant.Application.TradingSystems.Dtos;

namespace MomoQuant.UnitTests.TradingSystems;

public class SkSequenceAnalyzerTests
{
    private static readonly DateTime Base = new(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);

    private static SwingPointDto Swing(string type, decimal price, int minutes) => new()
    {
        Id = $"{type}-{minutes}",
        CandleId = minutes,
        TimeUtc = Base.AddMinutes(minutes),
        Price = price,
        Type = type,
        Strength = 60m,
        LeftBars = 3,
        RightBars = 3,
        Source = "Wick"
    };

    [Fact]
    public void Analyze_BullishImpulseAndCorrection_ReturnsBullishCandidate()
    {
        var analyzer = new SkSequenceAnalyzer();
        var swings = new List<SwingPointDto>
        {
            Swing("Low", 100m, 0),
            Swing("High", 120m, 15),
            Swing("Low", 110m, 30)
        };

        var result = analyzer.Analyze(swings, currentPrice: 111m, directionMode: "Auto", new SkSystemSettings());

        var candidate = Assert.Single(result.Candidates);
        Assert.Equal("Bullish", candidate.Direction);
        Assert.Equal(100m, candidate.InvalidationLevel);
        Assert.Equal(130m, candidate.Target1);
        Assert.True(candidate.CorrectionZoneMin < candidate.CorrectionZoneMax);
        Assert.NotEmpty(result.FibonacciZones);
        Assert.Contains(result.FibonacciZones, zone => zone.IsGoldenPocket);
    }

    [Fact]
    public void Analyze_BearishImpulseAndCorrection_ReturnsBearishCandidate()
    {
        var analyzer = new SkSequenceAnalyzer();
        var swings = new List<SwingPointDto>
        {
            Swing("High", 120m, 0),
            Swing("Low", 100m, 15),
            Swing("High", 110m, 30)
        };

        var result = analyzer.Analyze(swings, currentPrice: 108m, directionMode: "Auto", new SkSystemSettings());

        var candidate = Assert.Single(result.Candidates);
        Assert.Equal("Bearish", candidate.Direction);
        Assert.Equal(120m, candidate.InvalidationLevel);
        Assert.Equal(90m, candidate.Target1);
    }

    [Fact]
    public void Analyze_BullishOnlyMode_ExcludesBearishCandidates()
    {
        var analyzer = new SkSequenceAnalyzer();
        var swings = new List<SwingPointDto>
        {
            Swing("High", 120m, 0),
            Swing("Low", 100m, 15),
            Swing("High", 110m, 30)
        };

        var result = analyzer.Analyze(swings, currentPrice: 108m, directionMode: "BullishOnly", new SkSystemSettings());

        Assert.Empty(result.Candidates);
    }
}
