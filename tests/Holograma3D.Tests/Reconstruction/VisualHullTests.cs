using System.Numerics;
using Holograma3D.Core.Domain.Reconstruction;

namespace Holograma3D.Tests.Reconstruction;

public sealed class VisualHullTests
{
    /// <summary>
    /// Cada cámara descrita físicamente (ortográfica): hacia dónde apuntan la derecha y el arriba de su foto en el mundo
    /// (x derecha vista de frente, y arriba, z hacia la cámara frontal). No reutiliza los índices de VisualHull.
    /// </summary>
    private static readonly Dictionary<View, (Vector3 Right, Vector3 Up)> Cameras = new()
    {
        [View.Front] = (Vector3.UnitX, Vector3.UnitY),   // en +z mirando a −z
        [View.Back] = (-Vector3.UnitX, Vector3.UnitY),   // en −z mirando a +z: su derecha es −x
        [View.Left] = (Vector3.UnitZ, Vector3.UnitY),    // te mueves a TU izquierda: en −x mirando a +x; su derecha es el frente
        [View.Right] = (-Vector3.UnitZ, Vector3.UnitY),  // en +x mirando a −x
        [View.Top] = (Vector3.UnitX, -Vector3.UnitZ),    // desde arriba con el frente abajo en la foto: el arriba de la foto es −z
    };

    /// <summary>Silueta del sólido vista desde una cámara: proyecta cada voxel ocupado.</summary>
    internal static Mask Project(bool[,,] solid, View view)
    {
        var (right, up) = Cameras[view];
        int nx = solid.GetLength(0), ny = solid.GetLength(1), nz = solid.GetLength(2);
        var corners = from x in new[] { 0, nx - 1 } from y in new[] { 0, ny - 1 } from z in new[] { 0, nz - 1 } select new Vector3(x, y, z);
        var u = corners.Select(c => (int)Vector3.Dot(c, right)).ToArray();
        var v = corners.Select(c => (int)Vector3.Dot(c, up)).ToArray();
        int uMin = u.Min(), vMax = v.Max(), width = u.Max() - uMin + 1, height = vMax - v.Min() + 1;
        var data = new bool[width * height];
        for (var z = 0; z < nz; z++)
            for (var y = 0; y < ny; y++)
                for (var x = 0; x < nx; x++)
                    if (solid[x, y, z])
                    {
                        var p = new Vector3(x, y, z);
                        var col = (int)Vector3.Dot(p, right) - uMin;
                        var row = vMax - (int)Vector3.Dot(p, up);
                        data[row * width + col] = true;
                    }
        return new Mask(width, height, data);
    }

    internal static bool[,,] Solid(int nx, int ny, int nz, Func<int, int, int, bool> inside)
    {
        var solid = new bool[nx, ny, nz];
        for (var z = 0; z < nz; z++)
            for (var y = 0; y < ny; y++)
                for (var x = 0; x < nx; x++)
                    solid[x, y, z] = inside(x, y, z);
        return solid;
    }

    internal static Dictionary<View, Mask> Photos(bool[,,] solid) => Enum.GetValues<View>().ToDictionary(v => v, v => Project(solid, v));

    private static int Volume(VoxelGrid grid) => grid.Values.Count(v => v > 0);

    [Fact]
    public void Caja_con_mascaras_rectangulares_da_su_volumen()
    {
        var grid = VisualHull.Occupancy(Photos(Solid(60, 40, 30, (_, _, _) => true)), 60);

        Assert.Equal((60, 40, 30), (grid.Nx, grid.Ny, grid.Nz));
        Assert.InRange(Volume(grid), 60 * 40 * 30 * 0.97, 60 * 40 * 30 * 1.03);
    }

    [Fact]
    public void Circulos_en_las_cinco_vistas_dan_la_interseccion_de_tres_cilindros()
    {
        const int r = 40;
        var circle = Mask.From(2 * r, 2 * r, (c, row) => Math.Pow(c + 0.5 - r, 2) + Math.Pow(row + 0.5 - r, 2) <= r * r);
        var masks = Enum.GetValues<View>().ToDictionary(v => v, _ => circle);

        var volume = Volume(VisualHull.Occupancy(masks, 2 * r));

        var expected = 8 * (2 - Math.Sqrt(2)) * r * r * r;
        Assert.InRange(volume, expected * 0.94, expected * 1.06);
    }

    public static TheoryData<string> Prismas => ["frente", "laterales", "superior"];

    /// <summary>
    /// Un prisma con sección en L extruido: el hueco solo se ve en un par de vistas. Si algún índice de VisualHull
    /// está al revés, el hueco aparece también del otro lado y el volumen deja de coincidir.
    /// </summary>
    [Theory]
    [MemberData(nameof(Prismas))]
    public void Prisma_en_L_respeta_la_convencion_de_ejes(string caso)
    {
        Func<int, int, int, bool> inside = caso switch
        {
            "frente" => (x, y, _) => !(x >= 25 && y >= 18),     // hueco arriba a la derecha, visto de frente
            "laterales" => (_, y, z) => !(z >= 12 && y >= 18),  // hueco arriba, hacia el frente
            _ => (x, _, z) => !(x >= 25 && z >= 12),            // hueco frontal derecho, visto desde arriba
        };
        var solid = Solid(40, 30, 20, inside);

        var grid = VisualHull.Occupancy(Photos(solid), 40);

        Assert.Equal((40, 30, 20), (grid.Nx, grid.Ny, grid.Nz));
        var matches = 0;
        for (var z = 0; z < 20; z++)
            for (var y = 0; y < 30; y++)
                for (var x = 0; x < 40; x++)
                    if ((grid[x, y, z] > 0) == solid[x, y, z])
                        matches++;
        Assert.True(matches >= 0.97 * 40 * 30 * 20, $"{caso}: coinciden {matches} de {40 * 30 * 20} voxeles");
    }

    [Fact]
    public void Una_mancha_suelta_no_cambia_el_volumen()
    {
        var masks = Photos(Solid(30, 20, 10, (_, _, _) => true));
        var front = masks[View.Front];
        // Mancha a la derecha: la componente más grande la descarta y el recorte no se agranda.
        var noisy = Mask.From(front.Width + 10, front.Height, (c, r) => c < front.Width ? front[c, r] : c == front.Width + 8 && r == 2);
        masks[View.Front] = noisy;

        var grid = VisualHull.Occupancy(masks, 30);

        Assert.Equal((30, 20, 10), (grid.Nx, grid.Ny, grid.Nz));
    }
}
