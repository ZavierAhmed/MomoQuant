namespace MomoQuant.Application.Strategies.BbLiquiditySweep.Dtos;

public sealed class BbLiquiditySweepFunnelCounts
{
    public int Evaluations { get; set; }
    public int CandlesInAllowedSession { get; set; }
    public int CandlesOutsideSession { get; set; }
    public int BollingerBandUpperWickBreaks { get; set; }
    public int BollingerBandLowerWickBreaks { get; set; }
    public int CandlesClosedBackInsideBb { get; set; }
    public int FiveMinuteLiquidityLevelsDetected { get; set; }
    public int OneMinuteLiquidityLevelsDetected { get; set; }
    public int BuySideLiquidityLevelsAvailable { get; set; }
    public int SellSideLiquidityLevelsAvailable { get; set; }
    public int BuySideLiquiditySweeps { get; set; }
    public int SellSideLiquiditySweeps { get; set; }
    public int CloseBackAcrossLiquidityLine { get; set; }
    public int CisdCandidates { get; set; }
    public int CisdConfirmed { get; set; }
    public int RsiPrimedEvaluations { get; set; }
    public int RsiPrimedPassed { get; set; }
    public int TargetPassed3R { get; set; }
    public int TargetPassedMinimumR { get; set; }
    public int FinalCandidateSignals { get; set; }
    public int TradesCreated { get; set; }

    public string StrictnessProfile { get; set; } = BbStrategyStrictnessProfile.BalancedResearch.ToString();
    public string LiquidityLineEngineMode { get; set; } = "MOMO_APPROXIMATION";
    public bool SourceCodeAvailable { get; set; }
    public bool DetectorCalibrationMode { get; set; }

    public Dictionary<string, int> NoTradeReasonBreakdown { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public string? TopNoTradeReason =>
        NoTradeReasonBreakdown.Count == 0
            ? null
            : NoTradeReasonBreakdown.OrderByDescending(pair => pair.Value).First().Key;

    public int TopNoTradeReasonCount =>
        string.IsNullOrWhiteSpace(TopNoTradeReason) || !NoTradeReasonBreakdown.TryGetValue(TopNoTradeReason, out var count)
            ? 0
            : count;

    public string BuildPipelineSummary()
    {
        var bbSweeps = BollingerBandUpperWickBreaks + BollingerBandLowerWickBreaks;
        if (bbSweeps == 0)
        {
            return "BB sweeps 0 → no setup.";
        }

        return $"BB sweeps {bbSweeps} → Liquidity sweeps {BuySideLiquiditySweeps + SellSideLiquiditySweeps} → CISD {CisdConfirmed} → 3R target {TargetPassed3R} → Candidates {FinalCandidateSignals} → Trades {TradesCreated}";
    }

    public void RecordRejection(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return;
        }

        NoTradeReasonBreakdown.TryGetValue(code, out var count);
        NoTradeReasonBreakdown[code] = count + 1;
    }

    public BbLiquiditySweepFunnelCounts Clone() => new()
    {
        Evaluations = Evaluations,
        CandlesInAllowedSession = CandlesInAllowedSession,
        CandlesOutsideSession = CandlesOutsideSession,
        BollingerBandUpperWickBreaks = BollingerBandUpperWickBreaks,
        BollingerBandLowerWickBreaks = BollingerBandLowerWickBreaks,
        CandlesClosedBackInsideBb = CandlesClosedBackInsideBb,
        FiveMinuteLiquidityLevelsDetected = FiveMinuteLiquidityLevelsDetected,
        OneMinuteLiquidityLevelsDetected = OneMinuteLiquidityLevelsDetected,
        BuySideLiquidityLevelsAvailable = BuySideLiquidityLevelsAvailable,
        SellSideLiquidityLevelsAvailable = SellSideLiquidityLevelsAvailable,
        BuySideLiquiditySweeps = BuySideLiquiditySweeps,
        SellSideLiquiditySweeps = SellSideLiquiditySweeps,
        CloseBackAcrossLiquidityLine = CloseBackAcrossLiquidityLine,
        CisdCandidates = CisdCandidates,
        CisdConfirmed = CisdConfirmed,
        RsiPrimedEvaluations = RsiPrimedEvaluations,
        RsiPrimedPassed = RsiPrimedPassed,
        TargetPassed3R = TargetPassed3R,
        TargetPassedMinimumR = TargetPassedMinimumR,
        FinalCandidateSignals = FinalCandidateSignals,
        TradesCreated = TradesCreated,
        StrictnessProfile = StrictnessProfile,
        LiquidityLineEngineMode = LiquidityLineEngineMode,
        SourceCodeAvailable = SourceCodeAvailable,
        DetectorCalibrationMode = DetectorCalibrationMode,
        NoTradeReasonBreakdown = new Dictionary<string, int>(NoTradeReasonBreakdown, StringComparer.OrdinalIgnoreCase)
    };
}

public sealed record BbLiquiditySweepCandleMetrics
{
    public bool InAllowedSession { get; init; }
    public bool UpperBbWickBreak { get; init; }
    public bool LowerBbWickBreak { get; init; }
    public bool ClosedBackInsideBb { get; init; }
    public int OneMinuteLevelsActive { get; init; }
    public int FiveMinuteLevelsActive { get; init; }
    public int BuySideLevelsAvailable { get; init; }
    public int SellSideLevelsAvailable { get; init; }
    public bool BuySideSweep { get; init; }
    public bool SellSideSweep { get; init; }
    public bool CloseBackAcrossLiquidity { get; init; }
    public bool CisdCandidate { get; init; }
    public bool CisdConfirmed { get; init; }
    public bool RsiEvaluated { get; init; }
    public bool RsiPassed { get; init; }
    public bool TargetPassedMinimumR { get; init; }
    public bool TargetPassed3R { get; init; }
    public bool FinalCandidate { get; init; }
    public string? StagedRejectionCode { get; init; }
    public decimal? NearestBuySideLevelDistance { get; init; }
    public decimal? NearestSellSideLevelDistance { get; init; }
}

public sealed class BbLiquiditySweepSampleEvaluation
{
    public DateTime CandleTimeUtc { get; init; }
    public string? StagedRejectionCode { get; init; }
    public string? DisplayReason { get; init; }
    public BbLiquiditySweepDiagnosticsDto? Diagnostics { get; init; }
}
