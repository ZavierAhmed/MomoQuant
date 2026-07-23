using System.Text.Json;

namespace MomoQuant.Application.ValidationLab;

public sealed class LayerComparisonDto
{
    public string Layer { get; init; } = string.Empty;
    public decimal? CandidateCountChangePercent { get; init; }
    public decimal? OpportunityRateChangePercent { get; init; }
    public decimal? WinRateChangePoints { get; init; }
    public decimal? NetExpectancyChange { get; init; }
    public string NetExpectancyRetention { get; init; } = "NotMeaningful";
    public decimal? ProfitFactorChange { get; init; }
    public decimal? NetReturnChange { get; init; }
    public decimal? DrawdownChange { get; init; }
    public decimal? FeeImpactChange { get; init; }
    public decimal? SampleSizeRatio { get; init; }
    public bool PerformanceCollapseDetected { get; init; }
    public LayerSegmentMetrics? Training { get; init; }
    public LayerSegmentMetrics? Validation { get; init; }
}

public static class ValidationComparisonCalculator
{
    public static LayerComparisonDto Compare(string layer, LayerSegmentMetrics training, LayerSegmentMetrics validation)
    {
        var trainExp = training.NetExpectancyR ?? 0m;
        var valExp = validation.NetExpectancyR ?? 0m;
        var trainPf = training.ProfitFactor ?? 0m;
        var valPf = validation.ProfitFactor ?? 0m;

        string retention;
        if (trainExp <= 0m)
        {
            retention = "NotMeaningful";
        }
        else
        {
            retention = Math.Round(valExp / trainExp * 100m, 4).ToString("0.####");
        }

        var collapse = (trainExp > 0m && valExp < 0m)
            || (trainPf >= 1.0m && valPf > 0m && valPf < 1.0m && trainPf - valPf >= 0.3m);

        return new LayerComparisonDto
        {
            Layer = layer,
            CandidateCountChangePercent = PercentChange(training.CandidateCount, validation.CandidateCount),
            OpportunityRateChangePercent = PercentChange(training.OpportunityRatePer1000Candles, validation.OpportunityRatePer1000Candles),
            WinRateChangePoints = (validation.WinRate ?? 0m) - (training.WinRate ?? 0m),
            NetExpectancyChange = valExp - trainExp,
            NetExpectancyRetention = retention,
            ProfitFactorChange = valPf - trainPf,
            NetReturnChange = (validation.NetReturnPercent ?? 0m) - (training.NetReturnPercent ?? 0m),
            DrawdownChange = (validation.MaximumRealizedDrawdownPercent ?? 0m) - (training.MaximumRealizedDrawdownPercent ?? 0m),
            FeeImpactChange = (validation.FeeToGrossProfitPercent ?? 0m) - (training.FeeToGrossProfitPercent ?? 0m),
            SampleSizeRatio = training.ClosedTradeCount > 0
                ? Math.Round((decimal)validation.ClosedTradeCount / training.ClosedTradeCount, 4)
                : null,
            PerformanceCollapseDetected = collapse,
            Training = training,
            Validation = validation
        };
    }

    private static decimal? PercentChange(decimal from, decimal to)
    {
        if (from == 0m) return null;
        return Math.Round((to - from) / Math.Abs(from) * 100m, 4);
    }

    private static decimal? PercentChange(int from, int to) =>
        PercentChange((decimal)from, to);

    public static string Serialize(IEnumerable<LayerComparisonDto> comparisons) =>
        JsonSerializer.Serialize(comparisons, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
}
