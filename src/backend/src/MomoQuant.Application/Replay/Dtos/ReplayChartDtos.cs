namespace MomoQuant.Application.Replay.Dtos;

public sealed class ReplayChartQuery
{
    public int? UpToFrameIndex { get; init; }

    public int? CurrentFrameIndex { get; init; }

    public int? FromFrameIndex { get; init; }

    public int? ToFrameIndex { get; init; }

    public int? CandlesBefore { get; init; }

    public int? CandlesAfter { get; init; }

    public bool IncludeFutureContext { get; init; }
}

public sealed class ReplayChartDto
{
    public long ReplaySessionId { get; init; }

    public required string Symbol { get; init; }

    public required string Exchange { get; init; }

    public required string Timeframe { get; init; }

    public int CurrentFrameIndex { get; init; }

    public int TotalFrames { get; init; }

    public bool StrictReplayMode { get; init; }

    public bool IndicatorsMissing { get; init; }

    public string? IndicatorWarning { get; init; }

    public required IReadOnlyList<ReplayChartCandleDto> Candles { get; init; }

    public required IReadOnlyList<ReplayChartIndicatorDto> Indicators { get; init; }

    public required IReadOnlyList<ReplayChartStrategyMarkerDto> StrategyMarkers { get; init; }

    public required IReadOnlyList<ReplayChartRiskMarkerDto> RiskMarkers { get; init; }

    public required IReadOnlyList<ReplayChartExecutionMarkerDto> ExecutionMarkers { get; init; }

    public required IReadOnlyList<ReplayChartRangeLevelDto> RangeLevels { get; init; }
}

public sealed class ReplayChartCandleDto
{
    public int FrameIndex { get; init; }

    public long CandleId { get; init; }

    public DateTime Time { get; init; }

    public decimal Open { get; init; }

    public decimal High { get; init; }

    public decimal Low { get; init; }

    public decimal Close { get; init; }

    public decimal Volume { get; init; }

    public bool IsFutureContext { get; init; }
}

public sealed class ReplayChartIndicatorDto
{
    public int FrameIndex { get; init; }

    public long CandleId { get; init; }

    public DateTime Time { get; init; }

    public decimal? Ema20 { get; init; }

    public decimal? Ema50 { get; init; }

    public decimal? Ema200 { get; init; }

    public decimal? Vwap { get; init; }

    public decimal? Rsi14 { get; init; }

    public decimal? Atr14 { get; init; }

    public decimal? VolumeSma20 { get; init; }

    public bool SwingHigh { get; init; }

    public bool SwingLow { get; init; }

    public string? MarketStructure { get; init; }
}

public sealed class ReplayChartStrategyMarkerDto
{
    public int FrameIndex { get; init; }

    public DateTime Time { get; init; }

    public required string StrategyCode { get; init; }

    public required string SignalType { get; init; }

    public required string Direction { get; init; }

    public decimal Price { get; init; }

    public required string Reason { get; init; }
}

public sealed class ReplayChartRiskMarkerDto
{
    public int FrameIndex { get; init; }

    public DateTime Time { get; init; }

    public required string Decision { get; init; }

    public decimal Price { get; init; }

    public string? RejectedRuleKey { get; init; }

    public required string Reason { get; init; }
}

public sealed class ReplayChartExecutionMarkerDto
{
    public int FrameIndex { get; init; }

    public DateTime Time { get; init; }

    public required string Type { get; init; }

    public required string Direction { get; init; }

    public decimal Price { get; init; }

    public required string Label { get; init; }

    public decimal? Pnl { get; init; }
}

public sealed class ReplayChartRangeLevelDto
{
    public required string Label { get; init; }

    public decimal Price { get; init; }

    public DateTime StartUtc { get; init; }

    public DateTime EndUtc { get; init; }

    public required string Color { get; init; }
}
