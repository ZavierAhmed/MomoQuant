using MomoQuant.Application.StrategyLab;
using MomoQuant.Application.ValidationLab;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.MarketData;

namespace MomoQuant.UnitTests.StrategyLab;

public class Milestone230FingerprintAndPrefixTests
{
    [Fact]
    public void CandleContentFingerprint_IdenticalData_SameHash()
    {
        var a = new[] { Candle(1, 10m, 100m), Candle(2, 11m, 110m) };
        var b = new[] { Candle(1, 10m, 100m), Candle(2, 11m, 110m) };
        Assert.Equal(
            CandleContentFingerprintService.Compute(a).FullSha256,
            CandleContentFingerprintService.Compute(b).FullSha256);
    }

    [Fact]
    public void CandleContentFingerprint_ChangedClose_DifferentHash()
    {
        var a = new[] { Candle(1, 10m, 100m) };
        var b = new[] { Candle(1, 10m, 101m) };
        Assert.NotEqual(
            CandleContentFingerprintService.Compute(a).FullSha256,
            CandleContentFingerprintService.Compute(b).FullSha256);
    }

    [Fact]
    public void CandleContentFingerprint_ChangedVolume_DifferentHash()
    {
        var a = Candle(1, 10m, 100m);
        a.Volume = 1m;
        var b = Candle(1, 10m, 100m);
        b.Volume = 2m;
        Assert.NotEqual(
            CandleContentFingerprintService.Compute([a]).FullSha256,
            CandleContentFingerprintService.Compute([b]).FullSha256);
    }

    [Fact]
    public void CandleContentFingerprint_ReorderedInput_Canonicalized()
    {
        var a = new[] { Candle(2, 11m, 110m), Candle(1, 10m, 100m) };
        var b = new[] { Candle(1, 10m, 100m), Candle(2, 11m, 110m) };
        Assert.Equal(
            CandleContentFingerprintService.Compute(a).FullSha256,
            CandleContentFingerprintService.Compute(b).FullSha256);
    }

    [Fact]
    public void CandlePrefixView_ExposesOnlyVisibleCount_WithoutCopy()
    {
        var source = Enumerable.Range(0, 100).Select(i => Candle(i, 1m, 1m)).ToList();
        var view = new CandlePrefixView(source, 10);
        Assert.Equal(10, view.Count);
        Assert.Equal(source[9].OpenTimeUtc, view[9].OpenTimeUtc);
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = view[10]);
        view.SetVisibleCount(50);
        Assert.Equal(50, view.Count);
        Assert.Same(source[49], view[49]);
    }

    [Fact]
    public void ParameterFingerprint_EquivalentDecimalForms_Match()
    {
        var svc = new ValidationParameterFingerprintService();
        var a = svc.ComputeCanonical(new Dictionary<string, string> { ["lookback"] = "1" });
        var b = svc.ComputeCanonical(new Dictionary<string, string> { ["Lookback"] = "1.0" });
        var c = svc.ComputeCanonical(new Dictionary<string, string> { ["LOOKBACK"] = "1.000" });
        Assert.Equal(a.FullSha256, b.FullSha256);
        Assert.Equal(a.FullSha256, c.FullSha256);
        Assert.Equal(64, a.FullSha256.Length);
        Assert.Equal(16, a.ShortDisplayHash.Length);
    }

    [Fact]
    public void ParameterFingerprint_DifferentValues_Differ()
    {
        var svc = new ValidationParameterFingerprintService();
        var a = svc.ComputeCanonical(new Dictionary<string, string> { ["lookback"] = "1" });
        var b = svc.ComputeCanonical(new Dictionary<string, string> { ["lookback"] = "2" });
        Assert.NotEqual(a.FullSha256, b.FullSha256);
    }

    [Fact]
    public void LegacyMetadataFingerprint_StillReadable()
    {
        var legacy = ExperimentFingerprintBuilder.BuildCandleDatasetFingerprint(
            1, 2, "1h", DateTime.UtcNow, DateTime.UtcNow.AddDays(1), 10, DateTime.UtcNow, DateTime.UtcNow);
        Assert.Equal(16, legacy.Length);
    }

    private static Candle Candle(int hourOffset, decimal open, decimal close)
    {
        var openTime = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddHours(hourOffset);
        return new Candle
        {
            ExchangeId = 1,
            SymbolId = 1,
            Timeframe = Timeframe.H1,
            OpenTimeUtc = openTime,
            CloseTimeUtc = openTime.AddHours(1).AddMinutes(-1),
            Open = open,
            High = Math.Max(open, close),
            Low = Math.Min(open, close),
            Close = close,
            Volume = 1m,
            CreatedAtUtc = DateTime.UtcNow
        };
    }
}