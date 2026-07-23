namespace MomoQuant.Domain.Enums;

public enum OrderStatus
{
    Pending = 1,
    Submitted = 2,
    Open = 3,
    PartiallyFilled = 4,
    Filled = 5,
    Cancelled = 6,
    Rejected = 7,
    Expired = 8,
    Failed = 9
}
