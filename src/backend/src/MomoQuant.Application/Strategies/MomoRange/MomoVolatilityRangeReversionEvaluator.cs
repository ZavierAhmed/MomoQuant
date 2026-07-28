using System.Text.Json;
using MomoQuant.Application.Indicators.Calculators;
using MomoQuant.Application.Strategies.Implementations;
using MomoQuant.Application.Strategies.PriceStructure;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.MarketData;

namespace MomoQuant.Application.Strategies.MomoRange;

public sealed class MomoVolatilityRangeReversionParameters
{
    public int RangeLookback { get; init; }
    public decimal MinRangeWidthAtr { get; init; }
    public decimal MaxRangeWidthAtr { get; init; }
    public int FastEmaPeriod { get; init; }
    public int SlowEmaPeriod { get; init; }
    public decimal MaxEmaSeparationAtr { get; init; }
    public int SlopeLookback { get; init; }
    public decimal MaxSlowEmaSlopeAtr { get; init; }
    public int FastAtrPeriod { get; init; }
    public int SlowAtrPeriod { get; init; }
    public decimal MinVolatilityRatio { get; init; }
    public decimal MaxVolatilityRatio { get; init; }
    public int RsiPeriod { get; init; }
    public decimal RsiOversold { get; init; }
    public decimal RsiOverbought { get; init; }
    public decimal BoundaryToleranceAtr { get; init; }
    public decimal MinimumWickPercent { get; init; }
    public decimal StopBufferAtr { get; init; }
    public decimal MinimumRewardRisk { get; init; }
    public string TargetMode { get; init; } = "RangeMidpoint";
    public decimal MinStrength { get; init; }

    public static MomoVolatilityRangeReversionParameters Read(IReadOnlyDictionary<string, string> parameters) => new()
    {
        RangeLookback = StrategyParameterReader.GetInt(parameters, "rangeLookback", 48),
        MinRangeWidthAtr = StrategyParameterReader.GetDecimal(parameters, "minRangeWidthAtr", 3.0m),
        MaxRangeWidthAtr = StrategyParameterReader.GetDecimal(parameters, "maxRangeWidthAtr", 12.0m),
        FastEmaPeriod = StrategyParameterReader.GetInt(parameters, "fastEmaPeriod", 20),
        SlowEmaPeriod = StrategyParameterReader.GetInt(parameters, "slowEmaPeriod", 50),
        MaxEmaSeparationAtr = StrategyParameterReader.GetDecimal(parameters, "maxEmaSeparationAtr", 0.50m),
        SlopeLookback = StrategyParameterReader.GetInt(parameters, "slopeLookback", 5),
        MaxSlowEmaSlopeAtr = StrategyParameterReader.GetDecimal(parameters, "maxSlowEmaSlopeAtr", 0.15m),
        FastAtrPeriod = StrategyParameterReader.GetInt(parameters, "fastAtrPeriod", 14),
        SlowAtrPeriod = StrategyParameterReader.GetInt(parameters, "slowAtrPeriod", 100),
        MinVolatilityRatio = StrategyParameterReader.GetDecimal(parameters, "minVolatilityRatio", 0.65m),
        MaxVolatilityRatio = StrategyParameterReader.GetDecimal(parameters, "maxVolatilityRatio", 1.25m),
        RsiPeriod = StrategyParameterReader.GetInt(parameters, "rsiPeriod", 14),
        RsiOversold = StrategyParameterReader.GetDecimal(parameters, "rsiOversold", 35m),
        RsiOverbought = StrategyParameterReader.GetDecimal(parameters, "rsiOverbought", 65m),
        BoundaryToleranceAtr = StrategyParameterReader.GetDecimal(parameters, "boundaryToleranceAtr", 0.15m),
        MinimumWickPercent = StrategyParameterReader.GetDecimal(parameters, "minimumWickPercent", 30m),
        StopBufferAtr = StrategyParameterReader.GetDecimal(parameters, "stopBufferAtr", 0.25m),
        MinimumRewardRisk = StrategyParameterReader.GetDecimal(parameters, "minimumRewardRisk", 1.25m),
        TargetMode = StrategyParameterReader.GetString(parameters, "targetMode", "RangeMidpoint"),
        MinStrength = StrategyParameterReader.GetDecimal(parameters, "minStrength", 65m)
    };
}

