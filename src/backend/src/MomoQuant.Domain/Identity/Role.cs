namespace MomoQuant.Domain.Identity;

using MomoQuant.Domain.Common;
using MomoQuant.Domain.Enums;

public class Role : AuditableEntity
{
    public UserRole Name { get; set; }
    public string Description { get; set; } = string.Empty;
}
