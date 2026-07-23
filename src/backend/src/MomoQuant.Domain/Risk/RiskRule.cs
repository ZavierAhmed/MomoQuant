namespace MomoQuant.Domain.Risk;

using MomoQuant.Domain.Common;
using MomoQuant.Domain.Enums;

public class RiskRule : AuditableEntity
{
    public long RiskProfileId { get; set; }
    public string RuleKey { get; set; } = string.Empty;
    public string RuleValue { get; set; } = string.Empty;
    public SettingValueType ValueType { get; set; }
    public bool IsEnabled { get; set; } = true;
}
