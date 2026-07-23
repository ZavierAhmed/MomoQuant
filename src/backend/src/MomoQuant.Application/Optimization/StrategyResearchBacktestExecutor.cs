using MomoQuant.Application.Backtesting;
using MomoQuant.Application.Validation.Dtos;

namespace MomoQuant.Application.Optimization;

public sealed class StrategyResearchBacktestExecutor : IStrategyResearchBacktestExecutor
{
    private readonly IStrategyBacktestSliceRunner _sliceRunner;

    public StrategyResearchBacktestExecutor(IStrategyBacktestSliceRunner sliceRunner)
    {
        _sliceRunner = sliceRunner;
    }

    public Task<StrategyResearchBacktestResult?> RunWindowAsync(
        long exchangeId,
        long symbolId,
        string timeframe,
        DateTime fromUtc,
        DateTime toUtc,
        string strategyCode,
        IReadOnlyDictionary<string, string> parameters,
        long riskProfileId,
        decimal initialBalance,
        StrategyResearchExecutionOptions? options = null,
        CancellationToken cancellationToken = default) =>
        _sliceRunner.RunSliceAsync(new StrategyBacktestSliceRequest
        {
            ExchangeId = exchangeId,
            SymbolId = symbolId,
            Timeframe = timeframe,
            EvaluationFromUtc = fromUtc,
            EvaluationToUtc = toUtc,
            StrategyCode = strategyCode,
            Parameters = parameters,
            RiskProfileId = riskProfileId,
            InitialBalance = initialBalance,
            Options = options
        }, cancellationToken);
}
