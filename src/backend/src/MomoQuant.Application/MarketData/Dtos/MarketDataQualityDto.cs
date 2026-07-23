namespace MomoQuant.Application.MarketData.Dtos;

public sealed class MarketDataQualityDto
{
    public long ExchangeId { get; init; }

    public long SymbolId { get; init; }

    public string Timeframe { get; init; } = string.Empty;

    public DateTime FromUtc { get; init; }

    public DateTime ToUtc { get; init; }

    public int TotalCandles { get; init; }

    public int ExpectedCandles { get; init; }

    public int MissingCandles { get; init; }

    public int DuplicateCandles { get; init; }

    public DateTime? FirstOpenTimeUtc { get; init; }

    public DateTime? LastOpenTimeUtc { get; init; }

    public decimal CoveragePercent { get; init; }

    public IReadOnlyList<MarketDataQualityGapDto> Gaps { get; init; } = [];
}

public sealed class MarketDataQualityGapDto
{
    public DateTime FromUtc { get; init; }

    public DateTime ToUtc { get; init; }

    public int MissingCount { get; init; }
}

public sealed class MarketDataSettingsDto
{
    public string HistoricalProvider { get; init; } = "Fake";
}
