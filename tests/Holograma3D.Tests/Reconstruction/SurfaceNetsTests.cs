using System.Numerics;
using Holograma3D.Core.Domain.Models;
using Holograma3D.Core.Domain.Reconstruction;

namespace Holograma3D.Tests.Reconstruction;

public sealed class SurfaceNetsTests
{
    private static Mesh BoxMesh(out int boxVolume)
    {
        var occupancy = VisualHull.Occupancy(VisualHullTests.Photos(VisualHullTests.Solid(20, 14, 10, (_, _, _) => true)), 20);
        boxVolume = 20 * 14 * 10;
        return SurfaceNets.Extract(VisualHull.Smooth(occupancy));
    }

    [Fact]
    public void La_malla_de_una_caja_es_cerrada_y_mira_hacia_fuera()
    {
        var mesh = BoxMesh(out var boxVolume);
        Assert.True(mesh.TriangleCount > 0);

        // Cerrada y bien orientada: cada arista dirigida aparece una vez y su inversa también, una vez.
        var edges = new Dictionary<(int, int), int>();
        for (var t = 0; t < mesh.Triangles.Count; t += 3)
            for (var e = 0; e < 3; e++)
            {
                var key = (mesh.Triangles[t + e], mesh.Triangles[t + (e + 1) % 3]);
                edges[key] = edges.GetValueOrDefault(key) + 1;
            }
        Assert.All(edges, pair =>
        {
            Assert.Equal(1, pair.Value);
            Assert.Equal(1, edges.GetValueOrDefault((pair.Key.Item2, pair.Key.Item1)));
        });

        // Volumen con signo (teorema de la divergencia): positivo si las normales miran hacia fuera.
        double volume = 0;
        for (var t = 0; t < mesh.Triangles.Count; t += 3)
        {
            Vector3 a = mesh.Vertices[mesh.Triangles[t]], b = mesh.Vertices[mesh.Triangles[t + 1]], c = mesh.Vertices[mesh.Triangles[t + 2]];
            volume += Vector3.Dot(a, Vector3.Cross(b, c)) / 6.0;
        }
        Assert.InRange(volume, boxVolume * 0.85, boxVolume * 1.05);
    }

    [Fact]
    public void El_stl_escrito_lo_acepta_el_inspector()
    {
        var mesh = BoxMesh(out _);

        var stl = StlWriter.Write(mesh);

        var info = ModelFileInspector.Inspect(new MemoryStream(stl), "caja.stl");
        Assert.Equal((ModelFormat.Stl, mesh.TriangleCount), (info.Format, info.TriangleCount));
    }
}
