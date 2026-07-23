namespace MomoQuant.Domain.Enums;

public enum ParameterSetTestStatus
{
    TrainingFailed = 0,
    TrainingPassed = 1,
    ValidationPassed = 2,
    ValidationFailed = 3,
    Overfit = 4,
    TooFewTrades = 5,
    TooHighDrawdown = 6,
    NoTrades = 7,
    EngineError = 8
}
