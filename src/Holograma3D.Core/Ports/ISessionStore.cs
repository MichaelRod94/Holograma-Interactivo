using Holograma3D.Core.Domain.Sessions;

namespace Holograma3D.Core.Ports;

/// <summary>Sesiones activas. Síncrono porque hoy vive en memoria.</summary>
// ponytail: volver async si se migra a Redis.
public interface ISessionStore
{
    /// <returns>false si el código ya existe.</returns>
    bool TryAdd(HologramSession session);

    HologramSession? Get(string code);

    void Remove(string code);

    IEnumerable<HologramSession> All();
}
