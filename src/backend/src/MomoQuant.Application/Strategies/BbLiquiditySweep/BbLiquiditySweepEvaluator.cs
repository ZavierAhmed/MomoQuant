using MomoQuant.Application.Strategies.BbLiquiditySweep.Dtos;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.MarketData;

namespace MomoQuant.Application.Strategies.BbLiquiditySweep;

public sealed class BbLiquiditySweepEvaluator
{
    private readonly IExternalLiquidityLineEngine _liquidityEngine;
    private readonly LiquiditySweepDetector _sweepDetector = new();
    private readonly CisdDetector _cisdDetector = new();
    private readonly TradingSessionFilter _sessionFilter = new();

    public BbLiquiditySweepEvaluator(IExternalLiquidityLineEngine? liquidityEngine = null) =>
        _liquidityEngine = liquidityEngine ?? new MomoLiquidityLineEngine();

    public ExternalLiquidityEngineInfoDto GetImplementationInfo() => _liquidityEngine.GetImplementationInfo();

    public BbLiquiditySweepEvaluationResult Evaluate(
        IReadOnlyList<Candle> candles,
        int candleIndex,
        BbLiquiditySweepParameters parameters,
        IBbLiquiditySweepContextService contextService,
        IBbLiquiditySweepSessionTracker? sessionTracker,
        long? tradingSessionId)
    {
        var engineInfo = _liquidityEngine.GetImplementationInfo();
        var candle = candles[candleIndex];
        var bb = BollingerBandsIndicator.CalculateAt(candles, candleIndex, parameters.BbPeriod, parameters.BbStdDev);
        var (inSession, sessionName) = _sessionFilter.IsInAllowedSession(
            candle.CloseTimeUtc,
            parameters.UseSessionFilter,
            parameters.AllowedSessions);

        var diagnostics = CreateDiagnostics(
            candle.CloseTimeUtc,
            inSession,
            sessionName,
            bb,
            engineInfo.ImplementationMode,
            engineInfo.SourceCodeAvailable);

        var metrics = new BbLiquiditySweepCandleMetrics
        {
            InAllowedSession = inSession
        };

        if (!candle.IsClosed)
        {
            return Reject(diagnostics, metrics with { StagedRejectionCode = BbLiquiditySweepRejectionCodes.MissingRequiredTimeframeData }, BbLiquiditySweepRejectionCodes.MissingRequiredTimeframeData);
        }

        if (!inSession)
        {
            return Reject(diagnostics, metrics with { StagedRejectionCode = BbLiquiditySweepRejectionCodes.OutsideSession }, BbLiquiditySweepRejectionCodes.OutsideSession);
        }

        if (sessionTracker is not null
            && tradingSessionId is long sessionId
            && sessionTracker.IsBlocked(sessionId, candle.CloseTimeUtc, parameters.StopAfterLossesPerSession, parameters.UseSessionFilter))
        {
            return Reject(diagnostics, metrics with { StagedRejectionCode = BbLiquiditySweepRejectionCodes.SessionLossLimitReached }, BbLiquiditySweepRejectionCodes.SessionLossLimitReached);
        }

        if (bb is null)
        {
            return Reject(diagnostics, metrics with { StagedRejectionCode = BbLiquiditySweepRejectionCodes.MissingRequiredTimeframeData }, BbLiquiditySweepRejectionCodes.MissingRequiredTimeframeData);
        }

        metrics = metrics with
        {
            UpperBbWickBreak = candle.High > bb.Upper,
            LowerBbWickBreak = candle.Low < bb.Lower,
            ClosedBackInsideBb = candle.Close <= bb.Upper && candle.Close >= bb.Lower
        };

        var ltfLevels = contextService.GetLiquidityLevels("1m", candle.CloseTimeUtc);
        var fiveMinuteLevels = contextService.GetLiquidityLevels("5m", candle.CloseTimeUtc);
        var allLevels = ltfLevels.Concat(fiveMinuteLevels).ToList();
        var buyLevels = allLevels.Where(level => level.Direction == LiquidityDirection.BuySideLiquidity).ToList();
        var sellLevels = allLevels.Where(level => level.Direction == LiquidityDirection.SellSideLiquidity).ToList();

        metrics = metrics with
        {
            OneMinuteLevelsActive = ltfLevels.Count,
            FiveMinuteLevelsActive = fiveMinuteLevels.Count,
            BuySideLevelsAvailable = buyLevels.Count,
            SellSideLevelsAvailable = sellLevels.Count
        };

        var atr = EstimateAtr(candles, candleIndex);
        var nearestBuy = FindNearestLevel(buyLevels, candle.Close, atr, parameters, abovePrice: true);
        var nearestSell = FindNearestLevel(sellLevels, candle.Close, atr, parameters, abovePrice: false);

        diagnostics = CopyDiagnostics(diagnostics, nearestBuy?.Price, nearestSell?.Price);

        metrics = metrics with
        {
            NearestBuySideLevelDistance = nearestBuy is null ? null : Math.Abs(nearestBuy.Price - candle.Close),
            NearestSellSideLevelDistance = nearestSell is null ? null : Math.Abs(nearestSell.Price - candle.Close)
        };

        RsiPrimedResultDto? rsi = null;
        if (parameters.UseRsiPrimedFilter)
        {
            rsi = contextService.GetRsiPrimedAt(candle.CloseTimeUtc);
            metrics = metrics with { RsiEvaluated = true };
            if (rsi?.SignalValue is null)
            {
                metrics = metrics with { StagedRejectionCode = BbLiquiditySweepRejectionCodes.MissingRequiredTimeframeData };
                return Reject(
                    CopyRsiDiagnostics(diagnostics, rsi, null),
                    metrics,
                    BbLiquiditySweepRejectionCodes.MissingRequiredTimeframeData);
            }

            diagnostics = CopyRsiDiagnostics(diagnostics, rsi, null);
        }

        if (allLevels.Count == 0)
        {
            metrics = metrics with
            {
                StagedRejectionCode = metrics.UpperBbWickBreak || metrics.LowerBbWickBreak
                    ? BbLiquiditySweepRejectionCodes.NoLiquidityLevelsDetected
                    : BbLiquiditySweepRejectionCodes.NoBollingerBandSweep
            };
            return Reject(diagnostics, metrics, metrics.StagedRejectionCode!);
        }

        var longCandidate = nearestSell is not null
            ? TryDirectionalSetup(
                candles,
                candleIndex,
                candle,
                bb,
                nearestSell,
                TradeDirection.Long,
                parameters,
                rsi,
                diagnostics,
                atr,
                ltfLevels,
                fiveMinuteLevels,
                metrics)
            : null;

        if (longCandidate?.Direction is not null)
        {
            return longCandidate;
        }

        var shortCandidate = nearestBuy is not null
            ? TryDirectionalSetup(
                candles,
                candleIndex,
                candle,
                bb,
                nearestBuy,
                TradeDirection.Short,
                parameters,
                rsi,
                longCandidate?.Diagnostics ?? diagnostics,
                atr,
                ltfLevels,
                fiveMinuteLevels,
                longCandidate?.CandleMetrics ?? metrics)
            : null;

        if (shortCandidate?.Direction is not null)
        {
            return shortCandidate;
        }

        var bestMetrics = shortCandidate?.CandleMetrics ?? longCandidate?.CandleMetrics ?? metrics;
        var rejectionCode = bestMetrics.StagedRejectionCode
            ?? (nearestSell is null && nearestBuy is null
                ? BbLiquiditySweepRejectionCodes.NoNearbyLiquidityLevel
                : BbLiquiditySweepRejectionCodes.NoLiquiditySweep);

        return Reject(
            shortCandidate?.Diagnostics ?? longCandidate?.Diagnostics ?? diagnostics,
            bestMetrics with { StagedRejectionCode = rejectionCode },
            rejectionCode);
    }

