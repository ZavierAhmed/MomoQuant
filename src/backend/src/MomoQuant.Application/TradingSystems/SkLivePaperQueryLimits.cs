namespace MomoQuant.Application.TradingSystems;

/// <summary>
/// Bounded collection-query contracts for SK LivePaper simulation endpoints.
/// </summary>
public static class SkLivePaperQueryLimits
{
    public const int SessionsDefault = 50;
    public const int SessionsMaximum = 200;
    public const int CandidatesDefault = 100;
    public const int CandidatesMaximum = 500;
    public const int EventsDefault = 200;
    public const int EventsMaximum = 1000;

    public static int NormalizeSessions(int requested) => Normalize(requested, SessionsDefault, SessionsMaximum);

    public static int NormalizeCandidates(int requested) => Normalize(requested, CandidatesDefault, CandidatesMaximum);

    public static int NormalizeEvents(int requested) => Normalize(requested, EventsDefault, EventsMaximum);

    private static int Normalize(int requested, int fallback, int maximum) =>
        requested <= 0 ? fallback : Math.Min(requested, maximum);
}
