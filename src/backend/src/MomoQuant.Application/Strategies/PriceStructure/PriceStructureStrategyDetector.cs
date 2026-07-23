using MomoQuant.Application.Strategies.PriceStructure.Dtos;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.MarketData;

namespace MomoQuant.Application.Strategies.PriceStructure;

/// <summary>
/// Shared price-structure detector used by Synthetic Tests and Strategy Laboratory.
/// Keeps evaluation state scoped to one run/symbol/timeframe session.
/// </summary>
public interface IPriceStructureStrategyDetector
{
    void Initialize(IReadOnlyDictionary<string, string> parameters);
    PriceStructureDetectorResult ProcessCandle(
        IReadOnlyList<Candle> candlesThroughCurrent,
        string strategyCode,
        long symbolId,
        string timeframe);
    PriceStructureFunnelDiagnostics GetDiagnostics();
}

public sealed class PriceStructureDetectorResult
{
    public PriceStructureCandidateDto? Candidate { get; init; }
    public string Reason { get; init; } = string.Empty;
}

public sealed class PriceStructureFunnelDiagnostics
{
    public string StrategyFamily { get; set; } = string.Empty;
    public int CandlesLoaded { get; set; }
    public int WarmupCandlesLoaded { get; set; }
    public int TestRangeCandles { get; set; }
    public int EligibleEvaluationCandles { get; set; }
    public int CandlesEvaluated { get; set; }

    public int ConfirmedSwingHighs { get; set; }
    public int ConfirmedSwingLows { get; set; }

    public int BullishBreakoutChecks { get; set; }
    public int BearishBreakoutChecks { get; set; }
    public int BullishBreakoutsDetected { get; set; }
    public int BearishBreakoutsDetected { get; set; }
    public int BreakoutsRejectedNoCloseBeyondLevel { get; set; }
    public int BreakoutsExpiredBeforeRetest { get; set; }

    public int RetestChecks { get; set; }
    public int ValidRetests { get; set; }
    public int InvalidatedRetests { get; set; }
    public int ExpiredRetests { get; set; }

    public int ConfirmationChecks { get; set; }
    public int ConfirmationsPassed { get; set; }
    public int ConfirmationsFailed { get; set; }

    public int ActiveBuySideLiquidityLevels { get; set; }
    public int ActiveSellSideLiquidityLevels { get; set; }
    public int BuySideSweepChecks { get; set; }
    public int SellSideSweepChecks { get; set; }
    public int BuySideSweepsDetected { get; set; }
    public int SellSideSweepsDetected { get; set; }
    public int SameCandleReclaims { get; set; }
    public int DelayedReclaims { get; set; }
    public int SweepsWithoutReclaim { get; set; }
    public int ReclaimExpired { get; set; }

    public int DuplicateSetupsSuppressed { get; set; }
    public int SimulationInvalidCandidates { get; set; }
    public int CandidatesDetectedInMemory { get; set; }
    public int CandidatesRejectedAsDuplicate { get; set; }
    public int CandidatesSimulationInvalid { get; set; }
    public int CandidatesPersisted { get; set; }
    public int RawCandidatesCreated { get; set; }

    public int FingerprintCreatedCount { get; set; }
    public int DuplicateFingerprintCount { get; set; }
    public List<string> SampleFingerprints { get; set; } = [];

    public string? PrimaryBlocker { get; set; }
    public string? PrimaryBlockerDetails { get; set; }
    public string? SuggestedNextAction { get; set; }
    public string ZeroCandidateClassification { get; set; } = "Unknown";
    public List<string> RuntimeWarnings { get; set; } = [];

    public List<PriceStructureDiagnosticEvent> SampleEvents { get; set; } = [];

    public Dictionary<string, int> ReasonCounts { get; set; } = new(StringComparer.Ordinal);
}

public sealed class PriceStructureDiagnosticEvent
{
    public string Stage { get; init; } = string.Empty;
    public string Direction { get; init; } = string.Empty;
    public decimal Level { get; init; }
    public DateTime? LevelTimestampUtc { get; init; }
    public DateTime? EventTimestampUtc { get; init; }
    public DateTime? SecondaryTimestampUtc { get; init; }
    public decimal? EventPrice { get; init; }
    public string Outcome { get; init; } = string.Empty;
    public string? Reason { get; init; }
}

