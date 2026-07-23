namespace MomoQuant.Domain.Risk;

using MomoQuant.Domain.Common;

public class RiskProfile : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
}
