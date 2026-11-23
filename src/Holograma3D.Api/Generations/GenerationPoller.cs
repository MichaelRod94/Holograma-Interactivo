using Holograma3D.Core.Application;
using Holograma3D.Infrastructure.GenerativeAi;
using Microsoft.Extensions.Options;

namespace Holograma3D.Api.Generations;

/// <summary>Cada PollSeconds sigue los trabajos de IA activos.</summary>
// ponytail: un solo proceso consulta; con varias instancias, un lock en la base o una cola.
public sealed class GenerationPoller(IServiceScopeFactory scopes, IOptions<GenerativeAiOptions> options, TimeProvider time,
    ILogger<GenerationPoller> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.Value.PollSeconds), time);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var errors = await scope.ServiceProvider.GetRequiredService<GenerationService>().PollOnceAsync(stoppingToken);
                foreach (var (jobId, error) in errors)
                    log.LogWarning("La generación {JobId} no avanzó en este ciclo; se reintenta: {Error}", jobId, error.Message);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                log.LogError(ex, "Falló el ciclo de consulta de generaciones");
            }
        }
    }
}