public sealed class MomoVolatilityRangeReversionCandidate
{
    public required TradeDirection Direction { get; init; }
    public required decimal EntryPrice { get; init; }
    public required decimal StopLoss { get; init; }
    public required decimal TakeProfit { get; init; }
    public required decimal Strength { get; init; }
    public required decimal RewardRisk { get; init; }
    public required string Reason { get; init; }
    public required string SetupFingerprint { get; init; }
    public required string RawDataJson { get; init; }
}

public static class MomoVolatilityRangeReversionEvaluator
{
    public const string StrategyVersion = "1.0.0";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static (MomoVolatilityRangeReversionCandidate? Candidate, string Reason) EvaluateAtCurrentCandle(
        IReadOnlyList<Candle> candles,
        IReadOnlyDictionary<string, string> parameters,
        IReadOnlySet<string> seenFingerprints,
        string strategyCode,
        long symbolId,
        string timeframe)
    {
        var settings = MomoVolatilityRangeReversionParameters.Read(parameters);
        var minimumCandles = MinimumRequiredCandles(settings);
        if (candles.Count < minimumCandles)
        {
            return (null, MomoVolatilityRangeRejectionCodes.InsufficientData);
        }

        var currentIndex = candles.Count - 1;
        var current = candles[currentIndex];

        if (!TryComputeIndicators(candles, currentIndex, settings, out var indicators))
        {
            return (null, MomoVolatilityRangeRejectionCodes.InsufficientData);
        }

        if (!TryBuildRange(candles, currentIndex, settings.RangeLookback, out var range))
        {
            return (null, MomoVolatilityRangeRejectionCodes.InsufficientData);
        }

        var rangeQualification = QualifyRange(
            candles,
            currentIndex,
            settings,
            range,
            indicators);
        if (rangeQualification is not null)
        {
            return (null, rangeQualification);
        }

        var (longCandidate, longReason) = TryBuildLongSetup(
            current,
            settings,
            range,
            indicators,
            seenFingerprints,
            strategyCode,
            symbolId,
            timeframe);

        if (longCandidate is not null)
        {
            return (longCandidate, MomoVolatilityRangeRejectionCodes.EntryConfirmed);
        }

        var (shortCandidate, shortReason) = TryBuildShortSetup(
            current,
            settings,
            range,
            indicators,
            seenFingerprints,
            strategyCode,
            symbolId,
            timeframe);

        if (shortCandidate is not null)
        {
            return (shortCandidate, MomoVolatilityRangeRejectionCodes.EntryConfirmed);
        }

        return (null, PickCloserReason(longReason, shortReason));
    }

    private static int MinimumRequiredCandles(MomoVolatilityRangeReversionParameters settings) =>
        Math.Max(settings.SlowAtrPeriod, settings.SlowEmaPeriod)
        + settings.RangeLookback
        + settings.SlopeLookback
        + 2;

    private sealed record RangeBounds(decimal High, decimal Low, decimal Midpoint, decimal Width);

    private sealed record IndicatorValues(
        decimal FastEma,
        decimal SlowEma,
        decimal FastAtr,
        decimal SlowAtr,
        decimal VolatilityRatio,
        decimal EmaSeparationAtr,
        decimal SlowEmaSlopeAtr,
        decimal Rsi);

    private sealed record StrengthBreakdown(
        decimal Base,
        decimal RangeQuality,
        decimal VolatilityQuality,
        decimal RsiExtremity,
        decimal WickQuality,
        decimal RewardRiskQuality,
        decimal TrendFlatness,
        decimal Total);

    private static bool TryBuildRange(
        IReadOnlyList<Candle> candles,
        int currentIndex,
        int rangeLookback,
        out RangeBounds range)
    {
        range = default!;
        var start = currentIndex - rangeLookback;
        if (start < 0)
        {
            return false;
        }

        var high = decimal.MinValue;
        var low = decimal.MaxValue;
        for (var i = start; i < currentIndex; i++)
        {
            high = Math.Max(high, candles[i].High);
            low = Math.Min(low, candles[i].Low);
        }

        if (high <= low)
        {
            return false;
        }

        var midpoint = (high + low) / 2m;
        range = new RangeBounds(high, low, midpoint, high - low);
        return true;
    }

