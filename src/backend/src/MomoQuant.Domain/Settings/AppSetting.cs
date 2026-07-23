namespace MomoQuant.Domain.Settings;

using MomoQuant.Domain.Common;
using MomoQuant.Domain.Enums;

public class AppSetting : AuditableEntity
{
    public string SettingKey { get; set; } = string.Empty;
    public string SettingValue { get; set; } = string.Empty;
    public SettingValueType ValueType { get; set; }
    public string Category { get; set; } = string.Empty;
    public bool IsSensitive { get; set; }
}
