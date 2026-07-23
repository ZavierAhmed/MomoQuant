namespace MomoQuant.Domain.Identity;

using MomoQuant.Domain.Common;

public class ApiKeyVault : AuditableEntity
{
    public long ExchangeId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ApiKeyEncrypted { get; set; } = string.Empty;
    public string ApiSecretEncrypted { get; set; } = string.Empty;
    public string? PassphraseEncrypted { get; set; }
    public bool IsTestnet { get; set; }
    public bool IsActive { get; set; } = true;
}
