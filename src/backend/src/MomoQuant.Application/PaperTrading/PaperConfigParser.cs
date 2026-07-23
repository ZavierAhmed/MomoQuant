using System.Text.Json;
using MomoQuant.Application.MarketData;
using MomoQuant.Application.PaperTrading.Dtos;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.PaperTrading;

namespace MomoQuant.Application.PaperTrading;

public static class PaperConfigParser
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static CreatePaperSessionRequest? ParseCreateRequest(string? configJson)
    {
        if (string.IsNullOrWhiteSpace(configJson))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<CreatePaperSessionRequest>(configJson, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public static IReadOnlyList<long> ResolveSymbolIds(PaperTradingSession session, PaperSessionState? state)
    {
        if (state is not null)
        {
            return state.Settings.SymbolIds;
        }

        return ParseCreateRequest(session.ConfigJson)?.SymbolIds ?? [];
    }

    public static IReadOnlyList<Timeframe> ResolveTimeframes(PaperTradingSession session, PaperSessionState? state)
    {
        if (state is not null)
        {
            return state.Settings.Timeframes;
        }

        var request = ParseCreateRequest(session.ConfigJson);
        if (request?.Timeframes is null)
        {
            return [];
        }

        return request.Timeframes
            .Select(tf => TimeframeParser.TryParse(tf, out var parsed) ? parsed : default)
            .Where(tf => tf != default)
            .ToList();
    }
}
