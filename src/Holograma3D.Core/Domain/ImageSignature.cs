namespace Holograma3D.Core.Domain;

/// <summary>Tipo real de una imagen por su firma de bytes, no por la extensión ni por lo que declare el navegador.</summary>
public static class ImageSignature
{
    /// <returns>"image/jpeg" (FF D8 FF), "image/png" (89 50 4E 47), "image/webp" ("RIFF"…"WEBP") o null.</returns>
    public static string? ContentType(ReadOnlySpan<byte> data)
    {
        if (data is [0xFF, 0xD8, 0xFF, ..])
            return "image/jpeg";
        if (data is [0x89, 0x50, 0x4E, 0x47, ..])
            return "image/png";
        if (data.Length >= 12 && data[..4].SequenceEqual("RIFF"u8) && data[8..12].SequenceEqual("WEBP"u8))
            return "image/webp";
        return null;
    }
}
