using System.Buffers.Binary;
using System.Text;

namespace Holograma3D.Core.Domain.Models;

public readonly record struct InspectionResult(ModelFormat Format, int? TriangleCount);

/// <summary>Reconoce STL (binario o ASCII) y GLB por su contenido, no por la extensión.</summary>
public static class ModelFileInspector
{
    public const int MaxTriangles = 5_000_000;
    private const uint GlbMagic = 0x46546C67; // "glTF" en little-endian

    /// <summary>Requiere un stream con Seek; lo deja en la posición 0.</summary>
    /// <exception cref="ModelValidationException">El archivo no es un modelo válido.</exception>
    public static InspectionResult Inspect(Stream s, string fileName)
    {
        if (!s.CanSeek)
            throw new ArgumentException("El stream debe admitir Seek.", nameof(s));

        try
        {
            s.Position = 0;
            var result = Detect(s);
            var expected = result.Format == ModelFormat.Stl ? ".stl" : ".glb";
            if (!string.Equals(Path.GetExtension(fileName), expected, StringComparison.OrdinalIgnoreCase))
                throw new ModelValidationException($"La extensión no coincide con el contenido: el archivo es {expected}.");
            return result;
        }
        finally
        {
            s.Position = 0;
        }
    }

    private static InspectionResult Detect(Stream s)
    {
        var length = s.Length;
        if (length == 0)
            throw new ModelValidationException("El archivo está vacío.");

        Span<byte> head = stackalloc byte[84];
        var read = s.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);

        // STL binario primero: hay binarios cuya cabecera empieza con "solid".
        if (read == 84)
        {
            var n = BinaryPrimitives.ReadUInt32LittleEndian(head[80..]);
            if (length == 84 + 50L * n)
                return new(ModelFormat.Stl, CheckTriangles(n));
        }

        if (read >= 12 && BinaryPrimitives.ReadUInt32LittleEndian(head) == GlbMagic)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(head[4..]) != 2)
                throw new ModelValidationException("Solo se admite glTF binario versión 2.");
            if (BinaryPrimitives.ReadUInt32LittleEndian(head[8..]) != length)
                throw new ModelValidationException("El GLB está incompleto o dañado.");
            return new(ModelFormat.Glb, null);
        }

        if (StartsWithSolid(head[..read]))
        {
            s.Position = 0;
            return new(ModelFormat.Stl, CheckTriangles(CountAsciiFacets(s)));
        }

        throw new ModelValidationException("Formato no reconocido: sube un archivo .stl o .glb.");
    }

    private static bool StartsWithSolid(ReadOnlySpan<byte> head)
    {
        var start = 0;
        while (start < head.Length && char.IsWhiteSpace((char)head[start]))
            start++;
        return head[start..].StartsWith("solid"u8);
    }

    private static long CountAsciiFacets(Stream s)
    {
        using var reader = new StreamReader(s, Encoding.ASCII, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        long facets = 0;
        var ended = false;
        while (reader.ReadLine() is { } line)
        {
            var t = line.AsSpan().TrimStart();
            if (t.StartsWith("facet normal", StringComparison.OrdinalIgnoreCase) && ++facets > MaxTriangles)
                break; // CheckTriangles lo rechaza; no hace falta leer el resto
            if (t.StartsWith("endsolid", StringComparison.OrdinalIgnoreCase))
                ended = true;
        }

        if (!ended && facets <= MaxTriangles)
            throw new ModelValidationException("El STL ASCII está incompleto: falta \"endsolid\".");
        return facets;
    }

    private static int CheckTriangles(long n) => n switch
    {
        0 => throw new ModelValidationException("El modelo no tiene triángulos."),
        > MaxTriangles => throw new ModelValidationException($"El modelo supera el máximo de {MaxTriangles:N0} triángulos."),
        _ => (int)n,
    };
}
