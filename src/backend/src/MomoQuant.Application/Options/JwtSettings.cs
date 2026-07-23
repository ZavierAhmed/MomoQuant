namespace MomoQuant.Application.Options;

public sealed class JwtSettings
{
    public const string SectionName = "Jwt";

    public string Secret { get; set; } = string.Empty;
    public string Issuer { get; set; } = "MOMOQuant";
    public string Audience { get; set; } = "MomoQuantDashboard";
    public int ExpirationMinutes { get; set; } = 60;
}
