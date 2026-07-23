namespace MomoQuant.Domain.Enums;

public enum CloseReason
{
    TakeProfit = 1,
    StopLoss = 2,
    Manual = 3,
    RiskLimit = 4,
    EmergencyStop = 5,
    SignalExit = 6,
    Timeout = 7
}
