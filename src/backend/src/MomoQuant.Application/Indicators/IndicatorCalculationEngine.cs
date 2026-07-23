using MomoQuant.Application.Indicators.Calculators;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Indicators;
using MomoQuant.Domain.MarketData;

namespace MomoQuant.Application.Indicators;

public sealed class IndicatorCalculationEngine
{
    private const int Ema20Period = 20;
    private const int Ema50Period = 50;
    private const int Ema200Period = 200;

    private readonly EmaState _ema20 = new();
    private readonly EmaState _ema50 = new();
    private readonly EmaState _ema200 = new();
    private readonly RsiCalculator.State _rsi = new();
    private readonly AtrCalculator.State _atr = new();
    private readonly MacdCalculator.State _macd = new();
    private readonly SupertrendCalculator.State _supertrend = new();
    private readonly List<MarketStructureCalculator.SwingPoint> _swingHighs = [];
    private readonly List<MarketStructureCalculator.SwingPoint> _swingLows = [];

    public IndicatorSnapshot CalculateSnapshot(
        IReadOnlyList<Candle> candles,
        int index,
        int rangeStartIndex,
        Timeframe timeframe,
        DateTime calculatedAtUtc)
    {
        var candle = candles[index];
        var swingHigh = SwingPointCalculator.DetectSwingHigh(candles, index);
        var swingLow = SwingPointCalculator.DetectSwingLow(candles, index);

        if (swingHigh.HasValue)
        {
            _swingHighs.Add(new MarketStructureCalculator.SwingPoint { Price = swingHigh.Value, Index = index });
        }

        if (swingLow.HasValue)
        {
            _swingLows.Add(new MarketStructureCalculator.SwingPoint { Price = swingLow.Value, Index = index });
        }

        var atr = AtrCalculator.CalculateNext(candle, _atr);
        var (bollingerMiddle, bollingerUpper, bollingerLower, bollingerBandwidth) =
            BollingerCalculator.Calculate(candles, index);
        var (donchianHigh, donchianLow) = DonchianCalculator.Calculate(candles, index);
        var (macdLine, macdSignal, macdHistogram) = MacdCalculator.CalculateNext(candle.Close, _macd);
        var (supertrend, supertrendDirection) = SupertrendCalculator.CalculateNext(candle, atr, _supertrend);
        var (supportLevel, resistanceLevel) = SupportResistanceCalculator.Calculate(candles, index);

        return new IndicatorSnapshot
        {
            SymbolId = candle.SymbolId,
            Timeframe = timeframe,
            CandleId = candle.Id,
            CalculatedAtUtc = calculatedAtUtc,
            Ema20 = UpdateEma(_ema20, candle.Close, Ema20Period),
            Ema50 = UpdateEma(_ema50, candle.Close, Ema50Period),
            Ema200 = UpdateEma(_ema200, candle.Close, Ema200Period),
            Vwap = CalculateRangeVwap(candles, index, rangeStartIndex),
            Rsi14 = RsiCalculator.CalculateNext(candle.Close, _rsi),
            Atr14 = atr,
            VolumeSma20 = VolumeSmaCalculator.Calculate(candles, index),
            SwingHigh = swingHigh,
            SwingLow = swingLow,
            MarketStructure = MarketStructureCalculator.Classify(_swingHighs, _swingLows),
            BollingerMiddle20 = bollingerMiddle,
            BollingerUpper20 = bollingerUpper,
            BollingerLower20 = bollingerLower,
            BollingerBandwidth20 = bollingerBandwidth,
            DonchianHigh20 = donchianHigh,
            DonchianLow20 = donchianLow,
            MacdLine = macdLine,
            MacdSignal = macdSignal,
            MacdHistogram = macdHistogram,
            Supertrend = supertrend,
            SupertrendDirection = supertrendDirection == 0 ? null : supertrendDirection,
            SupportLevel = supportLevel,
            ResistanceLevel = resistanceLevel,
            CreatedAtUtc = calculatedAtUtc
        };
    }

    private static decimal? UpdateEma(EmaState state, decimal close, int period)
    {
        state.ClosesSeen++;
        if (state.Value is null)
        {
            state.CloseSum += close;
            if (state.ClosesSeen < period)
            {
                return null;
            }

            state.Value = state.CloseSum / period;
            return state.Value;
        }

        state.Value = EmaCalculator.CalculateNext(state.Value.Value, close, period);
        return state.Value;
    }

    private static decimal? CalculateRangeVwap(IReadOnlyList<Candle> candles, int index, int rangeStartIndex)
    {
        if (index < rangeStartIndex)
        {
            return null;
        }

        var rangeCandles = new List<Candle>();
        for (var i = rangeStartIndex; i <= index; i++)
        {
            rangeCandles.Add(candles[i]);
        }

        return VwapCalculator.CalculateCumulative(rangeCandles, rangeCandles.Count - 1);
    }

    private sealed class EmaState
    {
        public decimal? Value { get; set; }
        public int ClosesSeen { get; set; }
        public decimal CloseSum { get; set; }
    }
}
