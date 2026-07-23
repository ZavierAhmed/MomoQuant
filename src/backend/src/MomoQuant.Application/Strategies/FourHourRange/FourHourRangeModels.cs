using MomoQuant.Application.MarketData;
using MomoQuant.Domain.Enums;

namespace MomoQuant.Application.Strategies.FourHourRange;

public enum FourHourRangeState
{
    RangeNotReady,
    WaitingForBreakout,
    AwaitingShortReentry,
    AwaitingLongReentry,
    ShortSignalReady,
    LongSignalReady,
    CompletedOrExpired
}

public sealed class FourHourRangeDto
{
    public long SymbolId { get; init; }
    public required string Timeframe { get; init; }
    public required string NewYorkTradingDate { get; init; }
    public DateTime RangeStartUtc { get; init; }
    public DateTime RangeEndUtc { get; init; }
    public DateTime NewYorkDayEndUtc { get; init; }
    public decimal? RangeHigh { get; init; }
    public decimal? RangeLow { get; init; }
    public decimal? RangePercent { get; init; }
    public int CandleCountUsed { get; init; }
    public int ExpectedCandleCount { get; init; }
    public bool RangeReady { get; init; }
    public bool IsValid { get; init; }
    public string? InvalidReason { get; init; }
}

public sealed class FourHourRangeReEntryParameters
{
    public string AnchorTimezone { get; init; } = "America/New_York";
    public int RangeStartHour { get; init; } = 0;
    public int RangeDurationHours { get; init; } = 4;
    public decimal RewardRiskRatio { get; init; } = 2.0m;
    public int MaxTradesPerDay { get; init; } = 3;
    public bool AllowMultipleTradesPerDay { get; init; } = true;
    public bool RequireCloseOutsideRange { get; init; } = true;
    public bool RequireCloseBackInsideRange { get; init; } = true;
    public bool UseWicksForBreakout { get; init; }
    public string EntryMode { get; init; } = "Close";
    public string StopMode { get; init; } = "BreakoutExtreme";
    public decimal StopLossBufferPercent { get; init; } = 0.02m;
    public decimal StopLossBufferTicks { get; init; }
    public decimal StopLossBufferAtrMultiplier { get; init; }
    public decimal MaxStopDistancePercent { get; init; } = 1.5m;
    public bool AllowLargeBreakoutStructureStop { get; init; }
    public decimal MinRangePercent { get; init; } = 0.10m;
    public decimal MaxRangePercent { get; init; } = 4.00m;
    public decimal MinStrength { get; init; } = 55m;
    public string SupportedTimeframes { get; init; } = "3m,5m,15m";
    public string PreferredTimeframe { get; init; } = "5m";
    public bool DisableAfterNewYorkDayEnd { get; init; } = true;
    public bool AllowChoppy { get; init; }
    public bool AllowHighVolatility { get; init; }

    public IReadOnlyCollection<Timeframe> ResolveSupportedTimeframes()
    {
        var values = SupportedTimeframes
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => TimeframeParser.TryParse(value, out var timeframe) ? timeframe : default)
            .Where(timeframe => timeframe != default)
            .Distinct()
            .ToList();

        return values.Count > 0
            ? values
            : [Timeframe.M3, Timeframe.M5, Timeframe.M15];
    }

    public static FourHourRangeReEntryParameters From(IReadOnlyDictionary<string, string> parameters) => new()
    {
        AnchorTimezone = StrategyParameterReader.GetString(parameters, "AnchorTimezone", "America/New_York"),
        RangeStartHour = StrategyParameterReader.GetInt(parameters, "RangeStartHour", 0),
        RangeDurationHours = Math.Max(1, StrategyParameterReader.GetInt(parameters, "RangeDurationHours", 4)),
        RewardRiskRatio = StrategyParameterReader.GetDecimal(parameters, "RewardRiskRatio", 2.0m),
        MaxTradesPerDay = Math.Max(1, StrategyParameterReader.GetInt(parameters, "MaxTradesPerDay", 3)),
        AllowMultipleTradesPerDay = StrategyParameterReader.GetBool(parameters, "AllowMultipleTradesPerDay", true),
        RequireCloseOutsideRange = StrategyParameterReader.GetBool(parameters, "RequireCloseOutsideRange", true),
        RequireCloseBackInsideRange = StrategyParameterReader.GetBool(parameters, "RequireCloseBackInsideRange", true),
        UseWicksForBreakout = StrategyParameterReader.GetBool(parameters, "UseWicksForBreakout", false),
        EntryMode = StrategyParameterReader.GetString(parameters, "EntryMode", "Close"),
        StopMode = StrategyParameterReader.GetString(parameters, "StopMode", "BreakoutExtreme"),
        StopLossBufferPercent = StrategyParameterReader.GetDecimal(parameters, "StopLossBufferPercent", 0.02m),
        StopLossBufferTicks = StrategyParameterReader.GetDecimal(parameters, "StopLossBufferTicks", 0m),
        StopLossBufferAtrMultiplier = StrategyParameterReader.GetDecimal(parameters, "StopLossBufferAtrMultiplier", 0m),
        MaxStopDistancePercent = StrategyParameterReader.GetDecimal(parameters, "MaxStopDistancePercent", 1.5m),
        AllowLargeBreakoutStructureStop = StrategyParameterReader.GetBool(parameters, "AllowLargeBreakoutStructureStop", false),
        MinRangePercent = StrategyParameterReader.GetDecimal(parameters, "MinRangePercent", 0.10m),
        MaxRangePercent = StrategyParameterReader.GetDecimal(parameters, "MaxRangePercent", 4.00m),
        MinStrength = StrategyParameterReader.GetDecimal(parameters, "MinStrength", 55m),
        SupportedTimeframes = StrategyParameterReader.GetString(parameters, "SupportedTimeframes", "3m,5m,15m"),
        PreferredTimeframe = StrategyParameterReader.GetString(parameters, "PreferredTimeframe", "5m"),
        DisableAfterNewYorkDayEnd = StrategyParameterReader.GetBool(parameters, "DisableAfterNewYorkDayEnd", true),
        AllowChoppy = StrategyParameterReader.GetBool(parameters, "AllowChoppy", false),
        AllowHighVolatility = StrategyParameterReader.GetBool(parameters, "AllowHighVolatility", false)
    };
}
