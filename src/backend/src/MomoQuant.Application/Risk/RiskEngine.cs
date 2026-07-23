using System.Text.Json;
using MomoQuant.Application.Risk.Evaluators;
using MomoQuant.Application.Risk.Models;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Risk;

namespace MomoQuant.Application.Risk;

public sealed class RiskEngine : IRiskEngine
{
    private readonly IReadOnlyList<IRiskRuleEvaluator> _evaluators;
    private readonly PositionSizingService _positionSizingService;

    public RiskEngine(PositionSizingService positionSizingService)
        : this(positionSizingService, CreateDefaultEvaluators())
    {
    }

    internal RiskEngine(PositionSizingService positionSizingService, IEnumerable<IRiskRuleEvaluator> evaluators)
    {
        _positionSizingService = positionSizingService;
        _evaluators = evaluators.ToList();
    }

    public RiskEvaluationResult Evaluate(RiskContext context)
    {
        var rules = RiskRuleSet.FromRules(context.Rules);

        foreach (var evaluator in _evaluators)
        {
            var rejection = evaluator.Evaluate(context, rules);
            if (rejection is not null)
            {
                return rejection;
            }
        }

        var sizing = _positionSizingService.Calculate(context, rules);
        if (!sizing.Succeeded)
        {
            return RiskEvaluationResult.Reject(
                RiskRuleKeys.MaxRiskPerTradePercent,
                sizing.ErrorMessage ?? "Position sizing failed.");
        }

        return new RiskEvaluationResult
        {
            Decision = RiskDecisionType.Approved,
            Reason = "Trade approved. All configured risk rules passed.",
            ApprovedRiskPercent = sizing.ApprovedRiskPercent,
            PositionSize = sizing.PositionSize,
            StopLoss = sizing.StopLoss,
            TakeProfit = sizing.TakeProfit,
            RiskAmount = sizing.RiskAmount,
            RawDataJson = JsonSerializer.Serialize(new
            {
                context.SymbolId,
                context.Direction,
                context.EntryPrice,
                context.ConfidenceScore,
                ApprovedRiskPercent = sizing.ApprovedRiskPercent,
                PositionSize = sizing.PositionSize,
                RiskAmount = sizing.RiskAmount
            })
        };
    }

    private static IEnumerable<IRiskRuleEvaluator> CreateDefaultEvaluators() =>
    [
        new EmergencyStopEvaluator(),
        new RequireStopLossEvaluator(),
        new MinConfidenceEvaluator(),
        new MaxDailyLossEvaluator(),
        new MaxWeeklyLossEvaluator(),
        new MaxOpenPositionsEvaluator(),
        new ExposureEvaluator(),
        new ConsecutiveLossEvaluator(),
        new SpreadEvaluator(),
        new VolatilityEvaluator(),
        new RewardRiskEvaluator(),
        new MaxRiskPerTradeEvaluator()
    ];
}
