using Finestats.Events;

namespace Finestats.Services;

public sealed class SessionState(int slot, long startedAt, bool partial)
{
    public PlayerIdentity Player { get; set; } = new(slot, Guid.NewGuid().ToString("N"), null, null, null, null, false);
    public ulong? NativeSessionId { get; set; }
    public long StartedAt { get; } = startedAt;
    public bool Partial { get; } = partial;
    public bool AuthReported { get; set; }
    public bool CountryChecked { get; set; }
}

public sealed class SessionRegistry
{
    private readonly Dictionary<int, SessionState> _sessions = [];
    public IEnumerable<SessionState> Values => _sessions.Values;
    public SessionState? Find(int slot) => _sessions.GetValueOrDefault(slot);
    public SessionState Start(int slot, long now, bool partial)
    {
        var state = new SessionState(slot, now, partial);
        _sessions.Add(slot, state);
        return state;
    }
    public bool Remove(int slot) => _sessions.Remove(slot);
    public void Clear() => _sessions.Clear();
}
