using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Strategies;

namespace MomoQuant.Application.Strategies;

public static class StrategyParameterReader
{
    public static decimal GetDecimal(IReadOnlyDictionary<string, string> parameters, string key, decimal defaultValue) =>
        parameters.TryGetValue(key, out var value) && decimal.TryParse(value, out var parsed)
            ? parsed
            : defaultValue;

    public static int GetInt(IReadOnlyDictionary<string, string> parameters, string key, int defaultValue) =>
        parameters.TryGetValue(key, out var value) && int.TryParse(value, out var parsed)
            ? parsed
            : defaultValue;

    public static bool GetBool(IReadOnlyDictionary<string, string> parameters, string key, bool defaultValue) =>
        parameters.TryGetValue(key, out var value) && bool.TryParse(value, out var parsed)
            ? parsed
            : defaultValue;

    public static string GetString(IReadOnlyDictionary<string, string> parameters, string key, string defaultValue) =>
        parameters.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : defaultValue;
}

public abstract class StrategyBase : ITradingStrategy
{
    public abstract StrategyCode Code { get; }
    public abstract string Name { get; }
    public abstract string Description { get; }
    public abstract IReadOnlyCollection<MarketRegime> SupportedRegimes { get; }
    public abstract IReadOnlyCollection<Timeframe> SupportedTimeframes { get; }

    public abstract StrategySignalResult Evaluate(StrategyContext context);

    protected static StrategySignalResult NoTrade(string reason) => new()
    {
        SignalType = SignalType.NoTrade,
        Direction = TradeDirection.None,
        Strength = 0m,
        ConfidenceContribution = 0m,
        Reason = reason
    };

    protected static StrategySignalResult NoTrade(string rejectionCode, string displayReason, string? rawDataJson = null) => new()
    {
        SignalType = SignalType.NoTrade,
        Direction = TradeDirection.None,
        Strength = 0m,
        ConfidenceContribution = 0m,
        Reason = rejectionCode,
        RawDataJson = rawDataJson
    };

    protected static StrategySignalResult Entry(
        TradeDirection direction,
        decimal strength,
        decimal confidenceContribution,
        decimal entryPrice,
        decimal? stopLoss,
        decimal? takeProfit,
        string reason,
        string? rawDataJson = null) => new()
    {
        SignalType = SignalType.Entry,
        Direction = direction,
        Strength = strength,
        ConfidenceContribution = confidenceContribution,
        EntryPrice = entryPrice,
        SuggestedStopLoss = stopLoss,
        SuggestedTakeProfit = takeProfit,
        Reason = reason,
        RawDataJson = rawDataJson
    };

    protected static bool IsSupportedRegime(MarketRegime regime, IReadOnlyCollection<MarketRegime> supported) =>
        supported.Contains(regime);

    protected static bool IsSupportedTimeframe(Timeframe timeframe, IReadOnlyCollection<Timeframe> supported) =>
        supported.Contains(timeframe);
}
