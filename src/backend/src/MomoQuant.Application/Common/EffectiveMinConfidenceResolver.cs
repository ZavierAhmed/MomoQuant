namespace MomoQuant.Application.Common;

public static class EffectiveMinConfidenceResolver
{
    public static decimal Resolve(decimal sessionMinConfidenceScore, decimal riskProfileMinConfidenceScore)
    {
        if (sessionMinConfidenceScore <= 0m)
        {
            return riskProfileMinConfidenceScore;
        }

        return Math.Max(sessionMinConfidenceScore, riskProfileMinConfidenceScore);
    }
}
