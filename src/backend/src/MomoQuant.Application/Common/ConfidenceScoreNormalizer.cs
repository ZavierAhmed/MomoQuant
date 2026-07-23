using System.Globalization;

namespace MomoQuant.Application.Common;

public static class ConfidenceScoreNormalizer
{
    public static decimal Normalize(decimal? value)
    {
        if (!value.HasValue)
        {
            return 0m;
        }

        var score = value.Value;
        if (score < 0m)
        {
            return 0m;
        }

        if (score <= 1m)
        {
            return Math.Clamp(score * 100m, 0m, 100m);
        }

        return Math.Clamp(score, 0m, 100m);
    }

    public static string Format(decimal value) =>
        Normalize(value).ToString("0.00", CultureInfo.InvariantCulture);
}