    private BbLiquiditySweepEvaluationResult? TryDirectionalSetup(
        IReadOnlyList<Candle> candles,
        int candleIndex,
        Candle candle,
        BollingerBandsValueDto bb,
        LiquidityLevelDto liquidityLevel,
        TradeDirection direction,
        BbLiquiditySweepParameters parameters,
        RsiPrimedResultDto? rsi,
        BbLiquiditySweepDiagnosticsDto diagnostics,
        decimal atr,
        IReadOnlyList<LiquidityLevelDto> ltfLevels,
        IReadOnlyList<LiquidityLevelDto> fiveMinuteLevels,
        BbLiquiditySweepCandleMetrics baseMetrics)
    {
        var sweep = direction == TradeDirection.Long
            ? _sweepDetector.DetectLongSweep(
                candle,
                liquidityLevel,
                bb,
                parameters.RequireSweepOutsideBb,
                parameters.RequireCloseBackInsideBb,
                parameters.RequireCloseBackAcrossLiquidityLine)
            : _sweepDetector.DetectShortSweep(
                candle,
                liquidityLevel,
                bb,
                parameters.RequireSweepOutsideBb,
                parameters.RequireCloseBackInsideBb,
                parameters.RequireCloseBackAcrossLiquidityLine);

        var metrics = baseMetrics with
        {
            SellSideSweep = direction == TradeDirection.Long && candle.Low < liquidityLevel.Price,
            BuySideSweep = direction == TradeDirection.Short && candle.High > liquidityLevel.Price,
            CloseBackAcrossLiquidity = sweep.ClosedBackAcrossLiquidityLine
        };

        if (!parameters.RequireSweepOutsideBb)
        {
            metrics = direction == TradeDirection.Long
                ? metrics with { LowerBbWickBreak = metrics.LowerBbWickBreak || candle.Low < bb.Lower }
                : metrics with { UpperBbWickBreak = metrics.UpperBbWickBreak || candle.High > bb.Upper };
        }

        if (!sweep.IsValidSweep)
        {
            var sweepRejection = MapSweepRejection(sweep, parameters);
            if (!parameters.RequireSweepOutsideBb && !(metrics.UpperBbWickBreak || metrics.LowerBbWickBreak))
            {
                sweepRejection = BbLiquiditySweepRejectionCodes.NoLiquiditySweep;
            }
            else if (parameters.RequireSweepOutsideBb && !sweep.SweptOutsideBb)
            {
                sweepRejection = BbLiquiditySweepRejectionCodes.NoBollingerBandSweep;
            }

            return Reject(
                CopySweepDiagnostics(diagnostics, liquidityLevel, direction, sweep, false, null),
                metrics with { StagedRejectionCode = sweepRejection },
                sweepRejection);
        }

        var cisd = _cisdDetector.DetectAfterSweep(
            candles,
            candleIndex,
            direction,
            parameters.CisdLookbackCandles,
            true,
            parameters.DisplacementAtrMultiplier,
            parameters.MaxBarsAfterSweep);

        var cisdCandidate = cisd is not null;
        var cisdConfirmed = cisd?.IsConfirmed == true;
        metrics = metrics with
        {
            CisdCandidate = cisdCandidate,
            CisdConfirmed = cisdConfirmed
        };

        diagnostics = CopySweepDiagnostics(diagnostics, liquidityLevel, direction, sweep, cisdConfirmed, cisd?.CisdLevel);

        if (!cisdCandidate)
        {
            return Reject(diagnostics, metrics with { StagedRejectionCode = BbLiquiditySweepRejectionCodes.NoCisdCandidate }, BbLiquiditySweepRejectionCodes.NoCisdCandidate);
        }

        if (!cisdConfirmed)
        {
            return Reject(diagnostics, metrics with { StagedRejectionCode = BbLiquiditySweepRejectionCodes.NoCisdConfirmation }, BbLiquiditySweepRejectionCodes.NoCisdConfirmation);
        }

        if (parameters.RequireRsiPrimedFilter && rsi is not null)
        {
            var rsiPassed = direction == TradeDirection.Long
                ? PassesLongRsiFilter(rsi, parameters)
                : PassesShortRsiFilter(rsi, parameters);
            metrics = metrics with { RsiPassed = rsiPassed };
            if (!rsiPassed)
            {
                return Reject(
                    CopyRsiDiagnostics(diagnostics, rsi, false),
                    metrics with { StagedRejectionCode = BbLiquiditySweepRejectionCodes.RsiPrimedFilterFailed },
                    BbLiquiditySweepRejectionCodes.RsiPrimedFilterFailed);
            }
        }

        var entry = candle.Close;
        decimal stop;
        decimal risk;
        if (direction == TradeDirection.Long)
        {
            stop = sweep.CandleLow - (atr * parameters.StopLossAtrBufferMultiplier);
            risk = entry - stop;
        }
        else
        {
            stop = sweep.CandleHigh + (atr * parameters.StopLossAtrBufferMultiplier);
            risk = stop - entry;
        }

        if (risk <= 0m)
        {
            return Reject(diagnostics, metrics with { StagedRejectionCode = BbLiquiditySweepRejectionCodes.InvalidStopDistance }, BbLiquiditySweepRejectionCodes.InvalidStopDistance);
        }

        var (target, targetSource) = ResolveTarget(direction, entry, risk, parameters.MinRewardRisk, ltfLevels, fiveMinuteLevels);
        var rr = direction == TradeDirection.Long ? (target - entry) / risk : (entry - target) / risk;
        var passedMinimumR = parameters.MinRewardRisk <= 0m || rr >= parameters.MinRewardRisk;
        var passed3R = rr >= parameters.ResearchMinRewardRisk3R;
        metrics = metrics with
        {
            TargetPassedMinimumR = passedMinimumR,
            TargetPassed3R = passed3R
        };

        var requiresMinimumR = parameters.StrictnessProfile != BbStrategyStrictnessProfile.DetectorCalibration && parameters.MinRewardRisk > 0m;
        if (requiresMinimumR && !passedMinimumR)
        {
            return Reject(diagnostics, metrics with { StagedRejectionCode = BbLiquiditySweepRejectionCodes.TargetLessThanMinimumR }, BbLiquiditySweepRejectionCodes.TargetLessThanMinimumR);
        }

        metrics = metrics with { FinalCandidate = true };

        if (!parameters.AllowTradeCreation)
        {
            return Reject(
                FinalizeDiagnostics(diagnostics, false, rr, targetSource, "DetectorCalibration", parameters.RequireRsiPrimedFilter ? metrics.RsiPassed : null, BbLiquiditySweepRejectionCodes.DetectorCalibrationOnly),
                metrics with { StagedRejectionCode = BbLiquiditySweepRejectionCodes.DetectorCalibrationOnly },
                BbLiquiditySweepRejectionCodes.DetectorCalibrationOnly);
        }

        return new BbLiquiditySweepEvaluationResult
        {
            Diagnostics = FinalizeDiagnostics(diagnostics, true, rr, targetSource, direction == TradeDirection.Long ? "EntryLong" : "EntryShort", parameters.RequireRsiPrimedFilter ? true : null),
            CandleMetrics = metrics,
            StagedRejectionCode = null,
            Direction = direction,
            EntryPrice = entry,
            StopLoss = stop,
            TakeProfit = target,
            BreakevenTriggerPrice = direction == TradeDirection.Long
                ? entry + ((target - entry) * 0.5m)
                : entry - ((entry - target) * 0.5m),
            TargetSource = targetSource,
            Reason = direction == TradeDirection.Long
                ? "Sell-side liquidity sweep below lower BB with bullish CISD confirmation."
                : "Buy-side liquidity sweep above upper BB with bearish CISD confirmation."
        };
    }

