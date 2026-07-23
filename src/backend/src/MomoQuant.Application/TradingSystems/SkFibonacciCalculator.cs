namespace MomoQuant.Application.TradingSystems;

/// <summary>
/// Deterministic Fibonacci math for SK sequence analysis.
/// Levels are calculated only from detected swing points — never invented.
/// </summary>
public static class SkFibonacciCalculator
{
    /// <summary>
    /// Retracement price for an impulse leg from <paramref name="impulseStart"/> to
    /// <paramref name="impulseEnd"/>. Works for both bullish (start=low, end=high) and
    /// bearish (start=high, end=low) impulses.
    /// </summary>
    public static decimal Retracement(decimal impulseStart, decimal impulseEnd, decimal ratio) =>
        impulseEnd - (ratio * (impulseEnd - impulseStart));

    /// <summary>
    /// Extension/target price projected from a correction point in the direction of the impulse.
    /// </summary>
    public static decimal Extension(decimal impulseStart, decimal impulseEnd, decimal correctionPoint, decimal ratio) =>
        correctionPoint + (ratio * (impulseEnd - impulseStart));
}
