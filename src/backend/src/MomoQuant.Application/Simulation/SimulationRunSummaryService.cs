using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Common;
using MomoQuant.Application.Simulation.Dtos;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Simulation;
using MomoQuant.Shared.Contracts;

namespace MomoQuant.Application.Simulation;

public sealed class SimulationRunSummaryService : ISimulationRunSummaryService
{
    private readonly ISimulationRunSummaryRepository _repository;
    private readonly ILogger<SimulationRunSummaryService> _logger;

    public SimulationRunSummaryService(
        ISimulationRunSummaryRepository repository,
        ILogger<SimulationRunSummaryService> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task RecordAsync(SimulationRunSummaryInput input, CancellationToken cancellationToken = default)
    {
        try
        {
            var existing = await _repository.GetBySourceAsync(input.SourceType, input.SourceId, cancellationToken);
            var now = DateTime.UtcNow;
            var summaryText = BuildSummaryText(input);

            if (existing is null)
            {
                var summary = new SimulationRunSummary
                {
                    SourceType = input.SourceType,
                    SourceId = input.SourceId,
                    CreatedAtUtc = now
                };
                Apply(summary, input, summaryText, now);
                await _repository.AddAsync(summary, cancellationToken);
            }
            else
            {
                Apply(existing, input, summaryText, now);
                await _repository.UpdateAsync(existing, cancellationToken);
            }

            await _repository.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // Summaries are best-effort and must never break a run/session.
            _logger.LogWarning(
                ex,
                "Failed to record simulation run summary for {SourceType} {SourceId}.",
                input.SourceType,
                input.SourceId);
        }
    }

    public async Task<ServiceResult<PagedResult<SimulationRunSummaryDto>>> GetSummariesAsync(
        PagedRequest request,
        SimulationRunSourceType? sourceType = null,
        CancellationToken cancellationToken = default)
    {
        var (items, totalCount) = await _repository.GetPagedAsync(request, sourceType, cancellationToken);

        return ServiceResult<PagedResult<SimulationRunSummaryDto>>.Ok(new PagedResult<SimulationRunSummaryDto>
        {
            Items = items.Select(MapToDto).ToList(),
            Page = Math.Max(request.Page, 1),
            PageSize = Math.Max(request.PageSize, 1),
            TotalCount = totalCount
        });
    }

    public async Task<ServiceResult<SimulationRunSummaryDto>> GetSummaryAsync(
        SimulationRunSourceType sourceType,
        long sourceId,
        CancellationToken cancellationToken = default)
    {
        var summary = await _repository.GetBySourceAsync(sourceType, sourceId, cancellationToken);
        if (summary is null)
        {
            return ServiceResult<SimulationRunSummaryDto>.Fail("Summary was not found.");
        }

        return ServiceResult<SimulationRunSummaryDto>.Ok(MapToDto(summary));
    }

    private static void Apply(SimulationRunSummary summary, SimulationRunSummaryInput input, string summaryText, DateTime now)
    {
        summary.Name = input.Name;
        summary.Status = input.Status;
        summary.StartedAtUtc = input.StartedAtUtc;
        summary.CompletedAtUtc = input.CompletedAtUtc;
        summary.SymbolsJson = Serialize(input.Symbols);
        summary.StrategiesJson = Serialize(input.Strategies);
        summary.TimeframesJson = Serialize(input.Timeframes);
        summary.EvaluationMode = input.EvaluationMode;
        summary.InitialBalance = input.InitialBalance;
        summary.FinalBalance = input.FinalBalance;
        summary.NetPnl = input.NetPnl;
        summary.NetPnlPercent = input.NetPnlPercent;
        summary.MaxDrawdown = input.MaxDrawdown;
        summary.TotalTrades = input.TotalTrades;
        summary.WinningTrades = input.WinningTrades;
        summary.LosingTrades = input.LosingTrades;
        summary.WinRatePercent = input.WinRatePercent;
        summary.CandidateSignals = input.CandidateSignals;
        summary.ConfidenceRejected = input.ConfidenceRejected;
        summary.RiskRejected = input.RiskRejected;
        summary.ExecutedTrades = input.ExecutedTrades;
        summary.ShadowTrades = input.ShadowTrades;
        summary.ShadowNetPnl = input.ShadowNetPnl;
        summary.RejectedWouldHaveWon = input.RejectedWouldHaveWon;
        summary.RejectedWouldHaveLost = input.RejectedWouldHaveLost;
        summary.SummaryText = summaryText;
        summary.KeyFindingsJson = Serialize(input.KeyFindings);
        summary.WarningsJson = Serialize(input.Warnings);
        summary.UpdatedAtUtc = now;
    }

    private static string BuildSummaryText(SimulationRunSummaryInput input)
    {
        var builder = new StringBuilder();
        var symbols = input.Symbols.Count > 0 ? string.Join(", ", input.Symbols) : "no symbols";
        var strategies = input.Strategies.Count > 0 ? string.Join(", ", input.Strategies) : "no strategies";
        var timeframes = input.Timeframes.Count > 0 ? string.Join(", ", input.Timeframes) : "n/a";

        builder.Append($"{input.SourceType} '{input.Name}' finished with status {input.Status}. ");
        builder.Append($"Tested {strategies} on {symbols} ({timeframes})");
        if (!string.IsNullOrWhiteSpace(input.EvaluationMode))
        {
            builder.Append($" in {input.EvaluationMode} mode");
        }
        builder.Append(". ");

        builder.Append(
            $"{input.CandidateSignals} candidate signal(s): {input.ExecutedTrades} executed, " +
            $"{input.ConfidenceRejected} rejected by confidence, {input.RiskRejected} rejected by risk. ");

        if (input.TotalTrades > 0)
        {
            builder.Append(
                $"Executed trades: net PnL {input.NetPnl:0.##} ({input.NetPnlPercent:0.##}%), " +
                $"win rate {input.WinRatePercent:0.#}% ({input.WinningTrades}W/{input.LosingTrades}L), " +
                $"max drawdown {input.MaxDrawdown:0.##}%. ");
        }
        else
        {
            builder.Append("No trades were executed. ");
        }

        if (input.ShadowTrades > 0)
        {
            builder.Append(
                $"Shadow analysis: {input.ShadowTrades} rejected trade(s) simulated, " +
                $"{input.RejectedWouldHaveWon} would have won, {input.RejectedWouldHaveLost} would have lost, " +
                $"shadow net PnL {input.ShadowNetPnl:0.##}.");
        }

        return builder.ToString().Trim();
    }

    private static SimulationRunSummaryDto MapToDto(SimulationRunSummary summary) => new()
    {
        Id = summary.Id,
        SourceType = summary.SourceType,
        SourceId = summary.SourceId,
        Name = summary.Name,
        Status = summary.Status,
        StartedAtUtc = summary.StartedAtUtc,
        CompletedAtUtc = summary.CompletedAtUtc,
        Symbols = Deserialize(summary.SymbolsJson),
        Strategies = Deserialize(summary.StrategiesJson),
        Timeframes = Deserialize(summary.TimeframesJson),
        EvaluationMode = summary.EvaluationMode,
        InitialBalance = summary.InitialBalance,
        FinalBalance = summary.FinalBalance,
        NetPnl = summary.NetPnl,
        NetPnlPercent = summary.NetPnlPercent,
        MaxDrawdown = summary.MaxDrawdown,
        TotalTrades = summary.TotalTrades,
        WinningTrades = summary.WinningTrades,
        LosingTrades = summary.LosingTrades,
        WinRatePercent = summary.WinRatePercent,
        CandidateSignals = summary.CandidateSignals,
        ConfidenceRejected = summary.ConfidenceRejected,
        RiskRejected = summary.RiskRejected,
        ExecutedTrades = summary.ExecutedTrades,
        ShadowTrades = summary.ShadowTrades,
        ShadowNetPnl = summary.ShadowNetPnl,
        RejectedWouldHaveWon = summary.RejectedWouldHaveWon,
        RejectedWouldHaveLost = summary.RejectedWouldHaveLost,
        SummaryText = summary.SummaryText,
        KeyFindings = Deserialize(summary.KeyFindingsJson),
        Warnings = Deserialize(summary.WarningsJson),
        CreatedAtUtc = summary.CreatedAtUtc,
        UpdatedAtUtc = summary.UpdatedAtUtc
    };

    private static string Serialize(IReadOnlyList<string> values) =>
        JsonSerializer.Serialize(values ?? []);

    private static IReadOnlyList<string> Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
