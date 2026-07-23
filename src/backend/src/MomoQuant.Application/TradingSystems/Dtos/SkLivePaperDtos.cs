namespace MomoQuant.Application.TradingSystems.Dtos;

public sealed class SkLivePaperDefaultsDto
{
    public string HigherTimeframe { get; init; } = "4h";
    public string PrimaryTimeframe { get; init; } = "1h";
    public IReadOnlyList<string> AdditionalTimeframes { get; init; } = [];
    public decimal StartingBalance { get; init; } = 10_000m;
    public decimal RiskPerPaperTradePercent { get; init; } = 0.5m;
    public int MaxPaperTradesPerDay { get; init; } = 3;
    public int MaxOpenPaperPositions { get; init; } = 1;
    public bool AllowLong { get; init; } = true;
    public bool AllowShort { get; init; } = true;
    public bool RequireHtfAgreement { get; init; } = true;
    public decimal MinClarityScore { get; init; } = 60m;
    public decimal MinUsefulnessScore { get; init; } = 60m;
    public bool RequireReactionConfirmation { get; init; } = true;
    public string ConfirmationMode { get; init; } = "CloseBackInDirection";
    public decimal SimulatedLeverage { get; init; } = 3m;
    public string SimulationMode { get; init; } = "SK_LIVE_PAPER";
    public string SafetyDisclaimer { get; init; } =
        "SK LivePaper uses simulated orders only. It does not connect to your Binance account and cannot place real trades.";
}

public sealed class CreateSkLivePaperSessionRequest
{
    public string SessionName { get; set; } = string.Empty;
    public long ExchangeId { get; set; }
    public long SymbolId { get; set; }
    public string HigherTimeframe { get; set; } = "4h";
    public string PrimaryTimeframe { get; set; } = "1h";
    public IReadOnlyList<string> AdditionalTimeframes { get; set; } = [];
    public decimal StartingBalance { get; set; } = 10_000m;
    public decimal RiskPerPaperTradePercent { get; set; } = 0.5m;
    public int MaxPaperTradesPerDay { get; set; } = 3;
    public int MaxOpenPaperPositions { get; set; } = 1;
    public bool AllowLong { get; set; } = true;
    public bool AllowShort { get; set; } = true;
    public bool RequireHtfAgreement { get; set; } = true;
    public decimal MinClarityScore { get; set; } = 60m;
    public decimal MinUsefulnessScore { get; set; } = 60m;
    public bool RequireReactionConfirmation { get; set; } = true;
    public string ConfirmationMode { get; set; } = "CloseBackInDirection";
    public decimal SimulatedLeverage { get; set; } = 3m;
}

public sealed class SkLivePaperSessionDto
{
    public long Id { get; init; }
    public string SessionName { get; init; } = string.Empty;
    public long ExchangeId { get; init; }
    public long SymbolId { get; init; }
    public string Symbol { get; init; } = string.Empty;
    public string HigherTimeframe { get; init; } = string.Empty;
    public string PrimaryTimeframe { get; init; } = string.Empty;
    public decimal StartingBalance { get; init; }
    public decimal CurrentBalance { get; init; }
    public decimal RiskPerPaperTradePercent { get; init; }
    public int MaxPaperTradesPerDay { get; init; }
    public int MaxOpenPaperPositions { get; init; }
    public bool AllowLong { get; init; }
    public bool AllowShort { get; init; }
    public bool RequireHtfAgreement { get; init; }
    public decimal MinClarityScore { get; init; }
    public decimal MinUsefulnessScore { get; init; }
    public bool RequireReactionConfirmation { get; init; }
    public string ConfirmationMode { get; init; } = string.Empty;
    public decimal SimulatedLeverage { get; init; }
    public string Status { get; init; } = string.Empty;
    public string SimulationMode { get; init; } = "SK_LIVE_PAPER";
    public DateTime? StartedAtUtc { get; init; }
    public DateTime? StoppedAtUtc { get; init; }
    public DateTime? LastHeartbeatUtc { get; init; }
    public DateTime? LastAnalyzedCandleUtc { get; init; }
    public string? LastError { get; init; }
    public DateTime CreatedAtUtc { get; init; }
}

