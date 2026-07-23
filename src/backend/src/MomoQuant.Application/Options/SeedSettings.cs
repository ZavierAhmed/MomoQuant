namespace MomoQuant.Application.Options;

public sealed class SeedSettings
{
    public const string SectionName = "Seed";

    public string AdminEmail { get; set; } = "admin@momoquant.local";
    public string AdminPassword { get; set; } = string.Empty;
    public string AdminFullName { get; set; } = "Development Admin";
}
