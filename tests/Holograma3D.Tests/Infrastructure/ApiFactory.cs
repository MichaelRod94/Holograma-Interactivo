using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;

namespace Holograma3D.Tests.Infrastructure;

/// <summary>
/// La Api completa sobre una base nueva ("t_" + Guid) del contenedor. Corre en Development,
/// así que Program aplica las migraciones al arrancar.
/// </summary>
public sealed class ApiFactory(PostgresFixture db, int authPerMinute = 1000) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var connectionString = new NpgsqlConnectionStringBuilder(db.ConnectionString) { Database = "t_" + Guid.NewGuid().ToString("N") };
        builder.UseSetting("ConnectionStrings:Holograma3D", connectionString.ConnectionString);
        builder.UseSetting("RateLimits:AuthPerMinute", authPerMinute.ToString());
    }
}
