using MomoQuant.Application.Ai.Dtos;
using MomoQuant.Application.Common;

namespace MomoQuant.Application.Trading;

public sealed class AiScoringInput
{
    public bool Enabled { get; init; }
    public bool StrictRequired { get; init; }
    public bool Succeeded { get; init; }
    public bool Valid { get; init; }
    public decimal? Score { get; init; }
    public string? Warning { get; init; }

    public static AiScoringInput Disabled() => new() { Enabled = false };
}

public sealed class ConfidenceResolution
{
    public decimal StrategyConfidence { get; init; }
    public decimal? AiConfidence { get; init; }
    public decimal CombinedConfidence { get; init; }
    public decimal RawCombinedScore { get; init; }
    public bool AiEnabled { get; init; }
    public bool AiSucceeded { get; init; }
    public bool AiFallbackUsed { get; init; }
    public string? AiWarning { get; init; }
    public string Source { get; init; } = "Strategy";
}

public static class AiScoringHelper
{
    public const string UnavailableWarning = "AI service unavailable. AI scoring skipped.";
    public const string InvalidWarning = "AI response invalid. Strategy confidence used.";

    public static AiScoringInput FromConfidenceResult(
        bool useAi,
        bool strictAiRequired,
        ServiceResult<ScoreConfidenceResponseDto> result)
    {
        if (!useAi)
        {
            return AiScoringInput.Disabled();
        }

        if (!result.Succeeded || result.Data is null || result.Data.UsedFallback)
        {
            return new AiScoringInput
            {
                Enabled = true,
                StrictRequired = strictAiRequired,
                Succeeded = false,
                Valid = false,
                Warning = UnavailableWarning
            };
        }

        if (result.Data.ConfidenceScore < 0)
        {
            return new AiScoringInput
            {
                Enabled = true,
                StrictRequired = strictAiRequired,
                Succeeded = true,
                Valid = false,
                Warning = InvalidWarning
            };
        }

        return new AiScoringInput
        {
            Enabled = true,
            StrictRequired = strictAiRequired,
            Succeeded = true,
            Valid = true,
            Score = result.Data.ConfidenceScore
        };
    }
}
