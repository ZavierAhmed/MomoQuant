using MomoQuant.Application.Ai.Dtos;
using MomoQuant.Application.Common;
using MomoQuant.Application.Strategies.Models;
using MomoQuant.Application.Trading;
using MomoQuant.Domain.Enums;

namespace MomoQuant.UnitTests.Trading;

public class AiScoringTests
{
    [Fact]
    public void ResolveConfidence_WhenAiUnavailable_UsesStrategyConfidence()
    {
        var evaluation = CreateEntryEvaluation(75m);
        var aiInput = new AiScoringInput
        {
            Enabled = true,
            StrictRequired = false,
            Succeeded = false,
            Valid = false,
            Warning = AiScoringHelper.UnavailableWarning
        };

        var resolution = TradingPipelineConfidence.ResolveConfidence(evaluation, aiInput);

        Assert.Equal(75m, resolution.CombinedConfidence);
        Assert.True(resolution.AiFallbackUsed);
        Assert.Equal("Strategy", resolution.Source);
    }

    [Fact]
    public void ResolveConfidence_WhenAiSucceeds_Combines70And30()
    {
        var evaluation = CreateEntryEvaluation(80m);
        var aiInput = new AiScoringInput
        {
            Enabled = true,
            Succeeded = true,
            Valid = true,
            Score = 60m
        };

        var resolution = TradingPipelineConfidence.ResolveConfidence(evaluation, aiInput);

        Assert.Equal(74m, resolution.CombinedConfidence);
        Assert.Equal("Combined", resolution.Source);
        Assert.False(resolution.AiFallbackUsed);
    }

    [Fact]
    public void ResolveConfidence_NormalizesFractionalAiScore()
    {
        var evaluation = CreateEntryEvaluation(70m);
        var aiInput = new AiScoringInput
        {
            Enabled = true,
            Succeeded = true,
            Valid = true,
            Score = 0.82m
        };

        var resolution = TradingPipelineConfidence.ResolveConfidence(evaluation, aiInput);

        Assert.Equal(73.6m, resolution.CombinedConfidence);
    }

    [Fact]
    public void AiScoringHelper_InvalidResponseUsesFallback()
    {
        var result = ServiceResult<ScoreConfidenceResponseDto>.Ok(new ScoreConfidenceResponseDto
        {
            ConfidenceScore = -1,
            Classification = "Invalid",
            UsedFallback = false
        });

        var input = AiScoringHelper.FromConfidenceResult(true, false, result);

        Assert.False(input.Valid);
        Assert.Equal(AiScoringHelper.InvalidWarning, input.Warning);
    }

    private static StrategyEvaluationResult CreateEntryEvaluation(decimal strength) => new()
    {
        StrategyCode = "EMA_PULLBACK",
        StrategyName = "EMA Pullback",
        Evaluated = true,
        Skipped = false,
        SignalType = SignalType.Entry,
        Direction = TradeDirection.Long,
        Strength = strength,
        ConfidenceContribution = strength,
        EntryPrice = 100m,
        Reason = "Test",
        IsValid = true
    };
}
