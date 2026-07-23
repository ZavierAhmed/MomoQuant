namespace MomoQuant.Domain.Exchanges;

using MomoQuant.Domain.Common;

public class Exchange : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public string WebSocketUrl { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}
