using System.Globalization;

namespace MomoQuant.Application.TradingSystems;

/// <summary>
/// Symbol/price-aware formatting so the UI shows clean numbers such as
/// "63,415.70" instead of "63415.8362". Analysis only — never produces order sizes.
/// </summary>
public static class SkPriceFormatter
{
    /// <summary>
    /// Chooses a sensible number of decimals for a given price magnitude. Larger prices
    /// (BTC/ETH) use 2 decimals; small-cap prices use progressively more decimals.
    /// </summary>
    public static int ResolveDecimals(decimal price)
    {
        var abs = Math.Abs(price);
        if (abs >= 100m)
        {
            return 2;
        }

        if (abs >= 1m)
        {
            return 4;
        }

        if (abs >= 0.01m)
        {
            return 6;
        }

        return 8;
    }

    public static int ResolveDecimals(string? symbol, decimal referencePrice) => ResolveDecimals(referencePrice);

    public static string Format(decimal price, int decimals)
    {
        var safeDecimals = Math.Clamp(decimals, 0, 8);
        return price.ToString("N" + safeDecimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
    }

    public static string Format(decimal price, string? symbol) => Format(price, ResolveDecimals(price));

    public static string Range(decimal low, decimal high, int decimals) =>
        $"{Format(low, decimals)} – {Format(high, decimals)}";
}