    private static LiquidityLevelDto? FindNearestLevel(
        IReadOnlyList<LiquidityLevelDto> levels,
        decimal close,
        decimal atr,
        BbLiquiditySweepParameters parameters,
        bool abovePrice)
    {
        if (levels.Count == 0)
        {
            return null;
        }

        var maxDistance = atr * parameters.MaxDistanceFromLiquidityAtrMultiplier;
        IEnumerable<LiquidityLevelDto> candidates = abovePrice
            ? levels.Where(level => level.Price >= close).OrderBy(level => level.Price)
            : levels.Where(level => level.Price <= close).OrderByDescending(level => level.Price);

        if (!parameters.AllowSweepOfAnyRecentSwing)
        {
            return candidates.FirstOrDefault();
        }

        foreach (var level in candidates)
        {
            if (maxDistance <= 0m || Math.Abs(level.Price - close) <= maxDistance)
            {
                return level;
            }
        }

        return parameters.StrictnessProfile == BbStrategyStrictnessProfile.DetectorCalibration
            ? candidates.FirstOrDefault()
            : null;
    }

    private static string MapSweepRejection(LiquiditySweepSignalDto sweep, BbLiquiditySweepParameters parameters)
    {
        if (sweep.RejectionReason == "NoLiquiditySweep")
        {
            return BbLiquiditySweepRejectionCodes.NoLiquiditySweep;
        }

        if (sweep.RejectionReason == "NoBbSweep" || (parameters.RequireSweepOutsideBb && !sweep.SweptOutsideBb))
        {
            return BbLiquiditySweepRejectionCodes.NoBollingerBandSweep;
        }

        if (!sweep.ClosedBackInsideBb && parameters.RequireCloseBackInsideBb)
        {
            return BbLiquiditySweepRejectionCodes.DidNotCloseBackInsideBb;
        }

        if (!sweep.ClosedBackAcrossLiquidityLine && parameters.RequireCloseBackAcrossLiquidityLine)
        {
            return BbLiquiditySweepRejectionCodes.DidNotCloseBackAcrossLiquidity;
        }

        return BbLiquiditySweepRejectionCodes.NoLiquiditySweep;
    }