    private static string? QualifyRange(
        IReadOnlyList<Candle> candles,
        int currentIndex,
        MomoVolatilityRangeReversionParameters settings,
        RangeBounds range,
        IndicatorValues indicators)
    {
        if (indicators.FastAtr <= 0m || indicators.SlowAtr <= 0m)
        {
            return MomoVolatilityRangeRejectionCodes.InsufficientData;
        }

        var widthAtr = range.Width / indicators.FastAtr;
        if (widthAtr < settings.MinRangeWidthAtr)
        {
            return MomoVolatilityRangeRejectionCodes.RangeTooNarrow;
        }

        if (widthAtr > settings.MaxRangeWidthAtr)
        {
            return MomoVolatilityRangeRejectionCodes.RangeTooWide;
        }

        if (indicators.EmaSeparationAtr > settings.MaxEmaSeparationAtr
            || Math.Abs(indicators.SlowEmaSlopeAtr) > settings.MaxSlowEmaSlopeAtr)
        {
            return MomoVolatilityRangeRejectionCodes.TrendFilterFailed;
        }

        if (indicators.VolatilityRatio < settings.MinVolatilityRatio)
        {
            return MomoVolatilityRangeRejectionCodes.VolatilityTooLow;
        }

        if (indicators.VolatilityRatio > settings.MaxVolatilityRatio)
        {
            return MomoVolatilityRangeRejectionCodes.VolatilityTooHigh;
        }

        if (HasConfirmedExpansionBreakout(candles, currentIndex, settings.SlopeLookback, range))
        {
            return MomoVolatilityRangeRejectionCodes.TrendFilterFailed;
        }

        return null;
    }

    private static bool HasConfirmedExpansionBreakout(
        IReadOnlyList<Candle> candles,
        int currentIndex,
        int slopeLookback,
        RangeBounds range)
    {
        var start = Math.Max(0, currentIndex - slopeLookback);
        for (var i = start; i < currentIndex; i++)
        {
            var close = candles[i].Close;
            if (close > range.High || close < range.Low)
            {
                return true;
            }
        }

        return false;
    }

    private static (MomoVolatilityRangeReversionCandidate? Candidate, string Reason) TryBuildLongSetup(
        Candle current,
        MomoVolatilityRangeReversionParameters settings,
        RangeBounds range,
        IndicatorValues indicators,
        IReadOnlySet<string> seenFingerprints,
        string strategyCode,
        long symbolId,
        string timeframe)
    {
        var tolerance = settings.BoundaryToleranceAtr * indicators.FastAtr;
        if (current.Low >= range.Low)
        {
            return (null, MomoVolatilityRangeRejectionCodes.NoBoundaryProbe);
        }

        if (current.Close <= range.Low - tolerance || current.Close > range.High)
        {
            return (null, MomoVolatilityRangeRejectionCodes.CloseDidNotReclaim);
        }

        if (indicators.Rsi > settings.RsiOversold)
        {
            return (null, MomoVolatilityRangeRejectionCodes.RsiNotExtreme);
        }

        var lowerWickPercent = StrategyCandleHelper.WickPercent(current, lowerWick: true);
        if (lowerWickPercent < settings.MinimumWickPercent)
        {
            return (null, MomoVolatilityRangeRejectionCodes.WickConfirmationMissing);
        }

        var entry = current.Close;
        var stop = current.Low - (settings.StopBufferAtr * indicators.FastAtr);
        if (stop <= 0m || stop >= entry)
        {
            return (null, MomoVolatilityRangeRejectionCodes.InvalidStop);
        }

        var takeProfit = ResolveTarget(settings.TargetMode, range, TradeDirection.Long);
        var risk = entry - stop;
        var reward = takeProfit - entry;
        if (risk <= 0m || reward <= 0m)
        {
            return (null, MomoVolatilityRangeRejectionCodes.RewardRiskInsufficient);
        }

        var rewardRisk = reward / risk;
        if (rewardRisk < settings.MinimumRewardRisk)
        {
            return (null, MomoVolatilityRangeRejectionCodes.RewardRiskInsufficient);
        }

        var fingerprint = BuildFingerprint(
            strategyCode,
            symbolId,
            timeframe,
            TradeDirection.Long,
            range,
            current.OpenTimeUtc);
        if (seenFingerprints.Contains(fingerprint))
        {
            return (null, MomoVolatilityRangeRejectionCodes.DuplicateSetup);
        }

        var strengthBreakdown = CalculateStrength(
            settings,
            range,
            indicators,
            lowerWickPercent,
            rewardRisk,
            isLong: true);
        var strength = StrategyStrengthHelper.ResolveStrength(strengthBreakdown.Total, settings.MinStrength);
        var rawDataJson = BuildRawDataJson(
            TradeDirection.Long,
            range,
            indicators,
            lowerWickPercent,
            rewardRisk,
            entry,
            stop,
            takeProfit,
            strengthBreakdown);

        return (new MomoVolatilityRangeReversionCandidate
        {
            Direction = TradeDirection.Long,
            EntryPrice = entry,
            StopLoss = stop,
            TakeProfit = takeProfit,
            Strength = strength,
            RewardRisk = rewardRisk,
            Reason = "Long volatility range reversion after lower-boundary sweep and reclaim.",
            SetupFingerprint = fingerprint,
            RawDataJson = rawDataJson
        }, MomoVolatilityRangeRejectionCodes.EntryConfirmed);
    }

