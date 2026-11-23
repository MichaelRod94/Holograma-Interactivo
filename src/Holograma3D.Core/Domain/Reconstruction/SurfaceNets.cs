using System.Numerics;

namespace Holograma3D.Core.Domain.Reconstruction;

/// <summary>Malla triangular indexada; los triángulos van en sentido antihorario visto desde fuera.</summary>
public sealed record Mesh(IReadOnlyList<Vector3> Vertices, IReadOnlyList<int> Triangles)
{
    public int TriangleCount => Triangles.Count / 3;
}

/// <summary>
/// Naive Surface Nets (https://0fps.net/2012/07/12/smooth-voxel-terrain-part-2/): un vértice por celda que cruza la superficie
/// y un quad por cada arista del grid que la cruza. "Dentro" es valor &gt; isovalor.
/// </summary>
public static class SurfaceNets
{
    // Esquinas de una celda (bit 0 = x, bit 1 = y, bit 2 = z) y sus 12 aristas.
    private static readonly Vector3[] Corners = Enumerable.Range(0, 8).Select(i => new Vector3(i & 1, (i >> 1) & 1, (i >> 2) & 1)).ToArray();
    private static readonly (int A, int B)[] Edges =
        [.. from i in Enumerable.Range(0, 8) from bit in new[] { 1, 2, 4 } where (i & bit) == 0 select (i, i | bit)];

    public static Mesh Extract(VoxelGrid field, float iso = 0.5f)
    {
        int[] n = [field.Nx, field.Ny, field.Nz];
        int cx = n[0] - 1, cy = n[1] - 1, cz = n[2] - 1;
        var cellVertex = new int[Math.Max(0, cx * cy * cz)];
        Array.Fill(cellVertex, -1);
        var vertices = new List<Vector3>();
        var values = new float[8];

        // 1. Un vértice por celda con esquinas a los dos lados: el promedio de los cruces en sus aristas.
        for (var z = 0; z < cz; z++)
            for (var y = 0; y < cy; y++)
                for (var x = 0; x < cx; x++)
                {
                    var inside = 0;
                    for (var c = 0; c < 8; c++)
                    {
                        values[c] = field[x + (c & 1), y + ((c >> 1) & 1), z + ((c >> 2) & 1)];
                        if (values[c] > iso)
                            inside |= 1 << c;
                    }
                    if (inside is 0 or 255)
                        continue;

                    var sum = Vector3.Zero;
                    var crossings = 0;
                    foreach (var (a, b) in Edges)
                    {
                        if (((inside >> a) & 1) == ((inside >> b) & 1))
                            continue;
                        var t = (iso - values[a]) / (values[b] - values[a]);
                        sum += Vector3.Lerp(Corners[a], Corners[b], t);
                        crossings++;
                    }
                    cellVertex[(z * cy + y) * cx + x] = vertices.Count;
                    vertices.Add(new Vector3(x, y, z) + sum / crossings);
                }

        // 2. Un quad por arista del grid que cruza la superficie, entre las 4 celdas que la comparten.
        //    Para la arista en el eje i, con (i, j, k) cíclico: A = p − e_j − e_k, B = p − e_k, C = p, D = p − e_j
        //    va en sentido antihorario visto desde +i (e_j × e_k = e_i).
        var triangles = new List<int>();
        int[] p = new int[3];
        for (var i = 0; i < 3; i++)
        {
            int j = (i + 1) % 3, k = (i + 2) % 3;
            for (p[k] = 1; p[k] < n[k] - 1; p[k]++)
                for (p[j] = 1; p[j] < n[j] - 1; p[j]++)
                    for (p[i] = 0; p[i] < n[i] - 1; p[i]++)
                    {
                        var startInside = field[p[0], p[1], p[2]] > iso;
                        p[i]++;
                        var endInside = field[p[0], p[1], p[2]] > iso;
                        p[i]--;
                        if (startInside == endInside)
                            continue;

                        var c = Cell(p);
                        p[j]--;
                        var d = Cell(p);
                        p[k]--;
                        var a = Cell(p);
                        p[j]++;
                        var b = Cell(p);
                        p[k]++;
                        // Si el inicio está dentro, la superficie mira hacia +i.
                        if (startInside)
                            triangles.AddRange([a, b, c, a, c, d]);
                        else
                            triangles.AddRange([a, c, b, a, d, c]);
                    }
        }
        return new Mesh(vertices, triangles);

        int Cell(int[] q) => cellVertex[(q[2] * cy + q[1]) * cx + q[0]];
    }
}
