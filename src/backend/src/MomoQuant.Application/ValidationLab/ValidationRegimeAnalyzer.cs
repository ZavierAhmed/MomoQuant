using System.Text.Json;
using MomoQuant.Domain.MarketData;

namespace MomoQuant.Application.ValidationLab;

public sealed class RegimeMetrics
{
    public decimal RealizedVolatility { get; init; }
    public decimal AverageCandleRangePercent { get; init; }
    public decimal DirectionalReturn { get; init; }
    public decimal UpCandlePercent { get; init; }
    public decimal TrendPersistenceProxy { get; init; }
    public int GapCount { get; init; }
    public decimal LargeRangeCandleFrequency { get; init; }
}

public sealed class RegimeComparisonResult
{
    public RegimeMetrics Training { get; init; } = new();
    public RegimeMetrics Validation { get; init; } = new();
    public string QualitativeComparison { get; init; } = "SimilarRegime";
    public string? Note { get; init; }
}

public static class ValidationRegimeAnalyzer
{
    public static RegimeComparisonResult Compare(IReadOnlyList<Candle> training, IReadOnlyList<Candle> validation)
    {
        var train = Compute(training);
        var val = Compute(validation);
        var volRatio = train.RealizedVolatility <= 0m
            ? 1m
            : val.RealizedVolatility / train.RealizedVolatility;
        var rangeRatio = train.AverageCandleRangePercent <= 0m
            ? 1m
            : val.AverageCandleRangePercent / train.AverageCandleRangePercent;

        string qualitative;
        string? note = null;
        if (volRatio >= 1.75m || volRatio <= 0.57m || rangeRatio >= 1.75m || rangeRatio <= 0.57m)
        {
            qualitative = "MateriallyDifferentRegime";
            note = "Validation occurred under materially different volatility conditions.";
        }
        else if (volRatio >= 1.25m || volRatio <= 0.8m || rangeRatio >= 1.25m || rangeRatio <= 0.8m)
        {
            qualitative = "ModeratelyDifferentRegime";
        }
        else
        {
            qualitative = "SimilarRegime";
        }

        return new RegimeComparisonResult
        {
            Training = train,
            Validation = val,
            QualitativeComparison = qualitative,
            Note = note
        };
    }

    public static RegimeMetrics Compute(IReadOnlyList<Candle> candles)
    {
        if (candles.Count < 2)
        {
            return new RegimeMetrics();
        }

        var ordered = candles.OrderBy(c => c.OpenTimeUtc).ToList();
        var returns = new List<decimal>();
        var ranges = new List<decimal>();
        var up = 0;
        var sameDir = 0;
        var gaps = 0;
        var largeRange = 0;

        for (var i = 1; i < ordered.Count; i++)
        {
            var prev = ordered[i - 1];
            var cur = ordered[i];
            if (prev.Close == 0) continue;
            var r = (cur.Close - prev.Close) / prev.Close;
            returns.Add(r);
            var rangePct = prev.Close == 0 ? 0m : (cur.High - cur.Low) / prev.Close * 100m;
            ranges.Add(rangePct);
            if (cur.Close > cur.Open) up++;
            if (i >= 2)
            {
                var prevR = returns[^2];
                if (Math.Sign(prevR) == Math.Sign(r) && r != 0) sameDir++;
            }

            if (rangePct >= 2m) largeRange++;
            // crude gap: open far from previous close
            if (Math.Abs(cur.Open - prev.Close) / prev.Close >= 0.005m) gaps++;
        }

        var mean = returns.Count > 0 ? returns.Average() : 0m;
        var variance = returns.Count > 1
            ? returns.Sum(x => (x - mean) * (x - mean)) / (returns.Count - 1)
            : 0m;
        var vol = (decimal)Math.Sqrt((double)variance) * 100m;

        return new RegimeMetrics
        {
            RealizedVolatility = Math.Round(vol, 6),
            AverageCandleRangePercent = ranges.Count > 0 ? Math.Round(ranges.Average(), 6) : 0m,
            DirectionalReturn = ordered[0].Close == 0
                ? 0m
                : Math.Round((ordered[^1].Close - ordered[0].Close) / ordered[0].Close * 100m, 6),
            UpCandlePercent = ordered.Count > 0 ? Math.Round((decimal)up / ordered.Count * 100m, 4) : 0m,
            TrendPersistenceProxy = returns.Count > 1
                ? Math.Round((decimal)sameDir / (returns.Count - 1) * 100m, 4)
                : 0m,
            GapCount = gaps,
            LargeRangeCandleFrequency = ordered.Count > 0
                ? Math.Round((decimal)largeRange / ordered.Count * 100m, 4)
                : 0m
        };
    }

    public static string Serialize(RegimeComparisonResult result) =>
        JsonSerializer.Serialize(result, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
}
