namespace MomoQuant.Application.Strategies.VolatilityGatedSuperTrend;

public static class VolatilityGatedSuperTrendRejectionCodes
{
    public const string NoSuperTrendDirection = "NoSuperTrendDirection";
    public const string VolatilityGateFailed = "VolatilityGateFailed";
    public const string MomentumFailed = "MomentumFailed";
    public const string NoRetest = "NoRetest";
    public const string NoConfirmation = "NoConfirmation";
    public const string InvalidStopDistance = "InvalidStopDistance";
    public const string TargetLessThanMinimumR = "TargetLessThanMinimumR";
    public const string RiskRejected = "RiskRejected";
    public const string MissingIndicators = "MissingIndicators";
    public const string MissingData = "MissingData";
    public const string TimeframeNotSupported = "TimeframeNotSupported";
    public const string ExpiredAfterTrendFlip = "ExpiredAfterTrendFlip";

    public static string ToDisplayReason(string code) => code switch
    {
        NoSuperTrendDirection => "SuperTrend direction is neutral or unavailable.",
        VolatilityGateFailed => "Volatility gate failed (fast ATR / slow ATR below threshold).",
        MomentumFailed => "MACD histogram momentum confirmation failed.",
        NoRetest => "Price did not pull back near SuperTrend line for retest.",
        NoConfirmation => "Bullish/bearish confirmation candle after retest not detected.",
        InvalidStopDistance => "Stop loss distance is invalid or too small.",
        TargetLessThanMinimumR => "Take profit target does not meet minimum reward/risk.",
        RiskRejected => "Trade rejected by risk engine.",
        MissingIndicators => "Required indicators are not available.",
        MissingData => "Insufficient candle data.",
        TimeframeNotSupported => "Timeframe is not supported.",
        ExpiredAfterTrendFlip => "Setup window expired after trend flip.",
        _ => "No valid entry setup met strategy conditions."
    };
}
