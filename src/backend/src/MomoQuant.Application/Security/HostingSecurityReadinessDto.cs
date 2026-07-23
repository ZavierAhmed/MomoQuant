namespace MomoQuant.Application.Security;

/// <summary>
/// Development-facing hosting security gate. Remains Blocked until cookie/CSRF,
/// revocation, and lockout/rate-limit work lands in a future milestone.
/// </summary>
public sealed class HostingSecurityReadinessDto
{
    public const string StatusBlocked = "Blocked";

    public string Status { get; init; } = StatusBlocked;

    public IReadOnlyList<string> Reasons { get; init; } = DefaultReasons;

    public string Summary { get; init; } =
        "Hosting security is Blocked for production internet exposure until auth transport and revocation gaps are closed.";

    public static HostingSecurityReadinessDto CreateBlocked() => new();

    public static readonly IReadOnlyList<string> DefaultReasons =
    [
        "JWT is stored in browser localStorage (XSS exposure risk).",
        "Full server-side token revocation is not implemented.",
        "Cookie-based auth and CSRF protection are not implemented.",
        "Account lockout and authentication rate-limiting are incomplete."
    ];
}
