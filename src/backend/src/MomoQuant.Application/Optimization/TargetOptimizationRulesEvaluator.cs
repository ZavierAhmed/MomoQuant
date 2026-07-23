using MomoQuant.Application.Optimization.Dtos;
using MomoQuant.Application.Validation.Dtos;
using MomoQuant.Domain.Enums;

namespace MomoQuant.Application.Optimization;

public interface ITargetOptimizationRulesEvaluator
{
    (bool Passed, IReadOnlyList<string> FailReasons, TargetPassSummary Summary)
        EvaluateTraining(StrategyPerformanceMetricsDto metrics, TargetOptimizationRulesDto rules);

    (bool Passed, ParameterSetTestStatus Status, IReadOnlyList<string> FailReasons,
        IReadOnlyList<string> OverfitWarnings, TargetPassSummary Summary, decimal RobustnessScore)
        EvaluateValidation(
            StrategyPerformanceMetricsDto training,
            StrategyPerformanceMetricsDto validation,
            TargetOptimizationRulesDto rules);

    decimal CalculateRobustnessScore(
        StrategyPerformanceMetricsDto training,
        StrategyPerformanceMetricsDto validation);
}

public sealed class TargetOptimizationRulesEvaluator : ITargetOptimizationRulesEvaluator
{
    public (bool Passed, IReadOnlyList<string> FailReasons, TargetPassSummary Summary)
        EvaluateTraining(StrategyPerformanceMetricsDto metrics, TargetOptimizationRulesDto rules)
    {
        var failReasons = new List<string>();
        var summary = new TargetPassSummary
        {
            TrainingPnlPassed = metrics.NetPnlPercent >= rules.MinTrainingNetPnlPercent,
            TrainingProfitFactorPassed = metrics.ProfitFactor >= rules.MinTrainingProfitFactor,
            TrainingDrawdownPassed = metrics.MaxDrawdownPercent <= rules.MaxTrainingDrawdownPercent,
            TrainingTradesPassed = metrics.TradeCount >= rules.MinTrainingTrades
        };

        if (metrics.TradeCount == 0)
        {
            failReasons.Add("Training produced no trades.");
        }
        else if (!summary.TrainingTradesPassed)
        {
            failReasons.Add($"Training trade count ({metrics.TradeCount}) is below minimum ({rules.MinTrainingTrades}).");
        }

        if (!summary.TrainingPnlPassed)
        {
            failReasons.Add($"Training net PnL ({metrics.NetPnlPercent:F2}%) is below minimum ({rules.MinTrainingNetPnlPercent}%).");
        }

        if (!summary.TrainingProfitFactorPassed)
        {
            failReasons.Add($"Training profit factor ({metrics.ProfitFactor:F2}) is below minimum ({rules.MinTrainingProfitFactor}).");
        }

        if (!summary.TrainingDrawdownPassed)
        {
            failReasons.Add($"Training max drawdown ({metrics.MaxDrawdownPercent:F2}%) exceeds limit ({rules.MaxTrainingDrawdownPercent}%).");
        }

        return (summary.TrainingPassed, failReasons, summary);
    }

    public (bool Passed, ParameterSetTestStatus Status, IReadOnlyList<string> FailReasons,
        IReadOnlyList<string> OverfitWarnings, TargetPassSummary Summary, decimal RobustnessScore)
        EvaluateValidation(
            StrategyPerformanceMetricsDto training,
            StrategyPerformanceMetricsDto validation,
            TargetOptimizationRulesDto rules)
    {
        var (_, trainingFailReasons, trainingSummary) = EvaluateTraining(training, rules);
        var failReasons = new List<string>(trainingFailReasons);
        var overfitWarnings = DetectOverfitWarnings(training, validation, rules);
        var robustness = CalculateRobustnessScore(training, validation);

        var summary = trainingSummary with
        {
            ValidationPnlPassed = validation.NetPnlPercent >= rules.MinValidationNetPnlPercent,
            ValidationProfitFactorPassed = validation.ProfitFactor >= rules.MinValidationProfitFactor,
            ValidationDrawdownPassed = validation.MaxDrawdownPercent <= rules.MaxValidationDrawdownPercent,
            ValidationTradesPassed = validation.TradeCount >= rules.MinValidationTrades,
            RobustnessPassed = robustness >= rules.MinRobustnessScore
        };

        if (validation.TradeCount == 0)
        {
            failReasons.Add("Validation produced no trades.");
        }
        else if (!summary.ValidationTradesPassed)
        {
            failReasons.Add($"Validation trade count ({validation.TradeCount}) is below minimum ({rules.MinValidationTrades}).");
        }

        if (!summary.ValidationPnlPassed)
        {
            failReasons.Add($"Validation net PnL ({validation.NetPnlPercent:F2}%) is below minimum ({rules.MinValidationNetPnlPercent}%).");
        }

        if (!summary.ValidationProfitFactorPassed)
        {
            failReasons.Add($"Validation profit factor ({validation.ProfitFactor:F2}) is below minimum ({rules.MinValidationProfitFactor}).");
        }

        if (!summary.ValidationDrawdownPassed)
        {
            failReasons.Add($"Validation max drawdown ({validation.MaxDrawdownPercent:F2}%) exceeds limit ({rules.MaxValidationDrawdownPercent}%).");
        }

        if (!summary.RobustnessPassed)
        {
            failReasons.Add($"Robustness score ({robustness:F2}) is below minimum ({rules.MinRobustnessScore}).");
        }

        var status = DetermineStatus(training, validation, summary, overfitWarnings, failReasons);
        var passed = status == ParameterSetTestStatus.ValidationPassed;

        return (passed, status, failReasons, overfitWarnings, summary, robustness);
    }

