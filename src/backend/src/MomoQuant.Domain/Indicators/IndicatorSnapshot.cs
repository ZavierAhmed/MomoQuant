namespace MomoQuant.Domain.Indicators;

using MomoQuant.Domain.Common;
using MomoQuant.Domain.Enums;

public class IndicatorSnapshot : Entity
{
    public long SymbolId { get; set; }
    public Timeframe Timeframe { get; set; }
    public long CandleId { get; set; }
    public DateTime CalculatedAtUtc { get; set; }
    public decimal? Ema20 { get; set; }
    public decimal? Ema50 { get; set; }
    public decimal? Ema200 { get; set; }
    public decimal? Vwap { get; set; }
    public decimal? Rsi14 { get; set; }
    public decimal? Atr14 { get; set; }
    public decimal? VolumeSma20 { get; set; }
    public decimal? SwingHigh { get; set; }
    public decimal? SwingLow { get; set; }
    public MarketStructure MarketStructure { get; set; }
    public decimal? BollingerMiddle20 { get; set; }
    public decimal? BollingerUpper20 { get; set; }
    public decimal? BollingerLower20 { get; set; }
    public decimal? BollingerBandwidth20 { get; set; }
    public decimal? DonchianHigh20 { get; set; }
    public decimal? DonchianLow20 { get; set; }
    public decimal? MacdLine { get; set; }
    public decimal? MacdSignal { get; set; }
    public decimal? MacdHistogram { get; set; }
    public decimal? Supertrend { get; set; }
    public int? SupertrendDirection { get; set; }
    public decimal? SupportLevel { get; set; }
    public decimal? ResistanceLevel { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
