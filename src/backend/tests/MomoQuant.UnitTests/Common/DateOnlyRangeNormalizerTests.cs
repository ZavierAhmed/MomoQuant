using System.Text.Json;
using System.Text.Json.Serialization;
using MomoQuant.Application.Common;
using MomoQuant.Application.Optimization.Dtos;
using MomoQuant.Application.Strategies.Dtos;
using MomoQuant.Domain.Enums;

namespace MomoQuant.UnitTests.Common;

public class DateOnlyRangeNormalizerTests
{
    [Fact]
    public void Normalize_FromDateOnly_UsesUtcStartOfDay()
    {
        var request = new RunStrategyValidationRequest
        {
            StrategyCode = "VOLATILITY_GATED_SUPERTREND_MOMENTUM",
            ExchangeId = 1,
            SymbolId = 1,
            Timeframe = "15m",
            FromDate = "2026-06-01",
            ToDate = "2026-06-30",
            ValidationMode = ValidationMode.InSampleOutOfSample70_30,
            RiskProfileId = 1
        };

        var normalized = DateOnlyRangeNormalizer.Normalize(request);

        Assert.Equal(new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc), normalized.FromUtc);
        Assert.Equal(new DateTime(2026, 6, 30, 23, 59, 59, 999, DateTimeKind.Utc), normalized.ToUtc);
    }

    [Fact]
    public void ResolveFromUtc_DateTimeInput_NormalizesToStartOfDay()
    {
        var from = new DateTime(2026, 6, 1, 14, 30, 0, DateTimeKind.Utc);
        var normalized = DateOnlyRangeNormalizer.ResolveFromUtc(from, null);
        Assert.Equal(new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc), normalized);
    }

    [Fact]
    public void ResolveToUtc_DateTimeInput_NormalizesToEndOfDay()
    {
        var to = new DateTime(2026, 6, 30, 8, 15, 0, DateTimeKind.Utc);
        var normalized = DateOnlyRangeNormalizer.ResolveToUtc(to, null);
        Assert.Equal(new DateTime(2026, 6, 30, 23, 59, 59, 999, DateTimeKind.Utc), normalized);
    }
}

public class StrategyResearchRequestBindingTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public void DeserializeValidationRequest_AcceptsInSampleOutOfSample70_30()
    {
        const string json = """
            {
              "strategyCode": "VOLATILITY_GATED_SUPERTREND_MOMENTUM",
              "exchangeId": 1,
              "symbolId": 1,
              "timeframe": "15m",
              "fromDate": "2026-06-01",
              "toDate": "2026-06-30",
              "validationMode": "InSampleOutOfSample70_30",
              "riskProfileId": 1,
              "initialBalance": 10000
            }
            """;

        var request = JsonSerializer.Deserialize<RunStrategyValidationRequest>(json, JsonOptions);

        Assert.NotNull(request);
        Assert.Equal(ValidationMode.InSampleOutOfSample70_30, request!.ValidationMode);
        Assert.Equal("2026-06-01", request.FromDate);
    }

    [Fact]
    public void DeserializeValidationRequest_InvalidEnum_ThrowsJsonException()
    {
        const string json = """
            {
              "strategyCode": "VOLATILITY_GATED_SUPERTREND_MOMENTUM",
              "exchangeId": 1,
              "symbolId": 1,
              "timeframe": "15m",
              "fromUtc": "2026-06-01T00:00:00.000Z",
              "toUtc": "2026-06-30T23:59:59.999Z",
              "validationMode": "70/30 validation",
              "riskProfileId": 1
            }
            """;

        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<RunStrategyValidationRequest>(json, JsonOptions));
    }

    [Fact]
    public void DeserializeStrategyEvaluationRequest_AcceptsDirectBody()
    {
        const string json = """
            {
              "symbolId": 1,
              "timeframe": "15m",
              "candleId": 42,
              "marketRegime": "Trending",
              "strategyIds": [1]
            }
            """;

        var request = JsonSerializer.Deserialize<StrategyEvaluationRequest>(json, JsonOptions);

        Assert.NotNull(request);
        Assert.Equal("15m", request!.Timeframe);
        Assert.Equal(42, request.CandleId);
    }
}