public sealed class BreakoutRetestStrategyDetector : IPriceStructureStrategyDetector
{
    private readonly HashSet<string> _seenFingerprints = new(StringComparer.Ordinal);
    private readonly HashSet<string> _seenSwings = new(StringComparer.Ordinal);
    private readonly HashSet<string> _seenBreakouts = new(StringComparer.Ordinal);
    private readonly HashSet<string> _seenRetests = new(StringComparer.Ordinal);
    private readonly HashSet<string> _seenConfirmations = new(StringComparer.Ordinal);
    private readonly PriceStructureFunnelDiagnostics _funnel = new() { StrategyFamily = "BreakoutRetest" };
    private IReadOnlyDictionary<string, string> _parameters = new Dictionary<string, string>();

    public void Initialize(IReadOnlyDictionary<string, string> parameters)
    {
        _parameters = parameters;
        _seenFingerprints.Clear();
        _seenSwings.Clear();
        _seenBreakouts.Clear();
        _seenRetests.Clear();
        _seenConfirmations.Clear();
        _funnel.SampleEvents.Clear();
        _funnel.SampleFingerprints.Clear();
        _funnel.ReasonCounts.Clear();
        _funnel.RuntimeWarnings.Clear();
    }

    public PriceStructureDetectorResult ProcessCandle(
        IReadOnlyList<Candle> candlesThroughCurrent,
        string strategyCode,
        long symbolId,
        string timeframe)
    {
        _funnel.CandlesEvaluated++;
        var settings = PriceStructureBreakoutRetestEvaluator.ReadParameters(_parameters);
        var currentIndex = candlesThroughCurrent.Count - 1;
        var maxConfirmed = currentIndex - settings.SwingRightBars;
        if (maxConfirmed >= settings.SwingLeftBars)
        {
            var swings = PriceStructureSwingDetector.DetectConfirmedSwings(
                candlesThroughCurrent,
                settings.SwingLeftBars,
                settings.SwingRightBars,
                settings.UseWicksForSwing,
                maxConfirmed);
            foreach (var swing in swings)
            {
                var swingKey = $"{swing.Index}:{(swing.IsHigh ? "H" : "L")}:{swing.Price}";
                if (_seenSwings.Add(swingKey))
                {
                    if (swing.IsHigh)
                    {
                        _funnel.ConfirmedSwingHighs++;
                    }
                    else
                    {
                        _funnel.ConfirmedSwingLows++;
                    }

                    AddSample("Swing", swing.IsHigh ? "Short" : "Long", swing.Price, swing.OpenTimeUtc, null, null, "Confirmed");
                }
            }
        }

        var (candidate, reason) = PriceStructureBreakoutRetestEvaluator.EvaluateAtCurrentCandle(
            candlesThroughCurrent,
            _parameters,
            _seenFingerprints,
            strategyCode,
            symbolId,
            timeframe,
            TrackBreakoutFunnelEvent);

        IncrementReason(reason);

        if (candidate is not null)
        {
            _seenFingerprints.Add(candidate.SetupFingerprint);
            _funnel.CandidatesDetectedInMemory++;
            _funnel.RawCandidatesCreated++;
            _funnel.FingerprintCreatedCount++;
            if (_funnel.SampleFingerprints.Count < 20)
            {
                _funnel.SampleFingerprints.Add(candidate.SetupFingerprint);
            }

            AddSample(
                "Candidate",
                candidate.Direction.ToString(),
                candidate.Structure.BrokenOrSweptLevel,
                candidate.Structure.SwingTimeUtc,
                candidate.Structure.ConfirmationTimeUtc,
                candidate.EntryPrice,
                "Created",
                candidate.Reason);
        }
        else if (string.Equals(reason, PriceStructureRejectionCodes.DuplicateSetup, StringComparison.Ordinal))
        {
            _funnel.DuplicateSetupsSuppressed++;
            _funnel.DuplicateFingerprintCount++;
            _funnel.CandidatesRejectedAsDuplicate++;
        }

        return new PriceStructureDetectorResult { Candidate = candidate, Reason = reason };
    }

