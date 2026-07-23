namespace MomoQuant.Application.ValidationLab;

/// <summary>
/// ChronologicalHoldout/v1 — candle-count based 70/30 split (not candidate-based, not time-ratio).
/// </summary>
public static class ChronologicalHoldoutVersions
{
    public const string Current = "ChronologicalHoldout/v1";
}

public sealed class ChronologicalHoldoutSplitResult
{
    public string AlgorithmVersion { get; init; } = ChronologicalHoldoutVersions.Current;
    public decimal SplitRatio { get; init; } = 0.70m;
    public int TotalEligibleCandleCount { get; init; }
    public int TrainingCandleCount { get; init; }
    public int ValidationCandleCount { get; init; }
    public DateTime TrainingStartUtc { get; init; }
    public DateTime TrainingEndUtc { get; init; }
    public DateTime ValidationStartUtc { get; init; }
    public DateTime ValidationEndUtc { get; init; }
    public DateTime SplitCandleOpenTimeUtc { get; init; }
    public DateTime TrainingWarmupStartUtc { get; init; }
    public DateTime ValidationWarmupStartUtc { get; init; }
    public int RequiredWarmupCandles { get; init; }
    public string? FailureReason { get; init; }
    public bool IsValid => string.IsNullOrWhiteSpace(FailureReason);
}

public static class ChronologicalHoldoutSplit
{
    public static ChronologicalHoldoutSplitResult Split(
        IReadOnlyList<DateTime> eligibleCandleOpenTimesUtc,
        decimal splitRatio = 0.70m,
        int requiredWarmupCandles = 100,
        int minTrainingCandles = 10,
        int minValidationCandles = 5,
        int? timeframeMinutes = null)
    {
        if (eligibleCandleOpenTimesUtc is null || eligibleCandleOpenTimesUtc.Count == 0)
        {
            return new ChronologicalHoldoutSplitResult { FailureReason = "No eligible candles for split." };
        }

        if (splitRatio <= 0m || splitRatio >= 1m)
        {
            return new ChronologicalHoldoutSplitResult { FailureReason = "Split ratio must be between 0 and 1 exclusive." };
        }

        var ordered = eligibleCandleOpenTimesUtc
            .Select(t => DateTime.SpecifyKind(t, DateTimeKind.Utc))
            .OrderBy(t => t)
            .ToList();

        var n = ordered.Count;
        var trainingCount = (int)Math.Floor(n * (double)splitRatio);
        if (trainingCount < minTrainingCandles)
        {
            return new ChronologicalHoldoutSplitResult
            {
                TotalEligibleCandleCount = n,
                TrainingCandleCount = trainingCount,
                ValidationCandleCount = n - trainingCount,
                FailureReason = $"Insufficient training candles ({trainingCount} < {minTrainingCandles})."
            };
        }

        var validationCount = n - trainingCount;
        if (validationCount < minValidationCandles)
        {
            return new ChronologicalHoldoutSplitResult
            {
                TotalEligibleCandleCount = n,
                TrainingCandleCount = trainingCount,
                ValidationCandleCount = validationCount,
                FailureReason = $"Insufficient validation candles ({validationCount} < {minValidationCandles})."
            };
        }

        var trainingStart = ordered[0];
        var trainingEnd = ordered[trainingCount - 1];
        var validationStart = ordered[trainingCount];
        var validationEnd = ordered[^1];
        var splitOpen = validationStart;

        var trainingWarmupStart = trainingStart;
        if (requiredWarmupCandles > 0 && timeframeMinutes is > 0)
        {
            trainingWarmupStart = trainingStart.AddMinutes(-requiredWarmupCandles * timeframeMinutes.Value);
        }

        // Validation warmup may use end of training period (known at validation start).
        var validationWarmupIndex = Math.Max(0, trainingCount - requiredWarmupCandles);
        var validationWarmupStart = ordered[validationWarmupIndex];

        return new ChronologicalHoldoutSplitResult
        {
            AlgorithmVersion = ChronologicalHoldoutVersions.Current,
            SplitRatio = splitRatio,
            TotalEligibleCandleCount = n,
            TrainingCandleCount = trainingCount,
            ValidationCandleCount = validationCount,
            TrainingStartUtc = trainingStart,
            TrainingEndUtc = trainingEnd,
            ValidationStartUtc = validationStart,
            ValidationEndUtc = validationEnd,
            SplitCandleOpenTimeUtc = splitOpen,
            TrainingWarmupStartUtc = trainingWarmupStart,
            ValidationWarmupStartUtc = validationWarmupStart,
            RequiredWarmupCandles = requiredWarmupCandles
        };
    }
}