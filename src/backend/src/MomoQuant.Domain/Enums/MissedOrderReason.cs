namespace MomoQuant.Domain.Enums;

public enum MissedOrderReason
{
    MakerNotFilled = 1,
    PriceMovedAway = 2,
    SpreadTooWide = 3,
    Timeout = 4,
    RiskRejected = 5
}
