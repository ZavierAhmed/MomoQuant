using System.Text.Json;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.MarketData;

namespace MomoQuant.UnitTests.Strategies;

/// <summary>
/// Explicit three-path result-evidence contracts (Milestone 23.1B1C2).
/// Required properties are fixed per strategy/case — never derived from live SUT output.
/// </summary>
internal static class ParityEvidenceContracts
{
    public const string AdaptivePositiveFingerprint = "8DC2EABFE2BA0A5E";

    // Independently frozen from the deterministic 600-candle Adaptive fixture before any
    // direct, Lab, or Backtest invocation. The exact-object contract also makes every
    // unlisted root property absent by construction.
    public static ParityAssertionHelper.RawDataJsonContract CreateAdaptivePositiveRawDataContract() =>
        ParityAssertionHelper.RawDataJsonContract.Create(
            ParityAssertionHelper.RawDataJsonRootState.PresentJsonObject,
            ("setupFingerprint", ParityAssertionHelper.JsonPropertyExpectation.String(AdaptivePositiveFingerprint)),
            ("strengthBreakdown", ParityAssertionHelper.JsonPropertyExpectation.Json("""
                {"htfAlignment":100,"executionTrend":26.219979440800739041582574400,"volatilityQuality":88.41181306137762195365436734,"breakoutQuality":100,"momentum":44.303996640375170448326299680,"retestQuality":70.488207300021319344742447990,"total":71.570666073762475131384281568}
                """)),
            ("setup", ParityAssertionHelper.JsonPropertyExpectation.Json("""
                {"setupType":"MtfTrendBreakoutRetest","direction":"Long","brokenLevel":51364,"breakoutTimeUtc":"2026-01-10T12:20:00Z","retestTimeUtc":"2026-01-10T12:25:00Z","confirmationTimeUtc":"2026-01-10T12:30:00Z","breakoutIndex":597,"retestIndex":598,"confirmationIndex":599,"adaptiveBuffer":0.2154778505099169588368980612,"volRatio":1.7698523367327797255793204083,"breakoutAtrFast":349.05954976083357099324744597,"breakoutAtrSlow":197.22523880450493957184368274,"retestAtrFast":340.78386763505974449372977126,"confirmationAtrFast":340.78386763505974449372977126,"retestExtreme":51328.8,"stopBufferAtr":0.20}
                """)),
            ("version", ParityAssertionHelper.JsonPropertyExpectation.String("1.0.0")),
            ("reasonCode", ParityAssertionHelper.JsonPropertyExpectation.String("EntryConfirmed")));

    public static ParityAssertionHelper.PositiveOutcomeContract CreateAdaptivePositiveOutcomeContract() =>
        new(
            Direction: MomoQuant.Domain.Enums.TradeDirection.Long,
            EntryPrice: 51540.000m,
            StopLoss: 51260.643226472988051101254046m,
            TakeProfit: 52238.391933817529872246864885m,
            Strength: 71.570666073762475131384281568m,
            Reason: "Long MTF trend breakout retest confirmed.");

    public const string RangePositiveFingerprint = "43E14ED345E566C3";

    public sealed record RangePositiveEvidence(
        ParityAssertionHelper.RawDataJsonContract RawDataContract,
        ParityAssertionHelper.PositiveOutcomeContract OutcomeContract);

