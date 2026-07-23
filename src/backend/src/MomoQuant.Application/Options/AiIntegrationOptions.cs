namespace MomoQuant.Application.Options;

public sealed class AiIntegrationOptions
{
    public const string SectionName = "AiService";

    public string BaseUrl { get; set; } = "http://127.0.0.1:8001";

    public int TimeoutSeconds { get; set; } = 10;

    public bool EnableFallback { get; set; } = true;
}
