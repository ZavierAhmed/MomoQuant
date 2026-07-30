using System.Collections.Immutable;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Indicators;
using MomoQuant.Domain.MarketData;
using MomoQuant.Domain.Strategies;

namespace MomoQuant.Application.Strategies;

/// <summary>
/// Captures the exact StrategyContext delivered to a plugin at evaluation time.
/// The capture contains value snapshots only: it never exposes the mutable market-data
/// entities or parameter collection supplied by a caller.
/// </summary>
public interface IStrategyEvaluationCapture
{
    void Capture(StrategyContext context, ITradingStrategy strategy);
}

public sealed class StrategyEvaluationCaptureRecording : IStrategyEvaluationCapture
{
    private readonly List<StrategyEvaluationCaptureRecord> _records = [];

    /// <summary>Returns an immutable point-in-time view of the captured records.</summary>
    public IReadOnlyList<StrategyEvaluationCaptureRecord> Records => _records.ToImmutableArray();

    public void Capture(StrategyContext context, ITradingStrategy strategy)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(strategy);

        _records.Add(new StrategyEvaluationCaptureRecord(
            strategy.Code,
            context.EvaluatedAtUtc,
            context.Timeframe,
            context.HigherTimeframe,
            context.Candles,
            context.HigherTimeframeCandles,
            context.ExchangeId,
            context.SymbolId,
            context.Symbol,
            context.MarketRegime,
            context.CurrentCandleIndex,
            context.IndicatorSnapshot,
            context.StrategyParameters));
    }

    public void Clear() => _records.Clear();
}

/// <summary>Immutable copy of every canonical <see cref="Candle"/> field.</summary>
public sealed record StrategyEvaluationCandleSnapshot(
    long Id,
    long ExchangeId,
    long SymbolId,
    Timeframe Timeframe,
    DateTime OpenTimeUtc,
    DateTime CloseTimeUtc,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    decimal Volume,
    decimal QuoteVolume,
    int TradeCount,
    bool IsClosed,
    DateTime CreatedAtUtc)
{
    public static StrategyEvaluationCandleSnapshot Capture(Candle candle)
    {
        ArgumentNullException.ThrowIfNull(candle);
        return new(
            candle.Id,
            candle.ExchangeId,
            candle.SymbolId,
            candle.Timeframe,
            candle.OpenTimeUtc,
            candle.CloseTimeUtc,
            candle.Open,
            candle.High,
            candle.Low,
            candle.Close,
            candle.Volume,
            candle.QuoteVolume,
            candle.TradeCount,
            candle.IsClosed,
            candle.CreatedAtUtc);
    }
}

/// <summary>Immutable copy of every canonical <see cref="IndicatorSnapshot"/> field.</summary>
public sealed record StrategyEvaluationIndicatorSnapshot(
    long Id,
    long SymbolId,
    Timeframe Timeframe,
    long CandleId,
    DateTime CalculatedAtUtc,
    decimal? Ema20,
    decimal? Ema50,
    decimal? Ema200,
    decimal? Vwap,
    decimal? Rsi14,
    decimal? Atr14,
    decimal? VolumeSma20,
    decimal? SwingHigh,
    decimal? SwingLow,
    MarketStructure MarketStructure,
    decimal? BollingerMiddle20,
    decimal? BollingerUpper20,
    decimal? BollingerLower20,
    decimal? BollingerBandwidth20,
    decimal? DonchianHigh20,
    decimal? DonchianLow20,
    decimal? MacdLine,
    decimal? MacdSignal,
    decimal? MacdHistogram,
    decimal? Supertrend,
    int? SupertrendDirection,
    decimal? SupportLevel,
    decimal? ResistanceLevel,
    DateTime CreatedAtUtc)
{
    public static StrategyEvaluationIndicatorSnapshot? Capture(IndicatorSnapshot? snapshot) => snapshot is null
        ? null
        : new(
            snapshot.Id,
            snapshot.SymbolId,
            snapshot.Timeframe,
            snapshot.CandleId,
            snapshot.CalculatedAtUtc,
            snapshot.Ema20,
            snapshot.Ema50,
            snapshot.Ema200,
            snapshot.Vwap,
            snapshot.Rsi14,
            snapshot.Atr14,
            snapshot.VolumeSma20,
            snapshot.SwingHigh,
            snapshot.SwingLow,
            snapshot.MarketStructure,
            snapshot.BollingerMiddle20,
            snapshot.BollingerUpper20,
            snapshot.BollingerLower20,
            snapshot.BollingerBandwidth20,
            snapshot.DonchianHigh20,
            snapshot.DonchianLow20,
            snapshot.MacdLine,
            snapshot.MacdSignal,
            snapshot.MacdHistogram,
            snapshot.Supertrend,
            snapshot.SupertrendDirection,
            snapshot.SupportLevel,
            snapshot.ResistanceLevel,
            snapshot.CreatedAtUtc);
}