    private static (MomoVolatilityRangeReversionCandidate? Candidate, string Reason) TryBuildShortSetup(
        Candle current,
        MomoVolatilityRangeReversionParameters settings,
        RangeBounds range,
        IndicatorValues indicators,
        IReadOnlySet<string> seenFingerprints,
        string strategyCode,
        long symbolId,
        string timeframe)
    {
        var tolerance = settings.BoundaryToleranceAtr * indicators.FastAtr;
        if (current.High <= range.High)
        {
            return (null, MomoVolatilityRangeRejectionCodes.NoBoundaryProbe);
        }

        if (current.Close >= range.High + tolerance || current.Close < range.Low)
        {
            return (null, MomoVolatilityRangeRejectionCodes.CloseDidNotReclaim);
        }

        if (indicators.Rsi < settings.RsiOverbought)
        {
            return (null, MomoVolatilityRangeRejectionCodes.RsiNotExtreme);
        }

        var upperWickPercent = StrategyCandleHelper.WickPercent(current, lowerWick: false);
        if (upperWickPercent < settings.MinimumWickPercent)
        {
            return (null, MomoVolatilityRangeRejectionCodes.WickConfirmationMissing);
        }

        var entry = current.Close;
        var stop = current.High + (settings.StopBufferAtr * indicators.FastAtr);
        if (stop <= entry)
        {
            return (null, MomoVolatilityRangeRejectionCodes.InvalidStop);
        }

        var takeProfit = ResolveTarget(settings.TargetMode, range, TradeDirection.Short);
        var risk = stop - entry;
        var reward = entry - takeProfit;
        if (risk <= 0m || reward <= 0m)
        {
            return (null, MomoVolatilityRangeRejectionCodes.RewardRiskInsufficient);
        }

        var rewardRisk = reward / risk;
        if (rewardRisk < settings.MinimumRewardRisk)
        {
            return (null, MomoVolatilityRangeRejectionCodes.RewardRiskInsufficient);
        }

        var fingerprint = BuildFingerprint(
            strategyCode,
            symbolId,
            timeframe,
            TradeDirection.Short,
            range,
            current.OpenTimeUtc);
        if (seenFingerprints.Contains(fingerprint))
        {
            return (null, MomoVolatilityRangeRejectionCodes.DuplicateSetup);
        }

        var strengthBreakdown = CalculateStrength(
            settings,
            range,
            indicators,
            upperWickPercent,
            rewardRisk,
            isLong: false);
        var strength = StrategyStrengthHelper.ResolveStrength(strengthBreakdown.Total, settings.MinStrength);
        var rawDataJson = BuildRawDataJson(
            TradeDirection.Short,
            range,
            indicators,
            upperWickPercent,
            rewardRisk,
            entry,
            stop,
            takeProfit,
            strengthBreakdown);

        return (new MomoVolatilityRangeReversionCandidate
        {
            Direction = TradeDirection.Short,
            EntryPrice = entry,
            StopLoss = stop,
            TakeProfit = takeProfit,
            Strength = strength,
            RewardRisk = rewardRisk,
            Reason = "Short volatility range reversion after upper-boundary sweep and reclaim.",
            SetupFingerprint = fingerprint,
            RawDataJson = rawDataJson
        }, MomoVolatilityRangeRejectionCodes.EntryConfirmed);
    }

    private static decimal ResolveTarget(string targetMode, RangeBounds range, TradeDirection direction)
    {
        if (string.Equals(targetMode, "RangeMidpoint", StringComparison.OrdinalIgnoreCase))
        {
            return range.Midpoint;
        }

        return range.Midpoint;
    }

