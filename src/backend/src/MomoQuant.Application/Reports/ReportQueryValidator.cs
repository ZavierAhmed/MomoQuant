using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Common;
using MomoQuant.Application.MarketData;
using MomoQuant.Application.Reports.Dtos;
using MomoQuant.Domain.Enums;

namespace MomoQuant.Application.Reports;

public sealed class ReportQueryFilter
{
    public DateTime FromUtc { get; init; }
    public DateTime ToUtc { get; init; }
    public long? SymbolId { get; init; }
    public long? StrategyId { get; init; }
    public Timeframe? Timeframe { get; init; }
    public TradingMode? Mode { get; init; }
    public MarketRegime? MarketRegime { get; init; }
    public int Limit { get; init; } = 100;
    public long? TradingSessionId { get; init; }
}

public interface IReportQueryValidator
{
    Task<ServiceResult<ReportQueryFilter>> ValidateAsync(ReportQuery query, CancellationToken cancellationToken = default);
}

public sealed class ReportQueryValidator : IReportQueryValidator
{
    private static readonly TimeSpan DefaultRange = TimeSpan.FromDays(90);

    private readonly ISymbolRepository _symbolRepository;
    private readonly IStrategyRepository _strategyRepository;

    public ReportQueryValidator(ISymbolRepository symbolRepository, IStrategyRepository strategyRepository)
    {
        _symbolRepository = symbolRepository;
        _strategyRepository = strategyRepository;
    }

    public async Task<ServiceResult<ReportQueryFilter>> ValidateAsync(
        ReportQuery query,
        CancellationToken cancellationToken = default)
    {
        var toUtc = query.ToUtc ?? DateTime.UtcNow;
        var fromUtc = query.FromUtc ?? toUtc.Subtract(DefaultRange);

        if (fromUtc >= toUtc)
        {
            return ServiceResult<ReportQueryFilter>.Fail("FromUtc must be before ToUtc.", "fromUtc");
        }

        TradingMode? mode = null;
        if (!string.IsNullOrWhiteSpace(query.Mode))
        {
            if (!Enum.TryParse<TradingMode>(query.Mode, ignoreCase: true, out var parsedMode))
            {
                return ServiceResult<ReportQueryFilter>.Fail("Mode is invalid.", "mode");
            }

            if (parsedMode == TradingMode.Live)
            {
                return ServiceResult<ReportQueryFilter>.Fail("Live mode reporting is not implemented yet.", "mode");
            }

            mode = parsedMode;
        }

        Timeframe? timeframe = null;
        if (!string.IsNullOrWhiteSpace(query.Timeframe))
        {
            if (!TimeframeParser.TryParse(query.Timeframe, out var parsedTimeframe))
            {
                return ServiceResult<ReportQueryFilter>.Fail("Timeframe is invalid.", "timeframe");
            }

            timeframe = parsedTimeframe;
        }

        MarketRegime? marketRegime = null;
        if (!string.IsNullOrWhiteSpace(query.MarketRegime))
        {
            if (!Enum.TryParse<MarketRegime>(query.MarketRegime, ignoreCase: true, out var parsedRegime))
            {
                return ServiceResult<ReportQueryFilter>.Fail("Market regime is invalid.", "marketRegime");
            }

            marketRegime = parsedRegime;
        }

        if (query.SymbolId.HasValue)
        {
            var symbol = await _symbolRepository.GetByIdAsync(query.SymbolId.Value, cancellationToken);
            if (symbol is null)
            {
                return ServiceResult<ReportQueryFilter>.Fail("Symbol was not found.", "symbolId");
            }
        }

        if (query.StrategyId.HasValue)
        {
            var strategy = await _strategyRepository.GetByIdAsync(query.StrategyId.Value, cancellationToken);
            if (strategy is null)
            {
                return ServiceResult<ReportQueryFilter>.Fail("Strategy was not found.", "strategyId");
            }
        }

        return ServiceResult<ReportQueryFilter>.Ok(new ReportQueryFilter
        {
            FromUtc = fromUtc,
            ToUtc = toUtc,
            SymbolId = query.SymbolId,
            StrategyId = query.StrategyId,
            Timeframe = timeframe,
            Mode = mode,
            MarketRegime = marketRegime,
            Limit = Math.Clamp(query.Limit, 1, 500)
        });
    }
}
