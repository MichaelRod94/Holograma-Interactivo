using System.Numerics;
using System.Text;

namespace Holograma3D.Core.Domain.Reconstruction;

/// <summary>STL binario: la dimensión mayor mide 100 mm y Z va arriba, como lo esperan los programas de CAD e impresión 3D.</summary>
public static class StlWriter
{
    public const float LongestSideMm = 100f;

    public static byte[] Write(Mesh mesh)
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var v in mesh.Vertices)
        {
            min = Vector3.Min(min, v);
            max = Vector3.Max(max, v);
        }
        var extent = max - min;
        var scale = mesh.Vertices.Count == 0 ? 1f : LongestSideMm / MathF.Max(extent.X, MathF.Max(extent.Y, extent.Z));
        // Y arriba → Z arriba: (x, y, z) → (x, −z, y) es una rotación, así que conserva el sentido de los triángulos.
        Vector3 ToStl(Vector3 v)
        {
            var s = (v - min) * scale;
            return new Vector3(s.X, -s.Z, s.Y);
        }

        using var stream = new MemoryStream(84 + mesh.TriangleCount * 50);
        using var writer = new BinaryWriter(stream);
        var header = new byte[80];
        Encoding.ASCII.GetBytes("Holograma3D visual hull").CopyTo(header, 0);
        writer.Write(header);
        writer.Write((uint)mesh.TriangleCount);
        for (var t = 0; t < mesh.Triangles.Count; t += 3)
        {
            var a = ToStl(mesh.Vertices[mesh.Triangles[t]]);
            var b = ToStl(mesh.Vertices[mesh.Triangles[t + 1]]);
            var c = ToStl(mesh.Vertices[mesh.Triangles[t + 2]]);
            var normal = Vector3.Cross(b - a, c - a);
            normal = normal == Vector3.Zero ? normal : Vector3.Normalize(normal);
            foreach (var v in (ReadOnlySpan<Vector3>)[normal, a, b, c])
            {
                writer.Write(v.X);
                writer.Write(v.Y);
                writer.Write(v.Z);
            }
            writer.Write((ushort)0);
        }
        writer.Flush();
        return stream.ToArray();
    }
}