    /// <summary>Independent Range reference calculation over the fixed parity fixture, before strategy execution.</summary>
    public static RangePositiveEvidence CreateRangePositiveEvidence(IReadOnlyList<Candle> candles)
    {
        const int indexFromEnd = 1;
        const int rangeLookback = 48;
        const int fastEmaPeriod = 20;
        const int slowEmaPeriod = 50;
        const int slopeLookback = 5;
        const int fastAtrPeriod = 14;
        const int slowAtrPeriod = 100;
        const int rsiPeriod = 14;
        const decimal boundaryToleranceAtr = 0.15m;
        const decimal stopBufferAtr = 0.25m;
        const decimal minimumRewardRisk = 1.25m;
        const decimal minRangeWidthAtr = 3.0m;
        const decimal maxRangeWidthAtr = 12.0m;
        const decimal minVolatilityRatio = 0.65m;
        const decimal maxVolatilityRatio = 1.25m;
        const decimal rsiOversold = 35m;
        const decimal minimumWickPercent = 30m;
        const decimal maxEmaSeparationAtr = 0.50m;
        const decimal maxSlowEmaSlopeAtr = 0.15m;
        const string version = "1.0.0";

        var index = candles.Count - indexFromEnd;
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        var rangeStart = index - rangeLookback;
        if (rangeStart < 0)
        {
            throw new ArgumentException("Range parity fixture is too short.", nameof(candles));
        }

        var rangeHigh = decimal.MinValue;
        var rangeLow = decimal.MaxValue;
        for (var i = rangeStart; i < index; i++)
        {
            rangeHigh = Math.Max(rangeHigh, candles[i].High);
            rangeLow = Math.Min(rangeLow, candles[i].Low);
        }

        var rangeMidpoint = (rangeHigh + rangeLow) / 2m;
        var rangeWidth = rangeHigh - rangeLow;
        var fastEma = CalculateReferenceEma(candles, index, fastEmaPeriod);
        var slowEma = CalculateReferenceEma(candles, index, slowEmaPeriod);
        var priorSlowEma = CalculateReferenceEma(candles, index - slopeLookback, slowEmaPeriod);
        var fastAtr = CalculateReferenceAtr(candles, index, fastAtrPeriod);
        var slowAtr = CalculateReferenceAtr(candles, index, slowAtrPeriod);
        var rsi = CalculateReferenceRsi(candles, index, rsiPeriod);
        var volatilityRatio = fastAtr / slowAtr;
        var emaSeparationAtr = Math.Abs(fastEma - slowEma) / fastAtr;
        var slowEmaSlopeAtr = (slowEma - priorSlowEma) / fastAtr;
        var current = candles[index];
        var wickPercent = (Math.Min(current.Open, current.Close) - current.Low) / (current.High - current.Low) * 100m;
        var entry = current.Close;
        var stop = current.Low - (stopBufferAtr * fastAtr);
        var takeProfit = rangeMidpoint;
        var rewardRisk = (takeProfit - entry) / (entry - stop);
        var rangeQuality = Math.Clamp(20m - Math.Abs((rangeWidth / fastAtr) - ((minRangeWidthAtr + maxRangeWidthAtr) / 2m))
            / (maxRangeWidthAtr - minRangeWidthAtr) * 20m, 0m, 20m);
        var volatilityQuality = Math.Clamp(15m - Math.Abs(volatilityRatio - ((minVolatilityRatio + maxVolatilityRatio) / 2m))
            / (maxVolatilityRatio - minVolatilityRatio) * 15m, 0m, 15m);
        var rsiExtremity = Math.Clamp((rsiOversold - rsi) / rsiOversold * 20m, 0m, 20m);
        var wickQuality = Math.Clamp((wickPercent - minimumWickPercent) / (100m - minimumWickPercent) * 15m, 0m, 15m);
        var rewardRiskQuality = Math.Clamp((rewardRisk - minimumRewardRisk) * 5m, 0m, 15m);
        var trendFlatness = Math.Clamp(15m - emaSeparationAtr / maxEmaSeparationAtr * 7.5m
            - Math.Abs(slowEmaSlopeAtr) / maxSlowEmaSlopeAtr * 7.5m, 0m, 15m);
        var strength = rangeQuality + volatilityQuality + rsiExtremity + wickQuality + rewardRiskQuality + trendFlatness;

        var diagnostics = JsonSerializer.Serialize(new
        {
            version,
            direction = "Long",
            rangeHigh,
            rangeLow,
            rangeMidpoint,
            rangeWidth,
            volatilityRatio,
            emaSeparationAtr,
            slowEmaSlopeAtr,
            fastEma,
            slowEma,
            fastAtr,
            slowAtr,
            rsi,
            boundaryToleranceAtr,
            acceptedLowerBoundaryProbe = rangeLow - (boundaryToleranceAtr * fastAtr),
            acceptedUpperBoundaryProbe = rangeHigh + (boundaryToleranceAtr * fastAtr),
            wickPercent,
            rewardRisk,
            entry,
            stop,
            takeProfit,
            strengthBreakdown = new
            {
                rangeQuality,
                volatilityQuality,
                rsiExtremity,
                wickQuality,
                rewardRiskQuality,
                trendFlatness,
                total = strength
            }
        });

        return new RangePositiveEvidence(
            ParityAssertionHelper.RawDataJsonContract.Create(
                ParityAssertionHelper.RawDataJsonRootState.PresentJsonObject,
                ("setupFingerprint", ParityAssertionHelper.JsonPropertyExpectation.String(RangePositiveFingerprint)),
                ("version", ParityAssertionHelper.JsonPropertyExpectation.String(version)),
                ("diagnostics", ParityAssertionHelper.JsonPropertyExpectation.Json(diagnostics))),
            new ParityAssertionHelper.PositiveOutcomeContract(
                TradeDirection.Long,
                entry,
                stop,
                takeProfit,
                strength,
                "EntryConfirmed"));
    }

