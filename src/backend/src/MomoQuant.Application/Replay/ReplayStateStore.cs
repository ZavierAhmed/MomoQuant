using MomoQuant.Application.Backtesting;
using MomoQuant.Domain.Replay;
using MomoQuant.Domain.Risk;

namespace MomoQuant.Application.Replay;

public sealed class ReplayRuntimeState
{
    public required ReplaySession Session { get; init; }
    public required ReplaySessionSettings Settings { get; init; }
    public required Domain.Exchanges.Symbol Symbol { get; init; }
    public required IReadOnlyList<RiskRule> RiskRules { get; init; }
    public required BacktestContext Context { get; init; }
    public required BacktestDataset Dataset { get; init; }
    public required IReadOnlyList<PreparedStrategy> Strategies { get; init; }
    public int CurrentFrameIndex { get; set; } = -1;
}

public interface IReplayStateStore
{
    bool TryGet(long sessionId, out ReplayRuntimeState? state);

    void Set(long sessionId, ReplayRuntimeState state);

    void Remove(long sessionId);
}

public sealed class ReplayStateStore : IReplayStateStore
{
    private readonly Dictionary<long, ReplayRuntimeState> _states = new();

    public bool TryGet(long sessionId, out ReplayRuntimeState? state)
    {
        if (_states.TryGetValue(sessionId, out var existing))
        {
            state = existing;
            return true;
        }

        state = null;
        return false;
    }

    public void Set(long sessionId, ReplayRuntimeState state) => _states[sessionId] = state;

    public void Remove(long sessionId) => _states.Remove(sessionId);
}
