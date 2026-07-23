namespace MomoQuant.Domain.Enums;

/// <summary>How useful an SK setup is right now, separate from structural clarity.</summary>
public enum SkUsefulnessStatus
{
    Fresh,
    InZone,
    WaitingForReaction,
    AlreadyReached,
    TooFarAway,
    Invalidated,
    DirectionMismatch
}
