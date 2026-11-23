using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Holograma3D.Tests.Infrastructure;

/// <summary>
/// La Api completa sobre la base de prueba del PostgreSQL local y una carpeta temporal de archivos.
/// Corre en Development, así que Program aplica las migraciones al arrancar.
/// </summary>
public sealed class ApiFactory(PostgresFixture db, int authPerMinute = 1000, IReadOnlyDictionary<string, string>? settings = null)
    : WebApplicationFactory<Program>
{
    private readonly string _storageRoot = Path.Combine(Path.GetTempPath(), "holograma3d-tests", Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Holograma3D", db.ConnectionString);
        builder.UseSetting("RateLimits:AuthPerMinute", authPerMinute.ToString());
        builder.UseSetting("Storage:Root", _storageRoot);
        builder.UseSetting("GenerativeAi:Provider", ""); // aislado del proveedor real del .env
        foreach (var (key, value) in settings ?? new Dictionary<string, string>())
            builder.UseSetting(key, value);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(_storageRoot))
            Directory.Delete(_storageRoot, recursive: true);
    }
}
