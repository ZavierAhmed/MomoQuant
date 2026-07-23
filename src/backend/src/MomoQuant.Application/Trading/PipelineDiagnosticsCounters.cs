namespace MomoQuant.Application.Trading;

public sealed class PipelineDiagnosticsCounters
{
    public int StrategiesEvaluated { get; set; }
    public int NoTradeSignals { get; set; }
    public int EntrySignals { get; set; }
    public int CandidateSignals { get; set; }
    public int WarningSignals { get; set; }
    public int InvalidSignals { get; set; }
    public int ConfidenceEvaluations { get; set; }
    public int ConfidenceApproved { get; set; }
    public int ConfidenceRejected { get; set; }
    public int RiskEvaluations { get; set; }
    public int RiskApproved { get; set; }
    public int RiskRejected { get; set; }
}
