using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Risk;

namespace MomoQuant.Application.Risk.Evaluators;

internal interface IRiskRuleEvaluator
{
    string RuleKey { get; }

    RiskEvaluationResult? Evaluate(RiskContext context, Models.RiskRuleSet rules);
}
