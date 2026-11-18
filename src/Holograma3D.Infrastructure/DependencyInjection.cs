using Holograma3D.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Holograma3D.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Holograma3D");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException(
                "Falta la cadena de conexión 'ConnectionStrings:Holograma3D'. " +
                "Ponla en .env como ConnectionStrings__Holograma3D (run.ps1 la carga) o en user-secrets: " +
                "dotnet user-secrets set \"ConnectionStrings:Holograma3D\" \"<cadena>\" --project src/Holograma3D.Api");

        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(connectionString));
        return services;
    }
}
