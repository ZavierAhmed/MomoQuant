using MomoQuant.Domain.Enums;

namespace MomoQuant.Application.MarketData;

public static class TimeframeParser
{
    private static readonly IReadOnlyDictionary<string, Timeframe> ApiToEnum = new Dictionary<string, Timeframe>(StringComparer.OrdinalIgnoreCase)
    {
        ["1m"] = Timeframe.M1,
        ["3m"] = Timeframe.M3,
        ["5m"] = Timeframe.M5,
        ["15m"] = Timeframe.M15,
        ["30m"] = Timeframe.M30,
        ["1h"] = Timeframe.H1,
        ["4h"] = Timeframe.H4,
        ["1d"] = Timeframe.D1,
        ["1w"] = Timeframe.W1
    };

    private static readonly IReadOnlyDictionary<Timeframe, string> EnumToApi = ApiToEnum
        .ToDictionary(pair => pair.Value, pair => pair.Key);

    public static bool TryParse(string? value, out Timeframe timeframe)
    {
        timeframe = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (!TimeframeNormalizer.TryNormalize(value, out var canonical))
        {
            return false;
        }

        return ApiToEnum.TryGetValue(canonical, out timeframe);
    }

    public static string ToApiString(Timeframe timeframe) =>
        EnumToApi.TryGetValue(timeframe, out var apiValue)
            ? apiValue
            : timeframe.ToString().ToLowerInvariant();

    /// <summary>Duration of one candle in minutes (1m=1, 1h=60, 1w=10080).</summary>
    public static int GetDurationMinutes(Timeframe timeframe) => (int)timeframe;

    public static bool TryGetDurationMinutes(string? apiValue, out int minutes)
    {
        minutes = 0;
        if (!TryParse(apiValue, out var timeframe))
        {
            return false;
        }

        minutes = GetDurationMinutes(timeframe);
        return true;
    }

    public static bool IsHigherTimeframe(string higherTimeframe, string primaryTimeframe)
    {
        if (!TryGetDurationMinutes(higherTimeframe, out var higherMinutes) ||
            !TryGetDurationMinutes(primaryTimeframe, out var primaryMinutes))
        {
            return false;
        }

        return higherMinutes > primaryMinutes;
    }
}