    public PriceStructureFunnelDiagnostics GetDiagnostics() => _funnel;

    private void TrackBreakoutFunnelEvent(PriceStructureBreakoutFunnelEvent evt)
    {
        switch (evt.Kind)
        {
            case PriceStructureBreakoutFunnelEventKind.BreakoutCheck:
                if (evt.Direction == TradeDirection.Long)
                {
                    _funnel.BullishBreakoutChecks++;
                }
                else
                {
                    _funnel.BearishBreakoutChecks++;
                }

                break;
            case PriceStructureBreakoutFunnelEventKind.BreakoutDetected:
                if (_seenBreakouts.Add(evt.Key))
                {
                    if (evt.Direction == TradeDirection.Long)
                    {
                        _funnel.BullishBreakoutsDetected++;
                    }
                    else
                    {
                        _funnel.BearishBreakoutsDetected++;
                    }

                    AddSample("Breakout", evt.Direction.ToString(), evt.Level, evt.LevelTimeUtc, evt.EventTimeUtc, evt.EventPrice, "Detected");
                }

                break;
            case PriceStructureBreakoutFunnelEventKind.BreakoutRejectedNoClose:
                _funnel.BreakoutsRejectedNoCloseBeyondLevel++;
                break;
            case PriceStructureBreakoutFunnelEventKind.RetestCheck:
                _funnel.RetestChecks++;
                break;
            case PriceStructureBreakoutFunnelEventKind.ValidRetest:
                if (_seenRetests.Add(evt.Key))
                {
                    _funnel.ValidRetests++;
                    AddSample("Retest", evt.Direction.ToString(), evt.Level, evt.LevelTimeUtc, evt.EventTimeUtc, evt.EventPrice, "Valid");
                }

                break;
            case PriceStructureBreakoutFunnelEventKind.InvalidatedRetest:
                _funnel.InvalidatedRetests++;
                break;
            case PriceStructureBreakoutFunnelEventKind.ExpiredRetest:
                _funnel.BreakoutsExpiredBeforeRetest++;
                _funnel.ExpiredRetests++;
                break;
            case PriceStructureBreakoutFunnelEventKind.ConfirmationCheck:
                _funnel.ConfirmationChecks++;
                break;
            case PriceStructureBreakoutFunnelEventKind.ConfirmationPassed:
                if (_seenConfirmations.Add(evt.Key))
                {
                    _funnel.ConfirmationsPassed++;
                    AddSample("Confirmation", evt.Direction.ToString(), evt.Level, evt.LevelTimeUtc, evt.EventTimeUtc, evt.EventPrice, "Passed");
                }

                break;
            case PriceStructureBreakoutFunnelEventKind.ConfirmationFailed:
                _funnel.ConfirmationsFailed++;
                break;
        }
    }

    private void IncrementReason(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return;
        }

        _funnel.ReasonCounts.TryGetValue(reason, out var count);
        _funnel.ReasonCounts[reason] = count + 1;
    }

    private void AddSample(
        string stage,
        string direction,
        decimal level,
        DateTime? levelTime,
        DateTime? eventTime,
        decimal? eventPrice,
        string outcome,
        string? reason = null)
    {
        if (_funnel.SampleEvents.Count >= 20)
        {
            return;
        }

        _funnel.SampleEvents.Add(new PriceStructureDiagnosticEvent
        {
            Stage = stage,
            Direction = direction,
            Level = level,
            LevelTimestampUtc = levelTime,
            EventTimestampUtc = eventTime,
            EventPrice = eventPrice,
            Outcome = outcome,
            Reason = reason
        });
    }
}

public sealed class LiquiditySweepStrategyDetector : IPriceStructureStrategyDetector
{
    private readonly HashSet<string> _seenFingerprints = new(StringComparer.Ordinal);
    private readonly HashSet<string> _seenSwings = new(StringComparer.Ordinal);
    private readonly HashSet<string> _seenSweeps = new(StringComparer.Ordinal);
    private readonly HashSet<string> _seenReclaims = new(StringComparer.Ordinal);
    private readonly PriceStructureFunnelDiagnostics _funnel = new() { StrategyFamily = "LiquiditySweep" };
    private IReadOnlyDictionary<string, string> _parameters = new Dictionary<string, string>();

