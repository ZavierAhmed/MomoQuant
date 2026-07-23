using MomoQuant.Application.TradingSystems.Dtos;

namespace MomoQuant.Application.TradingSystems;

/// <summary>
/// Plain-language explanations for the terms used in the SK analyzer. Shown in the
/// "Explain these terms" section so beginners can follow the analysis.
/// </summary>
public static class SkGlossary
{
    public static IReadOnlyList<SkGlossaryTermDto> Terms { get; } =
    [
        new() { Term = "Reaction zone", Explanation = "An area where price may turn or pause." },
        new() { Term = "Strong reaction zone", Explanation = "A more important part of the reaction zone based on Fibonacci levels." },
        new() { Term = "Danger level", Explanation = "If price crosses this level, the current idea is probably wrong." },
        new() { Term = "First target", Explanation = "The first area price may try to reach if the setup works." },
        new() { Term = "Second target", Explanation = "A further target if momentum continues." },
        new() { Term = "Starting point", Explanation = "Where the measured move begins." },
        new() { Term = "Pullback point", Explanation = "Where price corrects before possibly continuing." },
        new() { Term = "Higher timeframe", Explanation = "The bigger chart view used to avoid analyzing only a small piece of the market." }
    ];
}