public sealed class SkLivePaperSessionSummaryDto
{
    public long Id { get; init; }
    public string SessionName { get; init; } = string.Empty;
    public string Symbol { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public decimal CurrentBalance { get; init; }
    public decimal NetSimulatedPnl { get; init; }
    public int OpenTrades { get; init; }
    public int ClosedTrades { get; init; }
    public DateTime? LastAnalyzedCandleUtc { get; init; }
    public string SimulationMode { get; init; } = "SK_LIVE_PAPER";
}

public sealed class SkLivePaperCandidateDto
{
    public long Id { get; init; }
    public long SessionId { get; init; }
    public string Symbol { get; init; } = string.Empty;
    public string Direction { get; init; } = string.Empty;
    public string SequenceStatus { get; init; } = string.Empty;
    public string ValidityStatus { get; init; } = string.Empty;
    public string UsefulnessStatus { get; init; } = string.Empty;
    public decimal ClarityScore { get; init; }
    public decimal UsefulnessScore { get; init; }
    public decimal ReactionZoneLow { get; init; }
    public decimal ReactionZoneHigh { get; init; }
    public decimal InvalidationLevel { get; init; }
    public decimal Target1 { get; init; }
    public decimal Target2 { get; init; }
    public decimal CurrentPrice { get; init; }
    public string CandidateStatus { get; init; } = string.Empty;
    public string? RejectionReason { get; init; }
    public DateTime CreatedAtUtc { get; init; }
}

public sealed class SkLivePaperTradeDto
{
    public long Id { get; init; }
    public long SessionId { get; init; }
    public string Symbol { get; init; } = string.Empty;
    public string Direction { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string SimulationMode { get; init; } = "SK_LIVE_PAPER";
    public DateTime EntryTimeUtc { get; init; }
    public decimal EntryPrice { get; init; }
    public decimal Quantity { get; init; }
    public decimal SimulatedLeverage { get; init; }
    public decimal MarginUsed { get; init; }
    public decimal StopLoss { get; init; }
    public decimal TakeProfit1 { get; init; }
    public decimal TakeProfit2 { get; init; }
    public DateTime? ExitTimeUtc { get; init; }
    public decimal? ExitPrice { get; init; }
    public string? ExitReason { get; init; }
    public decimal NetPnl { get; init; }
    public decimal NetPnlPercent { get; init; }
}

public sealed class SkLivePaperEventDto
{
    public long Id { get; init; }
    public string EventType { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public DateTime CreatedAtUtc { get; init; }
}

public sealed class SkLivePaperStatusDto
{
    public SkLivePaperSessionDto Session { get; init; } = new();
    public int OpenTrades { get; init; }
    public int ClosedTrades { get; init; }
    public decimal NetSimulatedPnl { get; init; }
    public SkLivePaperCandidateDto? LastCandidate { get; init; }
    public SkLivePaperDiagnosticsDto Diagnostics { get; init; } = new();
    public string SafetyDisclaimer { get; init; } = string.Empty;
}

public sealed class SkLivePaperDiagnosticsDto
{
    public string WebSocketStatus { get; init; } = "Unknown";
    public int ClosedCandlesProcessed { get; init; }
    public int SkAnalysesRun { get; init; }
    public int CandidatesDetected { get; init; }
    public int CandidatesRejected { get; init; }
    public int PaperTradesOpened { get; init; }
    public int PaperTradesClosed { get; init; }
    public DateTime? LastHeartbeatUtc { get; init; }
    public string? LastError { get; init; }
}

public sealed class SkLivePaperChartDto
{
    public long SessionId { get; init; }
    public string Symbol { get; init; } = string.Empty;
    public string PrimaryTimeframe { get; init; } = string.Empty;
    public string HigherTimeframe { get; init; } = string.Empty;
    public decimal CurrentPrice { get; init; }
    public IReadOnlyList<SkChartCandleDto> Candles { get; init; } = [];
    public IReadOnlyList<ChartOverlayDto> ChartOverlays { get; init; } = [];
    public IReadOnlyList<SkLivePaperTradeDto> OpenTrades { get; init; } = [];
}