    public void Initialize(IReadOnlyDictionary<string, string> parameters)
    {
        _parameters = parameters;
        _seenFingerprints.Clear();
        _seenSwings.Clear();
        _seenSweeps.Clear();
        _seenReclaims.Clear();
        _funnel.SampleEvents.Clear();
        _funnel.SampleFingerprints.Clear();
        _funnel.ReasonCounts.Clear();
        _funnel.RuntimeWarnings.Clear();
    }

    public PriceStructureDetectorResult ProcessCandle(
        IReadOnlyList<Candle> candlesThroughCurrent,
        string strategyCode,
        long symbolId,
        string timeframe)
    {
        _funnel.CandlesEvaluated++;
        var settings = PriceStructureLiquiditySweepEvaluator.ReadParameters(_parameters);
        var currentIndex = candlesThroughCurrent.Count - 1;
        var maxConfirmed = currentIndex - settings.SwingRightBars;
        if (maxConfirmed >= settings.SwingLeftBars)
        {
            var swings = PriceStructureSwingDetector.DetectConfirmedSwings(
                candlesThroughCurrent,
                settings.SwingLeftBars,
                settings.SwingRightBars,
                true,
                maxConfirmed);

            var buySide = 0;
            var sellSide = 0;
            foreach (var swing in swings.Where(s => currentIndex - s.Index <= settings.MaxLiquidityLevelAgeBars))
            {
                var swingKey = $"{swing.Index}:{(swing.IsHigh ? "H" : "L")}:{swing.Price}";
                if (_seenSwings.Add(swingKey))
                {
                    if (swing.IsHigh)
                    {
                        _funnel.ConfirmedSwingHighs++;
                        sellSide++;
                        AddSample("LiquidityLevel", "Short", swing.Price, swing.OpenTimeUtc, null, null, "ActiveSellSide");
                    }
                    else
                    {
                        _funnel.ConfirmedSwingLows++;
                        buySide++;
                        AddSample("LiquidityLevel", "Long", swing.Price, swing.OpenTimeUtc, null, null, "ActiveBuySide");
                    }
                }
            }

            _funnel.ActiveBuySideLiquidityLevels = Math.Max(_funnel.ActiveBuySideLiquidityLevels, buySide + _funnel.ConfirmedSwingLows);
            _funnel.ActiveSellSideLiquidityLevels = Math.Max(_funnel.ActiveSellSideLiquidityLevels, sellSide + _funnel.ConfirmedSwingHighs);
        }

        var (candidate, reason) = PriceStructureLiquiditySweepEvaluator.EvaluateAtCurrentCandle(
            candlesThroughCurrent,
            _parameters,
            _seenFingerprints,
            strategyCode,
            symbolId,
            timeframe,
            TrackSweepFunnelEvent);

        IncrementReason(reason);

        if (candidate is not null)
        {
            _seenFingerprints.Add(candidate.SetupFingerprint);
            _funnel.CandidatesDetectedInMemory++;
            _funnel.RawCandidatesCreated++;
            _funnel.FingerprintCreatedCount++;
            if (_funnel.SampleFingerprints.Count < 20)
            {
                _funnel.SampleFingerprints.Add(candidate.SetupFingerprint);
            }

            AddSample(
                "Candidate",
                candidate.Direction.ToString(),
                candidate.Structure.BrokenOrSweptLevel,
                candidate.Structure.SwingTimeUtc,
                candidate.Structure.ConfirmationTimeUtc,
                candidate.EntryPrice,
                "Created",
                candidate.Reason);
        }
        else if (string.Equals(reason, PriceStructureRejectionCodes.DuplicateSetup, StringComparison.Ordinal))
        {
            _funnel.DuplicateSetupsSuppressed++;
            _funnel.DuplicateFingerprintCount++;
            _funnel.CandidatesRejectedAsDuplicate++;
        }

        return new PriceStructureDetectorResult { Candidate = candidate, Reason = reason };
    }

