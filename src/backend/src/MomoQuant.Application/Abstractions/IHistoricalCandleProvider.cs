using MomoQuant.Domain.Enums;

namespace MomoQuant.Application.Abstractions;

public interface IHistoricalCandleProvider
{
    Task<IReadOnlyList<HistoricalCandleDefinition>> GetCandlesAsync(
        string exchangeCode,
        string symbolName,
        Timeframe timeframe,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default);
}

public sealed class HistoricalCandleDefinition
{
    public required DateTime OpenTimeUtc { get; init; }
    public required DateTime CloseTimeUtc { get; init; }
    public required decimal Open { get; init; }
    public required decimal High { get; init; }
    public required decimal Low { get; init; }
    public required decimal Close { get; init; }
    public required decimal Volume { get; init; }
    public required decimal QuoteVolume { get; init; }
    public required int TradeCount { get; init; }
    public bool IsClosed { get; init; } = true;
}