    private static bool PassesLongRsiFilter(RsiPrimedResultDto rsi, BbLiquiditySweepParameters parameters) =>
        parameters.RsiPrimedSignalValueMode switch
        {
            RsiPrimedSignalValueMode.HaLowHigh => rsi.HaLow <= parameters.RsiOversoldLevel,
            RsiPrimedSignalValueMode.Ohlc4 => rsi.Ohlc4 <= parameters.RsiOversoldLevel,
            _ => rsi.SignalValue <= parameters.RsiOversoldLevel
        };

    private static bool PassesShortRsiFilter(RsiPrimedResultDto rsi, BbLiquiditySweepParameters parameters) =>
        parameters.RsiPrimedSignalValueMode switch
        {
            RsiPrimedSignalValueMode.HaLowHigh => rsi.HaHigh >= parameters.RsiOverboughtLevel,
            RsiPrimedSignalValueMode.Ohlc4 => rsi.Ohlc4 >= parameters.RsiOverboughtLevel,
            _ => rsi.SignalValue >= parameters.RsiOverboughtLevel
        };

    private static (decimal Target, TargetSource Source) ResolveTarget(
        TradeDirection direction,
        decimal entry,
        decimal risk,
        decimal minRewardRisk,
        IReadOnlyList<LiquidityLevelDto> ltfLevels,
        IReadOnlyList<LiquidityLevelDto> fiveMinuteLevels)
    {
        if (direction == TradeDirection.Long)
        {
            var ltfTarget = ltfLevels
                .Where(level => level.Direction == LiquidityDirection.BuySideLiquidity && level.Price > entry)
                .OrderBy(level => level.Price)
                .FirstOrDefault(level => minRewardRisk <= 0m || (level.Price - entry) / risk >= minRewardRisk);
            if (ltfTarget is not null)
            {
                return (ltfTarget.Price, TargetSource.LtfLiquidity);
            }

            var fiveTarget = fiveMinuteLevels
                .Where(level => level.Direction == LiquidityDirection.BuySideLiquidity && level.Price > entry)
                .OrderBy(level => level.Price)
                .FirstOrDefault(level => minRewardRisk <= 0m || (level.Price - entry) / risk >= minRewardRisk);
            if (fiveTarget is not null)
            {
                return (fiveTarget.Price, TargetSource.FiveMinuteLiquidity);
            }

            return (entry + (risk * Math.Max(minRewardRisk, 1m)), TargetSource.Fixed3R);
        }

        var ltfShortTarget = ltfLevels
            .Where(level => level.Direction == LiquidityDirection.SellSideLiquidity && level.Price < entry)
            .OrderByDescending(level => level.Price)
            .FirstOrDefault(level => minRewardRisk <= 0m || (entry - level.Price) / risk >= minRewardRisk);
        if (ltfShortTarget is not null)
        {
            return (ltfShortTarget.Price, TargetSource.LtfLiquidity);
        }

        var fiveShortTarget = fiveMinuteLevels
            .Where(level => level.Direction == LiquidityDirection.SellSideLiquidity && level.Price < entry)
            .OrderByDescending(level => level.Price)
            .FirstOrDefault(level => minRewardRisk <= 0m || (entry - level.Price) / risk >= minRewardRisk);
        if (fiveShortTarget is not null)
        {
            return (fiveShortTarget.Price, TargetSource.FiveMinuteLiquidity);
        }

        return (entry - (risk * Math.Max(minRewardRisk, 1m)), TargetSource.Fixed3R);
    }

