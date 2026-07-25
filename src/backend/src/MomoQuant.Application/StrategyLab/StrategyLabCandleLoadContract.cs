namespace MomoQuant.Application.StrategyLab;

/// <summary>
/// Version constants for StrategyLab candle load contracts.
/// </summary>
public static class StrategyLabCandleLoadContractVersions
{
    public const string LegacyV1 = "StrategyLabCandleLoad/v1-Legacy";
    public const string ExactExclusiveV2 = "StrategyLabCandleLoad/v2-ExactExclusive";
    public const string Current = ExactExclusiveV2;
}

/// <summary>
/// Contract helper methods for StrategyLab candle load behavior.
/// </summary>
public static class StrategyLabCandleLoadContract
{
    /// <summary>
    /// Determines if a candle with the given open time should be included in the evaluation range
    /// based on the contract version.
    /// </summary>
    /// <param name="version">Contract version (null, LegacyV1, or ExactExclusiveV2)</param>
    /// <param name="open">Candle open time</param>
    /// <param name="from">Range start (inclusive)</param>
    /// <param name="to">Range end (inclusive for Legacy, exclusive for V2)</param>
    /// <returns>True if the candle should be included in the evaluation range</returns>
    /// <exception cref="InvalidOperationException">If an unknown non-null version is provided</exception>
    public static bool ContainsEvaluationOpenTime(string? version, DateTime open, DateTime from, DateTime to)
    {
        // Normalize all timestamps to UTC
        var openUtc = DateTime.SpecifyKind(open, DateTimeKind.Utc);
        var fromUtc = DateTime.SpecifyKind(from, DateTimeKind.Utc);
        var toUtc = DateTime.SpecifyKind(to, DateTimeKind.Utc);

        if (version == StrategyLabCandleLoadContractVersions.ExactExclusiveV2)
        {
            // V2: Exact Exclusive - open >= from && open < to
            return openUtc >= fromUtc && openUtc < toUtc;
        }
        
        if (version == StrategyLabCandleLoadContractVersions.LegacyV1 || version is null)
        {
            // Legacy/null: Inclusive - open >= from && open <= to
            return openUtc >= fromUtc && openUtc <= toUtc;
        }

        throw new InvalidOperationException(
            $"Unknown CandleLoadContractVersion '{version}'. " +
            $"Expected null, '{StrategyLabCandleLoadContractVersions.LegacyV1}', " +
            $"or '{StrategyLabCandleLoadContractVersions.ExactExclusiveV2}'.");
    }
}
