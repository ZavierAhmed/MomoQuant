using MomoQuant.Domain.Enums;

namespace MomoQuant.Application.Indicators.Dtos;

public sealed class IndicatorSnapshotDto
{
    public required long Id { get; init; }
    public required long SymbolId { get; init; }
    public required string Timeframe { get; init; }
    public required long CandleId { get; init; }
    public required DateTime CalculatedAtUtc { get; init; }
    public decimal? Ema20 { get; init; }
    public decimal? Ema50 { get; init; }
    public decimal? Ema200 { get; init; }
    public decimal? Vwap { get; init; }
    public decimal? Rsi14 { get; init; }
    public decimal? Atr14 { get; init; }
    public decimal? VolumeSma20 { get; init; }
    public decimal? SwingHigh { get; init; }
    public decimal? SwingLow { get; init; }
    public required MarketStructure MarketStructure { get; init; }
    public decimal? BollingerMiddle20 { get; init; }
    public decimal? BollingerUpper20 { get; init; }
    public decimal? BollingerLower20 { get; init; }
    public decimal? BollingerBandwidth20 { get; init; }
    public decimal? DonchianHigh20 { get; init; }
    public decimal? DonchianLow20 { get; init; }
    public decimal? MacdLine { get; init; }
    public decimal? MacdSignal { get; init; }
    public decimal? MacdHistogram { get; init; }
    public decimal? Supertrend { get; init; }
    public int? SupertrendDirection { get; init; }
    public decimal? SupportLevel { get; init; }
    public decimal? ResistanceLevel { get; init; }
    public required DateTime CreatedAtUtc { get; init; }
}
