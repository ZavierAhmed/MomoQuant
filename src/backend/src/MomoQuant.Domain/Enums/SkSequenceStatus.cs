namespace MomoQuant.Domain.Enums;

/// <summary>Lifecycle state of an SK sequence structure (analysis only).</summary>
public enum SkSequenceStatus
{
    Building,
    WaitingForCorrection,
    InsideCorrectionZone,
    ReactingFromZone,
    TargetReached,
    Overextended,
    Completed,
    Invalidated,
    DirectionMismatch,
    LowClarity,
    MissingData
}
