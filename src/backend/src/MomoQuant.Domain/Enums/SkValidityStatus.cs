namespace MomoQuant.Domain.Enums;

/// <summary>Whether an SK sequence is structurally valid for display as a scenario.</summary>
public enum SkValidityStatus
{
    Valid,
    Weak,
    Invalid,
    DirectionMismatch,
    AlreadyReached,
    StructureInvalidated,
    InsufficientData
}
