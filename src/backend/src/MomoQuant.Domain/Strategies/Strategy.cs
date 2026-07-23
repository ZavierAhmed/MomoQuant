namespace MomoQuant.Domain.Strategies;

using MomoQuant.Domain.Common;
using MomoQuant.Domain.Enums;

public class Strategy : AuditableEntity
{
    public StrategyCode Code { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public string Version { get; set; } = "1.0.0";
    public StrategyResearchStatus ResearchStatus { get; set; } = StrategyResearchStatus.NotEvaluated;
    public bool DeploymentQualificationEligible { get; set; } = true;
    public long? CanonicalValidationExperimentId { get; set; }
    public string? ResearchDecisionJson { get; set; }
    public DateTime? ResearchDecisionAtUtc { get; set; }
}