    private static decimal EstimateAtr(IReadOnlyList<Candle> candles, int index)
    {
        var start = Math.Max(1, index - 14);
        decimal sum = 0m;
        var count = 0;
        for (var i = start; i <= index; i++)
        {
            var current = candles[i];
            var previous = candles[i - 1];
            sum += Math.Max(current.High - current.Low,
                Math.Max(Math.Abs(current.High - previous.Close), Math.Abs(current.Low - previous.Close)));
            count++;
        }

        return count == 0 ? 0m : sum / count;
    }

    private static BbLiquiditySweepEvaluationResult Reject(
        BbLiquiditySweepDiagnosticsDto diagnostics,
        BbLiquiditySweepCandleMetrics metrics,
        string rejectionCode) => new()
    {
        Diagnostics = FinalizeDiagnostics(diagnostics, false, diagnostics.RiskReward, diagnostics.TargetSource, "NoTrade", diagnostics.RsiFilterPassed, rejectionCode),
        CandleMetrics = metrics with { StagedRejectionCode = rejectionCode },
        StagedRejectionCode = rejectionCode,
        Reason = BbLiquiditySweepRejectionCodes.ToDisplayReason(rejectionCode)
    };

    private static BbLiquiditySweepDiagnosticsDto CreateDiagnostics(
        DateTime candleTimeUtc,
        bool inSession,
        string? sessionName,
        BollingerBandsValueDto? bb,
        string engineMode,
        bool sourceAvailable) => new()
    {
        CandleTimeUtc = candleTimeUtc,
        InAllowedSession = inSession,
        SessionName = sessionName,
        UpperBb = bb?.Upper,
        MiddleBb = bb?.Middle,
        LowerBb = bb?.Lower,
        LiquidityLineEngineMode = engineMode,
        ItsImpossibleSourceAvailable = sourceAvailable,
        FinalDecision = "NoTrade"
    };

