using System.Collections.Concurrent;
using Holograma3D.Core.Domain.Sessions;
using Holograma3D.Core.Ports;

namespace Holograma3D.Infrastructure;

// ponytail: un solo servidor y se pierde al reiniciar; para escalar, Redis + backplane de SignalR.
public sealed class InMemorySessionStore : ISessionStore
{
    private readonly ConcurrentDictionary<string, HologramSession> _sessions = new();

    public bool TryAdd(HologramSession session) => _sessions.TryAdd(session.Code, session);

    public HologramSession? Get(string code) => _sessions.TryGetValue(code, out var session) ? session : null;

    public void Remove(string code) => _sessions.TryRemove(code, out _);

    public IEnumerable<HologramSession> All() => _sessions.Values;
}
