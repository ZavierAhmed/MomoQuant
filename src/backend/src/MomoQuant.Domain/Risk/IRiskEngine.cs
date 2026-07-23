namespace MomoQuant.Domain.Risk;

public interface IRiskEngine
{
    RiskEvaluationResult Evaluate(RiskContext context);
}
