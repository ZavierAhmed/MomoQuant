namespace MomoQuant.Domain.Strategies;

using MomoQuant.Domain.Common;
using MomoQuant.Domain.Enums;

public class StrategyParameter : AuditableEntity
{
    public long StrategyId { get; set; }
    public string ParameterKey { get; set; } = string.Empty;
    public string ParameterValue { get; set; } = string.Empty;
    public SettingValueType ValueType { get; set; }
    public Timeframe Timeframe { get; set; }
    public long? SymbolId { get; set; }
    public bool IsActive { get; set; } = true;
}
