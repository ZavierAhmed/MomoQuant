using MomoQuant.Application.Strategies.PriceStructure;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.MarketData;

namespace MomoQuant.Application.StrategyLab.Synthetic;

public interface ISyntheticCandleScenario
{
    string Name { get; }
    string Description { get; }
    string StrategyCode { get; }
    IReadOnlyList<Candle> BuildCandles();
    int ExpectedCandidateCount { get; }
    TradeDirection? ExpectedDirection { get; }
    string? ExpectedNoTradeReason { get; }
}

public sealed class SyntheticScenarioResult
{
    public required ISyntheticCandleScenario Scenario { get; init; }
    public bool Passed { get; init; }
    public int ActualCandidateCount { get; init; }
    public TradeDirection? ActualDirection { get; init; }
    public string? ActualReason { get; init; }
    public string? FailureDetails { get; init; }
}

public sealed class SyntheticCandleScenarioRunner
{
    public IReadOnlyList<SyntheticScenarioResult> RunAll(IEnumerable<ISyntheticCandleScenario> scenarios)
    {
        return scenarios.Select(Run).ToList();
    }

    public SyntheticScenarioResult Run(ISyntheticCandleScenario scenario)
    {
        var candles = scenario.BuildCandles();
        var parameters = new Dictionary<string, string>();
        var detector = PriceStructureDetectorFactory.Create(scenario.StrategyCode);
        if (detector is null)
        {
            return new SyntheticScenarioResult
            {
                Scenario = scenario,
                Passed = false,
                FailureDetails = $"No shared detector registered for {scenario.StrategyCode}."
            };
        }

        detector.Initialize(parameters);
        var candidates = new List<(TradeDirection Direction, string Reason)>();

        for (var i = 10; i < candles.Count; i++)
        {
            var slice = candles.Take(i + 1).ToList();
            var result = detector.ProcessCandle(slice, scenario.StrategyCode, 1, "15m");
            if (result.Candidate is not null)
            {
                candidates.Add((result.Candidate.Direction, result.Candidate.Reason));
            }
        }

        var actualCount = candidates.Count;
        var actualDirection = candidates.FirstOrDefault().Direction;
        var passed = actualCount == scenario.ExpectedCandidateCount;
        if (scenario.ExpectedDirection.HasValue && candidates.Count > 0)
        {
            passed = passed && candidates.All(c => c.Direction == scenario.ExpectedDirection);
        }

        string? failure = null;
        if (!passed)
        {
            failure = $"Expected {scenario.ExpectedCandidateCount} candidate(s)";
            if (scenario.ExpectedDirection.HasValue)
            {
                failure += $" {scenario.ExpectedDirection}, got {actualCount}";
                if (candidates.Count > 0)
                {
                    failure += $" {actualDirection}";
                }
            }
        }

        return new SyntheticScenarioResult
        {
            Scenario = scenario,
            Passed = passed,
            ActualCandidateCount = actualCount,
            ActualDirection = candidates.Count > 0 ? actualDirection : null,
            FailureDetails = failure
        };
    }
}
