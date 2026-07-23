using MomoQuant.Domain.Common;
using MomoQuant.Domain.Enums;

namespace MomoQuant.Domain.TradingSystems;

public class SkLivePaperCandidate : AuditableEntity
{
    public long SessionId { get; set; }
    public long? AnalysisId { get; set; }
    public long SymbolId { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public string HigherTimeframe { get; set; } = string.Empty;
    public string PrimaryTimeframe { get; set; } = string.Empty;
    public string Direction { get; set; } = string.Empty;
    public string SequenceStatus { get; set; } = string.Empty;
    public string ValidityStatus { get; set; } = string.Empty;
    public string UsefulnessStatus { get; set; } = string.Empty;
    public decimal ClarityScore { get; set; }
    public decimal UsefulnessScore { get; set; }
    public decimal ReactionZoneLow { get; set; }
    public decimal ReactionZoneHigh { get; set; }
    public decimal StrongReactionZoneLow { get; set; }
    public decimal StrongReactionZoneHigh { get; set; }
    public decimal InvalidationLevel { get; set; }
    public decimal Target1 { get; set; }
    public decimal Target2 { get; set; }
    public decimal CurrentPrice { get; set; }
    public SkLivePaperCandidateStatus CandidateStatus { get; set; } = SkLivePaperCandidateStatus.Detected;
    public string? RejectionReason { get; set; }
    public DateTime? ConfirmedAtUtc { get; set; }
    public DateTime? ExpiredAtUtc { get; set; }
    public string CandidateKey { get; set; } = string.Empty;
}
