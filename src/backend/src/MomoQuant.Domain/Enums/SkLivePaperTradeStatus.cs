namespace MomoQuant.Domain.Enums;

public enum SkLivePaperTradeStatus
{
    Open = 0,
    Closed = 1,
    Cancelled = 2
}

public enum SkLivePaperTradeExitReason
{
    StopLoss = 0,
    Target1 = 1,
    Target2 = 2,
    Invalidation = 3,
    ManualClose = 4,
    SessionStopped = 5
}