    private static StrengthBreakdown CalculateStrength(
        MomoVolatilityRangeReversionParameters settings,
        RangeBounds range,
        IndicatorValues indicators,
        decimal wickPercent,
        decimal rewardRisk,
        bool isLong)
    {
        var baseStrength = settings.MinStrength;
        var widthAtr = range.Width / indicators.FastAtr;
        var widthMid = (settings.MinRangeWidthAtr + settings.MaxRangeWidthAtr) / 2m;
        var widthSpan = Math.Max(0.01m, settings.MaxRangeWidthAtr - settings.MinRangeWidthAtr);
        var rangeQuality = Math.Clamp(8m - Math.Abs(widthAtr - widthMid) / widthSpan * 8m, 0m, 8m);

        var volMid = (settings.MinVolatilityRatio + settings.MaxVolatilityRatio) / 2m;
        var volSpan = Math.Max(0.01m, settings.MaxVolatilityRatio - settings.MinVolatilityRatio);
        var volatilityQuality = Math.Clamp(6m - Math.Abs(indicators.VolatilityRatio - volMid) / volSpan * 6m, 0m, 6m);

        var rsiExtremity = isLong
            ? Math.Clamp((settings.RsiOversold - indicators.Rsi) / Math.Max(1m, settings.RsiOversold) * 10m, 0m, 10m)
            : Math.Clamp((indicators.Rsi - settings.RsiOverbought) / Math.Max(1m, 100m - settings.RsiOverbought) * 10m, 0m, 10m);

        var wickQuality = Math.Clamp((wickPercent - settings.MinimumWickPercent) / Math.Max(1m, 100m - settings.MinimumWickPercent) * 8m, 0m, 8m);
        var rewardRiskQuality = Math.Clamp((rewardRisk - settings.MinimumRewardRisk) * 4m, 0m, 8m);
        var trendFlatness = Math.Clamp(
            8m
            - indicators.EmaSeparationAtr / Math.Max(0.01m, settings.MaxEmaSeparationAtr) * 4m
            - Math.Abs(indicators.SlowEmaSlopeAtr) / Math.Max(0.01m, settings.MaxSlowEmaSlopeAtr) * 4m,
            0m,
            8m);

        var total = baseStrength
            + rangeQuality
            + volatilityQuality
            + rsiExtremity
            + wickQuality
            + rewardRiskQuality
            + trendFlatness;

        return new StrengthBreakdown(
            baseStrength,
            rangeQuality,
            volatilityQuality,
            rsiExtremity,
            wickQuality,
            rewardRiskQuality,
            trendFlatness,
            total);
    }

    private static string BuildRawDataJson(
        TradeDirection direction,
        RangeBounds range,
        IndicatorValues indicators,
        decimal wickPercent,
        decimal rewardRisk,
        decimal entry,
        decimal stop,
        decimal takeProfit,
        StrengthBreakdown strengthBreakdown) =>
        JsonSerializer.Serialize(new
        {
            version = StrategyVersion,
            direction = direction.ToString(),
            rangeHigh = range.High,
            rangeLow = range.Low,
            rangeMidpoint = range.Midpoint,
            rangeWidth = range.Width,
            volatilityRatio = indicators.VolatilityRatio,
            emaSeparationAtr = indicators.EmaSeparationAtr,
            slowEmaSlopeAtr = indicators.SlowEmaSlopeAtr,
            fastEma = indicators.FastEma,
            slowEma = indicators.SlowEma,
            fastAtr = indicators.FastAtr,
            slowAtr = indicators.SlowAtr,
            rsi = indicators.Rsi,
            wickPercent,
            rewardRisk,
            entry,
            stop,
            takeProfit,
            strengthBreakdown = new
            {
                strengthBreakdown.Base,
                strengthBreakdown.RangeQuality,
                strengthBreakdown.VolatilityQuality,
                strengthBreakdown.RsiExtremity,
                strengthBreakdown.WickQuality,
                strengthBreakdown.RewardRiskQuality,
                strengthBreakdown.TrendFlatness,
                strengthBreakdown.Total
            }
        }, JsonOptions);

