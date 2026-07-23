namespace MomoQuant.Application.Strategies.BbLiquiditySweep;

public static class BbLiquiditySweepRejectionCodes
{
    public const string OutsideSession = "OutsideSession";
    public const string NoBollingerBandSweep = "NoBollingerBandSweep";
    public const string NoLiquidityLevelsDetected = "NoLiquidityLevelsDetected";
    public const string NoNearbyLiquidityLevel = "NoNearbyLiquidityLevel";
    public const string NoLiquiditySweep = "NoLiquiditySweep";
    public const string DidNotCloseBackInsideBb = "DidNotCloseBackInsideBB";
    public const string DidNotCloseBackAcrossLiquidity = "DidNotCloseBackAcrossLiquidity";
    public const string NoCisdCandidate = "NoCisdCandidate";
    public const string NoCisdConfirmation = "NoCisdConfirmation";
    public const string RsiPrimedFilterFailed = "RsiPrimedFilterFailed";
    public const string TargetLessThanMinimumR = "TargetLessThanMinimumR";
    public const string InvalidStopDistance = "InvalidStopDistance";
    public const string SessionLossLimitReached = "SessionLossLimitReached";
    public const string MissingRequiredTimeframeData = "MissingRequiredTimeframeData";
    public const string DetectorCalibrationOnly = "DetectorCalibrationOnly";
    public const string TimeframeNotSupported = "TimeframeNotSupported";
    public const string NoCurrentCandle = "NoCurrentCandle";

    public static string ToDisplayReason(string code) => code switch
    {
        OutsideSession => "Candle is outside allowed trading sessions.",
        NoBollingerBandSweep => "Price did not sweep outside Bollinger Bands.",
        NoLiquidityLevelsDetected => "No liquidity levels were detected on 1m/5m timeframes.",
        NoNearbyLiquidityLevel => "No liquidity level was close enough to price.",
        NoLiquiditySweep => "Liquidity level was not swept.",
        DidNotCloseBackInsideBb => "Price did not close back inside Bollinger Bands.",
        DidNotCloseBackAcrossLiquidity => "Price did not close back across the liquidity line.",
        NoCisdCandidate => "No CISD candidate structure was found after sweep.",
        NoCisdConfirmation => "CISD confirmation did not occur within max bars after sweep.",
        RsiPrimedFilterFailed => "RSI Primed filter rejected the setup.",
        TargetLessThanMinimumR => "Target did not meet minimum reward-to-risk.",
        InvalidStopDistance => "Stop distance was invalid for the setup.",
        SessionLossLimitReached => "Session loss limit was reached.",
        MissingRequiredTimeframeData => "Required timeframe or indicator data is missing.",
        DetectorCalibrationOnly => "Detector calibration only — not a final strategy result.",
        TimeframeNotSupported => "Execution timeframe is not supported.",
        NoCurrentCandle => "No current candle is available.",
        _ => code
    };
}