    private static BbLiquiditySweepDiagnosticsDto CopyDiagnostics(
        BbLiquiditySweepDiagnosticsDto source,
        decimal? nearestBuy,
        decimal? nearestSell) => new()
    {
        CandleTimeUtc = source.CandleTimeUtc,
        InAllowedSession = source.InAllowedSession,
        SessionName = source.SessionName,
        UpperBb = source.UpperBb,
        MiddleBb = source.MiddleBb,
        LowerBb = source.LowerBb,
        NearestBuySideLiquidity = nearestBuy,
        NearestSellSideLiquidity = nearestSell,
        LiquidityLineEngineMode = source.LiquidityLineEngineMode,
        ItsImpossibleSourceAvailable = source.ItsImpossibleSourceAvailable,
        FinalDecision = source.FinalDecision
    };

    private static BbLiquiditySweepDiagnosticsDto CopyRsiDiagnostics(
        BbLiquiditySweepDiagnosticsDto source,
        RsiPrimedResultDto? rsi,
        bool? rsiFilterPassed) => new()
    {
        CandleTimeUtc = source.CandleTimeUtc,
        InAllowedSession = source.InAllowedSession,
        SessionName = source.SessionName,
        UpperBb = source.UpperBb,
        MiddleBb = source.MiddleBb,
        LowerBb = source.LowerBb,
        NearestBuySideLiquidity = source.NearestBuySideLiquidity,
        NearestSellSideLiquidity = source.NearestSellSideLiquidity,
        SweptLiquidityLevel = source.SweptLiquidityLevel,
        SweepDirection = source.SweepDirection,
        SweptOutsideBb = source.SweptOutsideBb,
        ClosedBackInsideBb = source.ClosedBackInsideBb,
        ClosedBackAcrossLiquidityLine = source.ClosedBackAcrossLiquidityLine,
        CisdDetected = source.CisdDetected,
        CisdLevel = source.CisdLevel,
        RsiPrimedSignalValue = rsi?.SignalValue,
        RsiPrimedHaOpen = rsi?.HaOpen,
        RsiPrimedHaHigh = rsi?.HaHigh,
        RsiPrimedHaLow = rsi?.HaLow,
        RsiPrimedHaClose = rsi?.HaClose,
        RsiPrimedOhlc4 = rsi?.Ohlc4,
        RsiPrimedOversold = rsi?.IsOversold ?? false,
        RsiPrimedOverbought = rsi?.IsOverbought ?? false,
        RsiPrimedImplementationMode = rsi?.ImplementationMode.ToString(),
        RsiFilterPassed = rsiFilterPassed,
        EntryCandidate = source.EntryCandidate,
        RejectionReason = source.RejectionReason,
        RiskReward = source.RiskReward,
        TargetSource = source.TargetSource,
        FinalDecision = source.FinalDecision,
        LiquidityLineEngineMode = source.LiquidityLineEngineMode,
        ItsImpossibleSourceAvailable = source.ItsImpossibleSourceAvailable
    };