    private static string BuildFingerprint(
        string strategyCode,
        long symbolId,
        string timeframe,
        TradeDirection direction,
        RangeBounds range,
        DateTime candleOpenTimeUtc)
    {
        var high = Math.Round(range.High, 8);
        var low = Math.Round(range.Low, 8);
        var ts = candleOpenTimeUtc.ToString("yyyyMMdd'T'HHmm");
        var raw = $"{strategyCode}|v{StrategyVersion}|{symbolId}|{timeframe}|{direction}|RH_{high}|RL_{low}|{ts}";
        return SetupFingerprintHasher.Hash(raw);
    }

    private static bool TryComputeIndicators(
        IReadOnlyList<Candle> candles,
        int index,
        MomoVolatilityRangeReversionParameters settings,
        out IndicatorValues indicators)
    {
        indicators = default!;
        if (index < settings.SlowEmaPeriod || index < settings.SlowAtrPeriod || index < settings.RsiPeriod)
        {
            return false;
        }

        if (!TryComputeEma(candles, index, settings.FastEmaPeriod, out var fastEma)
            || !TryComputeEma(candles, index, settings.SlowEmaPeriod, out var slowEma)
            || !TryComputeAtr(candles, index, settings.FastAtrPeriod, out var fastAtr)
            || !TryComputeAtr(candles, index, settings.SlowAtrPeriod, out var slowAtr)
            || !TryComputeRsi(candles, index, settings.RsiPeriod, out var rsi))
        {
            return false;
        }

        if (fastAtr <= 0m || slowAtr <= 0m)
        {
            return false;
        }

        if (index < settings.SlopeLookback
            || !TryComputeEma(candles, index - settings.SlopeLookback, settings.SlowEmaPeriod, out var priorSlowEma))
        {
            return false;
        }

        var emaSeparationAtr = Math.Abs(fastEma - slowEma) / fastAtr;
        var slowEmaSlopeAtr = (slowEma - priorSlowEma) / fastAtr;
        var volatilityRatio = fastAtr / slowAtr;

        indicators = new IndicatorValues(
            fastEma,
            slowEma,
            fastAtr,
            slowAtr,
            volatilityRatio,
            emaSeparationAtr,
            slowEmaSlopeAtr,
            rsi);
        return true;
    }

    private static bool TryComputeEma(IReadOnlyList<Candle> candles, int index, int period, out decimal ema)
    {
        ema = default;
        if (index + 1 < period)
        {
            return false;
        }

        var sum = 0m;
        for (var i = 0; i < period; i++)
        {
            sum += candles[i].Close;
        }

        ema = sum / period;
        for (var i = period; i <= index; i++)
        {
            ema = EmaCalculator.CalculateNext(ema, candles[i].Close, period);
        }

        return true;
    }

    private static bool TryComputeAtr(IReadOnlyList<Candle> candles, int index, int period, out decimal atr)
    {
        atr = default;
        if (index + 1 < period)
        {
            return false;
        }

        var state = new AtrCalculator.State();
        decimal? latest = null;
        for (var i = 0; i <= index; i++)
        {
            latest = AtrCalculator.CalculateNext(candles[i], state, period);
        }

        if (latest is null || latest.Value <= 0m)
        {
            return false;
        }

        atr = latest.Value;
        return true;
    }

    private static bool TryComputeRsi(IReadOnlyList<Candle> candles, int index, int period, out decimal rsi)
    {
        rsi = default;
        if (index + 1 < period + 1)
        {
            return false;
        }

        var state = new RsiCalculator.State();
        decimal? latest = null;
        for (var i = 0; i <= index; i++)
        {
            latest = RsiCalculator.CalculateNext(candles[i].Close, state, period);
        }

        if (latest is null)
        {
            return false;
        }

        rsi = latest.Value;
        return true;
    }

    private static string PickCloserReason(string first, string second)
    {
        var priority = new[]
        {
            MomoVolatilityRangeRejectionCodes.DuplicateSetup,
            MomoVolatilityRangeRejectionCodes.InvalidStop,
            MomoVolatilityRangeRejectionCodes.RewardRiskInsufficient,
            MomoVolatilityRangeRejectionCodes.WickConfirmationMissing,
            MomoVolatilityRangeRejectionCodes.RsiNotExtreme,
            MomoVolatilityRangeRejectionCodes.CloseDidNotReclaim,
            MomoVolatilityRangeRejectionCodes.NoBoundaryProbe
        };

        foreach (var reason in priority)
        {
            if (string.Equals(first, reason, StringComparison.Ordinal))
            {
                return first;
            }

            if (string.Equals(second, reason, StringComparison.Ordinal))
            {
                return second;
            }
        }

        return first;
    }
}
