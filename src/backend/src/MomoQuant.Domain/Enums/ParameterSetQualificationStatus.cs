namespace MomoQuant.Domain.Enums;

/// <summary>
/// Describes the durable deployment-qualification state of a strategy parameter set.
/// This state is independent from human approval for controlled research use.
/// </summary>
public enum ParameterSetQualificationStatus
{
    HistoricalNotEvaluated = 0,
    ResearchOnly = 1,
    DeploymentQualified = 2
}