    private static BbLiquiditySweepDiagnosticsDto CopySweepDiagnostics(
        BbLiquiditySweepDiagnosticsDto source,
        LiquidityLevelDto level,
        TradeDirection direction,
        LiquiditySweepSignalDto sweep,
        bool cisdDetected,
        decimal? cisdLevel)
    {
        var copy = CopyRsiDiagnostics(source, null, source.RsiFilterPassed);
        return new BbLiquiditySweepDiagnosticsDto
        {
            CandleTimeUtc = copy.CandleTimeUtc,
            InAllowedSession = copy.InAllowedSession,
            SessionName = copy.SessionName,
            UpperBb = copy.UpperBb,
            MiddleBb = copy.MiddleBb,
            LowerBb = copy.LowerBb,
            NearestBuySideLiquidity = copy.NearestBuySideLiquidity,
            NearestSellSideLiquidity = copy.NearestSellSideLiquidity,
            SweptLiquidityLevel = level,
            SweepDirection = direction,
            SweptOutsideBb = sweep.SweptOutsideBb,
            ClosedBackInsideBb = sweep.ClosedBackInsideBb,
            ClosedBackAcrossLiquidityLine = sweep.ClosedBackAcrossLiquidityLine,
            CisdDetected = cisdDetected,
            CisdLevel = cisdLevel,
            RsiPrimedSignalValue = copy.RsiPrimedSignalValue,
            RsiPrimedHaOpen = copy.RsiPrimedHaOpen,
            RsiPrimedHaHigh = copy.RsiPrimedHaHigh,
            RsiPrimedHaLow = copy.RsiPrimedHaLow,
            RsiPrimedHaClose = copy.RsiPrimedHaClose,
            RsiPrimedOhlc4 = copy.RsiPrimedOhlc4,
            RsiPrimedOversold = copy.RsiPrimedOversold,
            RsiPrimedOverbought = copy.RsiPrimedOverbought,
            RsiPrimedImplementationMode = copy.RsiPrimedImplementationMode,
            RsiFilterPassed = copy.RsiFilterPassed,
            EntryCandidate = copy.EntryCandidate,
            RejectionReason = copy.RejectionReason,
            RiskReward = copy.RiskReward,
            TargetSource = copy.TargetSource,
            FinalDecision = copy.FinalDecision,
            LiquidityLineEngineMode = copy.LiquidityLineEngineMode,
            ItsImpossibleSourceAvailable = copy.ItsImpossibleSourceAvailable
        };
    }

