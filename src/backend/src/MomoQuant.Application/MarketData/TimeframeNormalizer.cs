namespace MomoQuant.Application.MarketData;

public static class TimeframeNormalizer
{
    public static readonly IReadOnlyList<string> SupportedCanonical =
    [
        "1m", "3m", "5m", "15m", "30m", "1h", "4h", "1d", "1w"
    ];

    private static readonly Dictionary<string, string> AliasToCanonical = BuildAliases();

    public static string Normalize(string input)
    {
        if (TryNormalize(input, out var canonical))
        {
            return canonical;
        }

        throw new ArgumentException(
            $"Unsupported timeframe '{input}'. Supported: {string.Join(", ", SupportedCanonical)}",
            nameof(input));
    }

    public static bool TryNormalize(string? input, out string canonical)
    {
        canonical = string.Empty;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var trimmed = input.Trim();
        if (AliasToCanonical.TryGetValue(NormalizeKey(trimmed), out var mapped))
        {
            canonical = mapped;
            return true;
        }

        return false;
    }

    public static bool IsSupported(string? input) => TryNormalize(input, out _);

    public static int ToMinutes(string canonical)
    {
        if (!TryNormalize(canonical, out var normalized) ||
            !TimeframeParser.TryParse(normalized, out var timeframe))
        {
            throw new ArgumentException($"Unsupported timeframe '{canonical}'.", nameof(canonical));
        }

        return TimeframeParser.GetDurationMinutes(timeframe);
    }

    public static string ToDisplayLabel(string canonical)
    {
        if (!TryNormalize(canonical, out var normalized))
        {
            return canonical;
        }

        return normalized switch
        {
            "1m" => "1 minute",
            "3m" => "3 minutes",
            "5m" => "5 minutes",
            "15m" => "15 minutes",
            "30m" => "30 minutes",
            "1h" => "1 hour",
            "4h" => "4 hours",
            "1d" => "1 day",
            "1w" => "1 week",
            _ => normalized
        };
    }

    public static string UnsupportedTimeframeMessage(string? received) =>
        $"Unsupported timeframe. Received: '{received ?? "(empty)"}'. Supported: [{string.Join(",", SupportedCanonical)}]";

    private static Dictionary<string, string> BuildAliases()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var canonical in SupportedCanonical)
        {
            map[NormalizeKey(canonical)] = canonical;
        }

        AddAlias(map, "1 minute", "1m");
        AddAlias(map, "1 minutes", "1m");
        AddAlias(map, "3 minute", "3m");
        AddAlias(map, "3 minutes", "3m");
        AddAlias(map, "5 minute", "5m");
        AddAlias(map, "5 minutes", "5m");
        AddAlias(map, "15 minute", "15m");
        AddAlias(map, "15 minutes", "15m");
        AddAlias(map, "30 minute", "30m");
        AddAlias(map, "30 minutes", "30m");
        AddAlias(map, "1 hour", "1h");
        AddAlias(map, "1 hours", "1h");
        AddAlias(map, "60m", "1h");
        AddAlias(map, "4 hour", "4h");
        AddAlias(map, "4 hours", "4h");
        AddAlias(map, "1 day", "1d");
        AddAlias(map, "1 week", "1w");
        return map;
    }

    private static void AddAlias(Dictionary<string, string> map, string alias, string canonical) =>
        map[NormalizeKey(alias)] = canonical;

    private static string NormalizeKey(string value) =>
        value.Trim().ToLowerInvariant();
}
