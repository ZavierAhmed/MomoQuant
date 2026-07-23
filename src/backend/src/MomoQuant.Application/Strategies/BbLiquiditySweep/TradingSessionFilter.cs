using System.Collections.Concurrent;
using MomoQuant.Application.Options;

namespace MomoQuant.Application.Strategies.BbLiquiditySweep;

public sealed class TradingSessionFilter
{
    private readonly IReadOnlyList<TradingSessionWindowSettings> _sessions;

    public TradingSessionFilter(IReadOnlyList<TradingSessionWindowSettings>? sessions = null) =>
        _sessions = sessions ?? new BbLiquiditySweepSettings().Sessions;

    public (bool InSession, string? SessionName) IsInAllowedSession(DateTime candleTimeUtc, bool useSessionFilter, IReadOnlyList<string>? allowedSessions = null)
    {
        if (!useSessionFilter)
        {
            return (true, "24/7");
        }

        foreach (var session in _sessions)
        {
            if (allowedSessions is { Count: > 0 }
                && !allowedSessions.Contains(session.Name, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            if (IsWithinSession(candleTimeUtc, session))
            {
                return (true, session.Name);
            }
        }

        return (false, null);
    }

    public string? ResolveSessionName(DateTime candleTimeUtc)
    {
        foreach (var session in _sessions)
        {
            if (IsWithinSession(candleTimeUtc, session))
            {
                return session.Name;
            }
        }

        return null;
    }

    private static bool IsWithinSession(DateTime candleTimeUtc, TradingSessionWindowSettings session)
    {
        var timezone = TimeZoneInfo.FindSystemTimeZoneById(session.Timezone);
        var local = TimeZoneInfo.ConvertTimeFromUtc(candleTimeUtc, timezone);
        if (!TimeSpan.TryParse(session.Start, out var start) || !TimeSpan.TryParse(session.End, out var end))
        {
            return false;
        }

        var time = local.TimeOfDay;
        return start <= end
            ? time >= start && time < end
            : time >= start || time < end;
    }
}

public interface IBbLiquiditySweepSessionTracker
{
    void ResetRun(long tradingSessionId);
    void RecordLoss(long tradingSessionId, DateTime closeTimeUtc, string? sessionName);
    bool IsBlocked(long tradingSessionId, DateTime candleTimeUtc, int stopAfterLossesPerSession, bool useSessionFilter);
}

public sealed class BbLiquiditySweepSessionTracker : IBbLiquiditySweepSessionTracker
{
    private readonly TradingSessionFilter _sessionFilter = new();
    private readonly ConcurrentDictionary<string, int> _lossCounts = new(StringComparer.OrdinalIgnoreCase);

    public void ResetRun(long tradingSessionId) =>
        _lossCounts.Clear();

    public void RecordLoss(long tradingSessionId, DateTime closeTimeUtc, string? sessionName)
    {
        sessionName ??= _sessionFilter.ResolveSessionName(closeTimeUtc) ?? "Unknown";
        var key = BuildKey(tradingSessionId, sessionName, closeTimeUtc);
        _lossCounts.AddOrUpdate(key, 1, (_, count) => count + 1);
    }

    public bool IsBlocked(long tradingSessionId, DateTime candleTimeUtc, int stopAfterLossesPerSession, bool useSessionFilter)
    {
        if (!useSessionFilter)
        {
            return false;
        }

        var sessionName = _sessionFilter.ResolveSessionName(candleTimeUtc);
        if (sessionName is null)
        {
            return false;
        }

        var key = BuildKey(tradingSessionId, sessionName, candleTimeUtc);
        return _lossCounts.TryGetValue(key, out var losses) && losses > stopAfterLossesPerSession;
    }

    private static string BuildKey(long tradingSessionId, string sessionName, DateTime candleTimeUtc)
    {
        var timezone = TimeZoneInfo.Utc;
        var date = candleTimeUtc.Date;
        return $"{tradingSessionId}:{sessionName}:{date:yyyy-MM-dd}";
    }
}