    public PriceStructureFunnelDiagnostics GetDiagnostics() => _funnel;

    private void TrackSweepFunnelEvent(PriceStructureLiquidityFunnelEvent evt)
    {
        switch (evt.Kind)
        {
            case PriceStructureLiquidityFunnelEventKind.SweepCheck:
                if (evt.Direction == TradeDirection.Long)
                {
                    _funnel.BuySideSweepChecks++;
                }
                else
                {
                    _funnel.SellSideSweepChecks++;
                }

                break;
            case PriceStructureLiquidityFunnelEventKind.SweepDetected:
                if (_seenSweeps.Add(evt.Key))
                {
                    if (evt.Direction == TradeDirection.Long)
                    {
                        _funnel.BuySideSweepsDetected++;
                    }
                    else
                    {
                        _funnel.SellSideSweepsDetected++;
                    }

                    AddSample("Sweep", evt.Direction.ToString(), evt.Level, evt.LevelTimeUtc, evt.EventTimeUtc, evt.EventPrice, "Detected");
                }

                break;
            case PriceStructureLiquidityFunnelEventKind.SameCandleReclaim:
                if (_seenReclaims.Add(evt.Key))
                {
                    _funnel.SameCandleReclaims++;
                    AddSample("Reclaim", evt.Direction.ToString(), evt.Level, evt.LevelTimeUtc, evt.EventTimeUtc, evt.EventPrice, "SameCandle");
                }

                break;
            case PriceStructureLiquidityFunnelEventKind.DelayedReclaim:
                _funnel.DelayedReclaims++;
                break;
            case PriceStructureLiquidityFunnelEventKind.SweepWithoutReclaim:
                _funnel.SweepsWithoutReclaim++;
                break;
            case PriceStructureLiquidityFunnelEventKind.ReclaimExpired:
                _funnel.ReclaimExpired++;
                break;
        }
    }

    private void IncrementReason(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return;
        }

        _funnel.ReasonCounts.TryGetValue(reason, out var count);
        _funnel.ReasonCounts[reason] = count + 1;
    }

    private void AddSample(
        string stage,
        string direction,
        decimal level,
        DateTime? levelTime,
        DateTime? eventTime,
        decimal? eventPrice,
        string outcome,
        string? reason = null)
    {
        if (_funnel.SampleEvents.Count >= 20)
        {
            return;
        }

        _funnel.SampleEvents.Add(new PriceStructureDiagnosticEvent
        {
            Stage = stage,
            Direction = direction,
            Level = level,
            LevelTimestampUtc = levelTime,
            EventTimestampUtc = eventTime,
            EventPrice = eventPrice,
            Outcome = outcome,
            Reason = reason
        });
    }
}

public static class PriceStructureDetectorFactory
{
    public static IPriceStructureStrategyDetector? Create(string strategyCode) =>
        strategyCode switch
        {
            "PRICE_STRUCTURE_BREAKOUT_RETEST" => new BreakoutRetestStrategyDetector(),
            "PRICE_STRUCTURE_LIQUIDITY_SWEEP_RECLAIM" => new LiquiditySweepStrategyDetector(),
            _ => null
        };
}

public enum PriceStructureBreakoutFunnelEventKind
{
    BreakoutCheck,
    BreakoutDetected,
    BreakoutRejectedNoClose,
    RetestCheck,
    ValidRetest,
    InvalidatedRetest,
    ExpiredRetest,
    ConfirmationCheck,
    ConfirmationPassed,
    ConfirmationFailed
}

public sealed class PriceStructureBreakoutFunnelEvent
{
    public required PriceStructureBreakoutFunnelEventKind Kind { get; init; }
    public required string Key { get; init; }
    public TradeDirection Direction { get; init; }
    public decimal Level { get; init; }
    public DateTime? LevelTimeUtc { get; init; }
    public DateTime? EventTimeUtc { get; init; }
    public decimal? EventPrice { get; init; }
}

public enum PriceStructureLiquidityFunnelEventKind
{
    SweepCheck,
    SweepDetected,
    SameCandleReclaim,
    DelayedReclaim,
    SweepWithoutReclaim,
    ReclaimExpired
}