    public static ParityAssertionHelper.RawDataJsonContract CreateRangeRejectionEnvelopeContract(
        string strategyCode,
        string version,
        string reason,
        long symbolId,
        string symbol,
        string timeframe,
        string marketRegime,
        DateTime evaluatedAtUtc) =>
        ParityAssertionHelper.RawDataJsonContract.Create(
            ParityAssertionHelper.RawDataJsonRootState.PresentJsonObject,
            ("strategyCode", ParityAssertionHelper.JsonPropertyExpectation.String(strategyCode)),
            ("version", ParityAssertionHelper.JsonPropertyExpectation.String(version)),
            ("reason", ParityAssertionHelper.JsonPropertyExpectation.String(reason)),
            ("symbolId", ParityAssertionHelper.JsonPropertyExpectation.Number(symbolId)),
            ("symbol", ParityAssertionHelper.JsonPropertyExpectation.String(symbol)),
            ("timeframe", ParityAssertionHelper.JsonPropertyExpectation.String(timeframe)),
            ("marketRegime", ParityAssertionHelper.JsonPropertyExpectation.String(marketRegime)),
            ("evaluatedAtUtc", ParityAssertionHelper.JsonPropertyExpectation.Json(JsonSerializer.Serialize(evaluatedAtUtc))));

    private static decimal CalculateReferenceEma(IReadOnlyList<Candle> candles, int index, int period)
    {
        var sum = 0m;
        for (var i = 0; i < period; i++) sum += candles[i].Close;
        var ema = sum / period;
        var multiplier = 2m / (period + 1m);
        for (var i = period; i <= index; i++) ema = ((candles[i].Close - ema) * multiplier) + ema;
        return ema;
    }

    private static decimal CalculateReferenceAtr(IReadOnlyList<Candle> candles, int index, int period)
    {
        decimal? previousClose = null;
        decimal? atr = null;
        for (var i = 0; i <= index; i++)
        {
            var candle = candles[i];
            var tr = previousClose is null
                ? candle.High - candle.Low
                : Math.Max(candle.High - candle.Low, Math.Max(Math.Abs(candle.High - previousClose.Value), Math.Abs(candle.Low - previousClose.Value)));
            previousClose = candle.Close;
            if (i + 1 >= period) atr = (((atr ?? 0m) * (period - 1)) + tr) / period;
        }

        return atr ?? throw new ArgumentException("Range parity fixture cannot establish ATR.", nameof(candles));
    }

    private static decimal CalculateReferenceRsi(IReadOnlyList<Candle> candles, int index, int period)
    {
        decimal? previousClose = null;
        decimal? averageGain = null;
        decimal? averageLoss = null;
        var processedChanges = 0;
        for (var i = 0; i <= index; i++)
        {
            var close = candles[i].Close;
            if (previousClose is null) { previousClose = close; continue; }
            var change = close - previousClose.Value;
            previousClose = close;
            var gain = change > 0m ? change : 0m;
            var loss = change < 0m ? -change : 0m;
            processedChanges++;
            if (processedChanges < period)
            {
                averageGain = (averageGain ?? 0m) + gain;
                averageLoss = (averageLoss ?? 0m) + loss;
                continue;
            }
            averageGain = ((averageGain ?? 0m) * (period - 1) + gain) / period;
            averageLoss = ((averageLoss ?? 0m) * (period - 1) + loss) / period;
        }

        if (averageLoss == 0m) return averageGain > 0m ? 100m : 0m;
        return Math.Clamp(100m - (100m / (1m + (averageGain!.Value / averageLoss!.Value))), 0m, 100m);
    }

    public static readonly IReadOnlyList<string> AdaptivePositiveStructure =
        ["strength", "strengthBreakdown"];

    public static readonly IReadOnlyList<string> AdaptiveRejectionRawData =
        Array.Empty<string>();

    public static readonly IReadOnlyList<string> RangePositiveRawData =
        ["setupFingerprint", "version", "diagnostics"];

    public static readonly IReadOnlyList<string> RangePositiveStructure =
        ["strength", "strengthBreakdown"];

    public static readonly IReadOnlyList<string> RangeRejectionRawData =
        ["strategyCode", "version", "reason", "symbolId", "timeframe", "marketRegime", "evaluatedAtUtc"];

    public static readonly IReadOnlyList<string> PsbrPositiveRawData =
        ["setupFingerprint", "structure", "version", "strengthBreakdown"];

    public static readonly IReadOnlyList<string> PsbrPositiveStructure =
        ["strength", "strengthBreakdown"];

    public static readonly IReadOnlyList<string> PsbrRejectionRawData =
        Array.Empty<string>();

    /// <summary>
    /// Production Adaptive/PSBR/Range NoTrade paths do not emit setupFingerprint.
    /// </summary>
    public static ParityAssertionHelper.FingerprintContract RejectionFingerprintAbsent { get; } =
        new ParityAssertionHelper.FingerprintContract.RequiredAbsent();

    public static ParityAssertionHelper.FingerprintContract PositiveFingerprint(string expectedNonEmptyValue)
    {
        if (string.IsNullOrWhiteSpace(expectedNonEmptyValue))
        {
            throw new ArgumentException(
                "Positive fingerprint contract requires a non-empty canonical value.",
                nameof(expectedNonEmptyValue));
        }

        return new ParityAssertionHelper.FingerprintContract.RequiredPresent(expectedNonEmptyValue);
    }
}