    private static BbLiquiditySweepDiagnosticsDto FinalizeDiagnostics(
        BbLiquiditySweepDiagnosticsDto source,
        bool entryCandidate,
        decimal? riskReward,
        TargetSource? targetSource,
        string finalDecision,
        bool? rsiFilterPassed,
        string? rejectionReason = null) => new()
    {
        CandleTimeUtc = source.CandleTimeUtc,
        InAllowedSession = source.InAllowedSession,
        SessionName = source.SessionName,
        UpperBb = source.UpperBb,
        MiddleBb = source.MiddleBb,
        LowerBb = source.LowerBb,
        NearestBuySideLiquidity = source.NearestBuySideLiquidity,
        NearestSellSideLiquidity = source.NearestSellSideLiquidity,
        SweptLiquidityLevel = source.SweptLiquidityLevel,
        SweepDirection = source.SweepDirection,
        SweptOutsideBb = source.SweptOutsideBb,
        ClosedBackInsideBb = source.ClosedBackInsideBb,
        ClosedBackAcrossLiquidityLine = source.ClosedBackAcrossLiquidityLine,
        CisdDetected = source.CisdDetected,
        CisdLevel = source.CisdLevel,
        RsiPrimedSignalValue = source.RsiPrimedSignalValue,
        RsiPrimedHaOpen = source.RsiPrimedHaOpen,
        RsiPrimedHaHigh = source.RsiPrimedHaHigh,
        RsiPrimedHaLow = source.RsiPrimedHaLow,
        RsiPrimedHaClose = source.RsiPrimedHaClose,
        RsiPrimedOhlc4 = source.RsiPrimedOhlc4,
        RsiPrimedOversold = source.RsiPrimedOversold,
        RsiPrimedOverbought = source.RsiPrimedOverbought,
        RsiPrimedImplementationMode = source.RsiPrimedImplementationMode,
        RsiFilterPassed = rsiFilterPassed ?? source.RsiFilterPassed,
        EntryCandidate = entryCandidate,
        RejectionReason = rejectionReason ?? source.RejectionReason,
        RiskReward = riskReward ?? source.RiskReward,
        TargetSource = targetSource ?? source.TargetSource,
        FinalDecision = finalDecision,
        LiquidityLineEngineMode = source.LiquidityLineEngineMode,
        ItsImpossibleSourceAvailable = source.ItsImpossibleSourceAvailable
    };
}