    public decimal CalculateRobustnessScore(
        StrategyPerformanceMetricsDto training,
        StrategyPerformanceMetricsDto validation)
    {
        var pfRatio = training.ProfitFactor > 0m
            ? Math.Min(1m, validation.ProfitFactor / training.ProfitFactor)
            : validation.ProfitFactor >= 1.1m ? 1m : 0m;
        var wrRatio = training.WinRate > 0m
            ? Math.Min(1m, validation.WinRate / training.WinRate)
            : validation.WinRate > 50m ? 1m : 0m;
        var pnlRatio = training.NetPnlPercent > 0m
            ? Math.Min(1m, Math.Max(0m, validation.NetPnlPercent / training.NetPnlPercent))
            : validation.NetPnlPercent > 0m ? 1m : 0m;
        var ddOk = validation.MaxDrawdownPercent <= 8m ? 1m : Math.Max(0m, 1m - (validation.MaxDrawdownPercent - 8m) / 50m);
        var tradeOk = training.TradeCount > 0
            ? Math.Min(1m, (decimal)validation.TradeCount / training.TradeCount)
            : validation.TradeCount >= 10 ? 1m : 0m;

        return Math.Round((pfRatio * 25m + wrRatio * 20m + pnlRatio * 25m + ddOk * 15m + tradeOk * 15m), 2);
    }

    private static ParameterSetTestStatus DetermineStatus(
        StrategyPerformanceMetricsDto training,
        StrategyPerformanceMetricsDto validation,
        TargetPassSummary summary,
        IReadOnlyList<string> overfitWarnings,
        IReadOnlyList<string> failReasons)
    {
        if (training.TradeCount == 0 && validation.TradeCount == 0)
        {
            return ParameterSetTestStatus.NoTrades;
        }

        if (!summary.TrainingPassed)
        {
            if (training.TradeCount == 0) return ParameterSetTestStatus.NoTrades;
            if (!summary.TrainingTradesPassed) return ParameterSetTestStatus.TooFewTrades;
            if (!summary.TrainingDrawdownPassed) return ParameterSetTestStatus.TooHighDrawdown;
            return ParameterSetTestStatus.TrainingFailed;
        }

        if (validation.TradeCount == 0) return ParameterSetTestStatus.NoTrades;
        if (!summary.ValidationTradesPassed) return ParameterSetTestStatus.TooFewTrades;
        if (!summary.ValidationDrawdownPassed) return ParameterSetTestStatus.TooHighDrawdown;

        if (summary.ValidationPassed)
        {
            return ParameterSetTestStatus.ValidationPassed;
        }

        if (overfitWarnings.Count > 0)
        {
            return ParameterSetTestStatus.Overfit;
        }

        return ParameterSetTestStatus.ValidationFailed;
    }

    private static List<string> DetectOverfitWarnings(
        StrategyPerformanceMetricsDto training,
        StrategyPerformanceMetricsDto validation,
        TargetOptimizationRulesDto rules)
    {
        var warnings = new List<string>();

        if (training.NetPnlPercent > 0m && validation.NetPnlPercent < 0m)
        {
            warnings.Add("Training PnL was positive but validation PnL is negative.");
        }

        if (training.ProfitFactor >= 1.5m && validation.ProfitFactor < rules.MinValidationProfitFactor)
        {
            warnings.Add("Training profit factor was strong but validation profit factor is weak.");
        }

        if (training.NetPnlPercent > 0m)
        {
            var pnlDrop = (training.NetPnlPercent - validation.NetPnlPercent) / training.NetPnlPercent * 100m;
            if (pnlDrop > rules.MaxValidationPnLDropPercent)
            {
                warnings.Add($"Validation PnL dropped {pnlDrop:F0}% from training (limit {rules.MaxValidationPnLDropPercent}%).");
            }
        }

        if (training.ProfitFactor > 0m)
        {
            var pfDrop = (training.ProfitFactor - validation.ProfitFactor) / training.ProfitFactor * 100m;
            if (pfDrop > rules.MaxValidationProfitFactorDropPercent)
            {
                warnings.Add($"Validation profit factor dropped {pfDrop:F0}% from training (limit {rules.MaxValidationProfitFactorDropPercent}%).");
            }
        }

        if (training.TradeCount >= rules.MinTrainingTrades && validation.TradeCount < rules.MinValidationTrades / 2)
        {
            warnings.Add("Training had many trades but validation has almost none.");
        }

        if (warnings.Count > 0)
        {
            warnings.Add("Training performance was strong, but validation failed. This parameter set may be overfit to the training period.");
        }

        return warnings;
    }
}