public sealed class PriceStructureLiquidityFunnelEvent
{
    public required PriceStructureLiquidityFunnelEventKind Kind { get; init; }
    public required string Key { get; init; }
    public TradeDirection Direction { get; init; }
    public decimal Level { get; init; }
    public DateTime? LevelTimeUtc { get; init; }
    public DateTime? EventTimeUtc { get; init; }
    public decimal? EventPrice { get; init; }
}

public static class PriceStructureZeroCandidateExplainer
{
    public static void Populate(PriceStructureFunnelDiagnostics funnel, bool syntheticAllPassed)
    {
        var swings = funnel.ConfirmedSwingHighs + funnel.ConfirmedSwingLows;
        var breakouts = funnel.BullishBreakoutsDetected + funnel.BearishBreakoutsDetected;
        var sweeps = funnel.BuySideSweepsDetected + funnel.SellSideSweepsDetected;
        var reclaims = funnel.SameCandleReclaims + funnel.DelayedReclaims;

        if (funnel.EligibleEvaluationCandles == 0 || funnel.CandlesEvaluated == 0)
        {
            funnel.ZeroCandidateClassification = "NoCandles";
            funnel.PrimaryBlocker = "No candles evaluated";
            funnel.PrimaryBlockerDetails = "The run had no eligible evaluation candles in the requested test range.";
            funnel.SuggestedNextAction = "Verify candle import and date range coverage.";
            return;
        }

        if (funnel.WarmupCandlesLoaded < 50 && funnel.CandlesLoaded < 100)
        {
            funnel.ZeroCandidateClassification = "InsufficientWarmup";
            funnel.PrimaryBlocker = "Insufficient warmup";
            funnel.PrimaryBlockerDetails = $"Only {funnel.WarmupCandlesLoaded} warmup candles were loaded.";
            funnel.SuggestedNextAction = "Import additional history before the test FromUtc.";
        }

        if (funnel.CandlesEvaluated > 500 && swings == 0)
        {
            funnel.RuntimeWarnings.Add(
                "DetectorRuntimeWarning: No swing structure was detected in a sufficiently large real-market sample. The runtime evaluation pipeline may be incorrect.");
            if (syntheticAllPassed)
            {
                funnel.ZeroCandidateClassification = "SyntheticRuntimeMismatch";
                funnel.PrimaryBlocker = "Synthetic/runtime mismatch";
                funnel.PrimaryBlockerDetails =
                    "Synthetic detector tests pass, but the real-market runtime produced no structure events. Verify candle history/state handling.";
                funnel.SuggestedNextAction = "Compare synthetic path and Strategy Lab detector path; verify warmup candle slices.";
                funnel.RuntimeWarnings.Add("SyntheticRuntimeMismatch");
                return;
            }

            funnel.ZeroCandidateClassification = "NoSwingStructure";
            funnel.PrimaryBlocker = "No swing structure";
            funnel.PrimaryBlockerDetails = $"{funnel.CandlesEvaluated:N0} candles evaluated, but no confirmed swings were detected.";
            funnel.SuggestedNextAction = "Inspect swingLeftBars/swingRightBars after confirming candles load correctly.";
            return;
        }

        if (funnel.StrategyFamily == "BreakoutRetest")
        {
            if (swings > 0 && breakouts == 0)
            {
                funnel.ZeroCandidateClassification = "NoBreakout";
                funnel.PrimaryBlocker = "No breakouts";
                funnel.PrimaryBlockerDetails = $"{swings} confirmed swings, but no breakout closes beyond levels.";
                funnel.SuggestedNextAction = "Review breakoutMustCloseBeyondLevel and minBreakoutClosePercent after confirming the detector chart.";
                return;
            }

            if (breakouts > 0 && funnel.ValidRetests == 0)
            {
                funnel.ZeroCandidateClassification = "NoRetest";
                funnel.PrimaryBlocker = "No valid retests";
                funnel.PrimaryBlockerDetails =
                    $"{funnel.CandlesEvaluated:N0} candles evaluated, {swings} confirmed swings, {breakouts} breakouts, 0 valid retests.";
                funnel.SuggestedNextAction = "Review maxRetestBars and retest tolerance after confirming the detector chart.";
                return;
            }

            if (funnel.ValidRetests > 0 && funnel.ConfirmationsPassed == 0)
            {
                funnel.ZeroCandidateClassification = "NoConfirmation";
                funnel.PrimaryBlocker = "No confirmations";
                funnel.PrimaryBlockerDetails = $"{funnel.ValidRetests} valid retests, but no confirmation signals.";
                funnel.SuggestedNextAction = "Review confirmationMode after confirming retest behavior on the diagnostic chart.";
                return;
            }
        }
        else
        {
            if (swings == 0)
            {
                funnel.ZeroCandidateClassification = "NoLiquidityLevels";
                funnel.PrimaryBlocker = "No liquidity levels";
                funnel.PrimaryBlockerDetails = $"{funnel.CandlesEvaluated:N0} candles evaluated with no swing liquidity levels.";
                funnel.SuggestedNextAction = "Inspect swing detection on the diagnostic chart.";
                return;
            }

            if (sweeps == 0)
            {
                funnel.ZeroCandidateClassification = "NoSweep";
                funnel.PrimaryBlocker = "No sweeps";
                funnel.PrimaryBlockerDetails = $"Liquidity levels were detected ({swings}), but no sweep occurred.";
                funnel.SuggestedNextAction = "Review minimumSweepDistancePercent after confirming levels on the chart.";
                return;
            }

            if (sweeps > 0 && reclaims == 0)
            {
                funnel.ZeroCandidateClassification = "NoReclaim";
                funnel.PrimaryBlocker = "No reclaims";
                funnel.PrimaryBlockerDetails = $"{sweeps} sweeps occurred, but none closed back through the swept level.";
                funnel.SuggestedNextAction = "Review requireSameCandleReclaim / maxReclaimBars after confirming sweep candles.";
                return;
            }
        }

        if (funnel.CandidatesDetectedInMemory > 0 && funnel.CandidatesPersisted == 0)
        {
            if (funnel.CandidatesRejectedAsDuplicate == funnel.CandidatesDetectedInMemory)
            {
                funnel.ZeroCandidateClassification = "AllDuplicates";
                funnel.PrimaryBlocker = "All duplicates";
                funnel.PrimaryBlockerDetails = "All in-memory candidates were suppressed as duplicates.";
                funnel.SuggestedNextAction = "Inspect setup fingerprint uniqueness.";
                return;
            }

            if (funnel.CandidatesSimulationInvalid == funnel.CandidatesDetectedInMemory)
            {
                funnel.ZeroCandidateClassification = "AllSimulationInvalid";
                funnel.PrimaryBlocker = "All simulation invalid";
                funnel.PrimaryBlockerDetails = "Candidates were detected but all failed simulation validation.";
                funnel.SuggestedNextAction = "Inspect stop/target geometry on detected setups.";
                return;
            }

            funnel.ZeroCandidateClassification = "CandidatePersistenceBug";
            funnel.PrimaryBlocker = "Candidate persistence bug";
            funnel.PrimaryBlockerDetails =
                $"Detected in memory: {funnel.CandidatesDetectedInMemory}, persisted: 0.";
            funnel.SuggestedNextAction = "Inspect Strategy Lab candidate persistence and fingerprint extraction.";
            funnel.RuntimeWarnings.Add("CandidatePersistenceBug");
            return;
        }

        if (funnel.RawCandidatesCreated == 0)
        {
            funnel.ZeroCandidateClassification = "Unknown";
            funnel.PrimaryBlocker = "No raw candidates";
            funnel.PrimaryBlockerDetails =
                $"{funnel.CandlesEvaluated:N0} candles evaluated; swings={swings}; breakouts={breakouts}; retests={funnel.ValidRetests}; confirmations={funnel.ConfirmationsPassed}; sweeps={sweeps}; reclaims={reclaims}.";
            funnel.SuggestedNextAction = "Inspect the Funnel tab and diagnostic chart before changing parameters.";
        }
    }
}
