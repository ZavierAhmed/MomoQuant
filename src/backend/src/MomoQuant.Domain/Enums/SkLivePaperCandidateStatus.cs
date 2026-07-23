namespace MomoQuant.Domain.Enums;

public enum SkLivePaperCandidateStatus
{
    Detected = 0,
    WaitingForReaction = 1,
    Confirmed = 2,
    Rejected = 3,
    Expired = 4,
    ConvertedToPaperTrade = 5
}
