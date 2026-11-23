using Holograma3D.Core.Ports;
using Holograma3D.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Holograma3D.Infrastructure;

public static class DependencyInjection
{
    /// <param name="contentRoot">Base para rutas relativas, como "Storage:Root" (por defecto "storage").</param>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration, string contentRoot)
    {
        var connectionString = configuration.GetConnectionString("Holograma3D");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException(
                "Falta la cadena de conexión 'ConnectionStrings:Holograma3D'. " +
                "Ponla en .env como ConnectionStrings__Holograma3D (run.ps1 la carga) o en user-secrets: " +
                "dotnet user-secrets set \"ConnectionStrings:Holograma3D\" \"<cadena>\" --project src/Holograma3D.Api");

        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(connectionString));

        var storageRoot = Path.GetFullPath(configuration["Storage:Root"] ?? "storage", contentRoot);
        var modelsDir = Path.Combine(storageRoot, "models");
        services.AddScoped<IModelStore>(sp => new EfModelStore(
            sp.GetRequiredService<AppDbContext>(), modelsDir, sp.GetRequiredService<ILogger<EfModelStore>>()));
        return services;
    }
}
