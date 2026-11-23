using Holograma3D.Core.Ports;

namespace Holograma3D.Api.Realtime;

/// <summary>Cada 10 minutos borra las sesiones con más de 12 h sin actividad.</summary>
// ponytail: barrido periódico de la memoria; con Redis sería un TTL por clave.
public sealed class SessionCleanup(ISessionStore store, TimeProvider time) : BackgroundService
{
    public static readonly TimeSpan MaxIdle = TimeSpan.FromHours(12);
    private static readonly TimeSpan Every = TimeSpan.FromMinutes(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Every, time);
        while (await timer.WaitForNextTickAsync(stoppingToken))
            RemoveIdle(store, time.GetUtcNow() - MaxIdle);
    }

    public static void RemoveIdle(ISessionStore store, DateTimeOffset cutoff)
    {
        foreach (var session in store.All())
        {
            bool idle;
            lock (session.Gate)
                idle = session.LastActivity < cutoff;
            if (idle)
                store.Remove(session.Code);
        }
    }
}
