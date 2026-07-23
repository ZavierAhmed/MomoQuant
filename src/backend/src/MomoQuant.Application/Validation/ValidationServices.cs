using MomoQuant.Application.Validation.Dtos;
using MomoQuant.Domain.Enums;

namespace MomoQuant.Application.Validation;

public interface IValidationDateSplitService
{
    ValidationSplitDto Split(DateTime fromUtc, DateTime toUtc, decimal trainingPercent = 70m);
    bool HasEnoughCandles(int totalCandles, int warmupCandles, decimal trainingPercent = 70m, int minTraining = 100, int minValidation = 50);
}

public sealed class ValidationDateSplitService : IValidationDateSplitService
{
    public ValidationSplitDto Split(DateTime fromUtc, DateTime toUtc, decimal trainingPercent = 70m)
    {
        if (toUtc <= fromUtc)
        {
            throw new ArgumentException("End date must be after start date.");
        }

        var total = toUtc - fromUtc;
        var trainingEnd = fromUtc.AddTicks((long)(total.Ticks * (trainingPercent / 100m)));
        var validationPercent = 100m - trainingPercent;

        return new ValidationSplitDto
        {
            SplitMethod = "TimeRatio",
            TrainingPercent = trainingPercent,
            ValidationPercent = validationPercent,
            FullDateRange = new DateRangeDto { FromUtc = fromUtc, ToUtc = toUtc },
            TrainingRange = new DateRangeDto { FromUtc = fromUtc, ToUtc = trainingEnd },
            ValidationRange = new DateRangeDto { FromUtc = trainingEnd, ToUtc = toUtc }
        };
    }

    public bool HasEnoughCandles(int totalCandles, int warmupCandles, decimal trainingPercent = 70m, int minTraining = 100, int minValidation = 50)
    {
        var evaluable = Math.Max(0, totalCandles - warmupCandles);
        var trainingCount = (int)Math.Floor(evaluable * (trainingPercent / 100m));
        var validationCount = evaluable - trainingCount;
        return trainingCount >= minTraining && validationCount >= minValidation;
    }
}

public interface IStrategyValidationEvaluator
{
    (ValidationStatus Status, IReadOnlyList<string> FailReasons, IReadOnlyList<string> Warnings, decimal RobustnessScore)
        Evaluate(
            StrategyPerformanceMetricsDto training,
            StrategyPerformanceMetricsDto validation,
            decimal maxDrawdownPercent = 25m);
}

public sealed class StrategyValidationEvaluator : IStrategyValidationEvaluator
{
    public (ValidationStatus Status, IReadOnlyList<string> FailReasons, IReadOnlyList<string> Warnings, decimal RobustnessScore)
        Evaluate(
            StrategyPerformanceMetricsDto training,
            StrategyPerformanceMetricsDto validation,
            decimal maxDrawdownPercent = 25m)
    {
        var failReasons = new List<string>();
        var warnings = new List<string>();

        if (validation.TradeCount < 10)
        {
            failReasons.Add("Validation trade count is below minimum (10).");
        }
        else if (validation.TradeCount < 20)
        {
            warnings.Add("Validation trade count is low; results may be unreliable.");
        }

        if (validation.NetPnlPercent <= 0m)
        {
            failReasons.Add("Validation net PnL is not positive.");
        }

        if (validation.ProfitFactor < 1.2m)
        {
            failReasons.Add("Validation profit factor is below 1.2.");
        }

        if (validation.MaxDrawdownPercent > maxDrawdownPercent)
        {
            failReasons.Add($"Validation max drawdown exceeds configured limit ({maxDrawdownPercent}%).");
        }

        if (training.WinRate - validation.WinRate > 20m)
        {
            failReasons.Add("Validation win rate dropped more than 20 percentage points vs training.");
        }

        if (training.ProfitFactor > 0m && validation.ProfitFactor < training.ProfitFactor * 0.6m)
        {
            failReasons.Add("Validation profit factor is more than 40% below training.");
        }

        if (training.NetPnlPercent > 0m && validation.NetPnlPercent <= 0m)
        {
            warnings.Add("Training was profitable but validation was not — possible overfitting.");
        }

        var robustness = CalculateRobustnessScore(training, validation);

        if (failReasons.Count > 0)
        {
            return (ValidationStatus.Failed, failReasons, warnings, robustness);
        }

        if (warnings.Count > 0)
        {
            return (ValidationStatus.Warning, failReasons, warnings, robustness);
        }

        return (ValidationStatus.Passed, failReasons, warnings, robustness);
    }

    private static decimal CalculateRobustnessScore(
        StrategyPerformanceMetricsDto training,
        StrategyPerformanceMetricsDto validation)
    {
        var pfRatio = training.ProfitFactor > 0m
            ? Math.Min(1m, validation.ProfitFactor / training.ProfitFactor)
            : validation.ProfitFactor >= 1.2m ? 1m : 0m;
        var wrRatio = training.WinRate > 0m
            ? Math.Min(1m, validation.WinRate / training.WinRate)
            : validation.WinRate > 50m ? 1m : 0m;
        var pnlOk = validation.NetPnlPercent > 0m ? 1m : 0m;
        var ddOk = validation.MaxDrawdownPercent <= 25m ? 1m : Math.Max(0m, 1m - (validation.MaxDrawdownPercent - 25m) / 50m);
        return Math.Round((pfRatio * 30m + wrRatio * 25m + pnlOk * 25m + ddOk * 20m), 2);
    }
}