/// <summary>Immutable, production-time snapshot of a strategy evaluation context.</summary>
public sealed record StrategyEvaluationCaptureRecord
{
    public StrategyEvaluationCaptureRecord(
        StrategyCode StrategyCode,
        DateTime EvaluatedAtUtc,
        Timeframe ExecutionTimeframe,
        Timeframe HigherTimeframe,
        IReadOnlyList<Candle> Candles,
        IReadOnlyList<Candle> HigherTimeframeCandles,
        long? ExchangeId,
        long SymbolId,
        string? Symbol,
        MarketRegime MarketRegime,
        int? CurrentCandleIndex,
        IndicatorSnapshot? IndicatorSnapshot,
        IReadOnlyDictionary<string, string> StrategyParameters)
    {
        ArgumentNullException.ThrowIfNull(Candles);
        ArgumentNullException.ThrowIfNull(HigherTimeframeCandles);
        ArgumentNullException.ThrowIfNull(StrategyParameters);

        this.StrategyCode = StrategyCode;
        this.EvaluatedAtUtc = EvaluatedAtUtc;
        this.ExecutionTimeframe = ExecutionTimeframe;
        this.HigherTimeframe = HigherTimeframe;
        this.Candles = Candles.Select(StrategyEvaluationCandleSnapshot.Capture).ToImmutableArray();
        this.HigherTimeframeCandles = HigherTimeframeCandles.Select(StrategyEvaluationCandleSnapshot.Capture).ToImmutableArray();
        this.ExchangeId = ExchangeId;
        this.SymbolId = SymbolId;
        this.Symbol = Symbol;
        this.MarketRegime = MarketRegime;
        this.CurrentCandleIndex = CurrentCandleIndex;
        this.IndicatorSnapshot = StrategyEvaluationIndicatorSnapshot.Capture(IndicatorSnapshot);
        this.StrategyParameters = StrategyParameters.ToImmutableDictionary(StringComparer.Ordinal);
    }

    public StrategyCode StrategyCode { get; init; }
    public DateTime EvaluatedAtUtc { get; init; }
    public Timeframe ExecutionTimeframe { get; init; }
    public Timeframe HigherTimeframe { get; init; }
    public IReadOnlyList<StrategyEvaluationCandleSnapshot> Candles { get; init; } = ImmutableArray<StrategyEvaluationCandleSnapshot>.Empty;
    public IReadOnlyList<StrategyEvaluationCandleSnapshot> HigherTimeframeCandles { get; init; } = ImmutableArray<StrategyEvaluationCandleSnapshot>.Empty;
    public long? ExchangeId { get; init; }
    public long SymbolId { get; init; }
    public string? Symbol { get; init; }
    public MarketRegime MarketRegime { get; init; }
    public int? CurrentCandleIndex { get; init; }
    public StrategyEvaluationIndicatorSnapshot? IndicatorSnapshot { get; init; }
    public IReadOnlyDictionary<string, string> StrategyParameters { get; init; } = ImmutableDictionary<string, string>.Empty;
}
