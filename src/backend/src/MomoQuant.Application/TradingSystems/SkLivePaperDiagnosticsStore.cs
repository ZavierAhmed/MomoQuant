using System.Collections.Concurrent;
using MomoQuant.Application.TradingSystems.Dtos;

namespace MomoQuant.Application.TradingSystems;

public sealed class SkLivePaperDiagnosticsStore
{
    private readonly ConcurrentDictionary<long, SkLivePaperSessionDiagnostics> _sessions = new();

    public SkLivePaperSessionDiagnostics GetOrCreate(long sessionId) =>
        _sessions.GetOrAdd(sessionId, _ => new SkLivePaperSessionDiagnostics());

    public void Remove(long sessionId) => _sessions.TryRemove(sessionId, out _);
}

public sealed class SkLivePaperSessionDiagnostics
{
    public string WebSocketStatus { get; set; } = "Unknown";
    public int ClosedCandlesProcessed { get; set; }
    public int SkAnalysesRun { get; set; }
    public int CandidatesDetected { get; set; }
    public int CandidatesRejected { get; set; }
    public int PaperTradesOpened { get; set; }
    public int PaperTradesClosed { get; set; }
    public SkSystemAnalysisResultDto? LastAnalysis { get; set; }
    public SkLivePaperCandidateDto? LastCandidate { get; set; }
}
