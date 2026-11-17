using Holograma3D.Core;

namespace Holograma3D.Tests;

public class ArchitectureTests
{
    // El núcleo hexagonal no puede conocer a sus adaptadores.
    private static readonly string[] Forbidden =
    [
        "Microsoft.AspNetCore",
        "Microsoft.EntityFrameworkCore",
        "Npgsql",
        "SkiaSharp",
        "Microsoft.ML",
        "Holograma3D.Infrastructure",
    ];

    [Fact]
    public void Core_no_depende_de_adaptadores()
    {
        var offenders = typeof(CoreAssembly).Assembly.GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(name => Forbidden.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)))
            .ToList();

        Assert.Empty(offenders);
    }
}
