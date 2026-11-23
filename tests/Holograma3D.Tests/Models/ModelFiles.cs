using System.Buffers.Binary;
using System.Text;

namespace Holograma3D.Tests.Models;

/// <summary>Archivos 3D sintéticos mínimos.</summary>
internal static class ModelFiles
{
    /// <summary>STL binario: cabecera de 80 bytes, n y 50 bytes por triángulo.</summary>
    public static byte[] BinaryStl(uint triangles, string header = "Holograma3D test")
    {
        var bytes = new byte[84 + 50 * triangles];
        Encoding.ASCII.GetBytes(header, bytes);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(80), triangles);
        for (var i = 0; i < triangles; i++)
        {
            var t = bytes.AsSpan(84 + 50 * i);
            // normal (0,0,1) y vértices (0,0,0) (1,0,0) (0,1,0)
            BinaryPrimitives.WriteSingleLittleEndian(t[8..], 1f);
            BinaryPrimitives.WriteSingleLittleEndian(t[24..], 1f);
            BinaryPrimitives.WriteSingleLittleEndian(t[40..], 1f);
        }
        return bytes;
    }

    public static byte[] AsciiStl(int facets, bool withEnd = true)
    {
        var sb = new StringBuilder("solid prueba\n");
        for (var i = 0; i < facets; i++)
            sb.Append("  facet normal 0 0 1\n    outer loop\n      vertex 0 0 0\n      vertex 1 0 0\n      vertex 0 1 0\n    endloop\n  endfacet\n");
        if (withEnd)
            sb.Append("endsolid prueba\n");
        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    /// <summary>GLB mínimo: cabecera de 12 bytes y un chunk JSON.</summary>
    public static byte[] Glb()
    {
        var json = Encoding.UTF8.GetBytes("""{"asset":{"version":"2.0"}}""");
        var padded = (json.Length + 3) / 4 * 4;
        var bytes = new byte[12 + 8 + padded];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, 0x46546C67);       // "glTF"
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), 2);       // versión
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), (uint)bytes.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), (uint)padded);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16), 0x4E4F534A); // "JSON"
        json.CopyTo(bytes, 20);
        bytes.AsSpan(20 + json.Length).Fill((byte)' ');
        return bytes;
    }
}
