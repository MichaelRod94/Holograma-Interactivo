using Holograma3D.Core.Domain.Models;
using SkiaSharp;

namespace Holograma3D.Infrastructure.Reconstruction;

public static class ImageLoader
{
    public const int MaxSide = 1024;

    /// <summary>Decodifica JPG, PNG o WEBP, la endereza según su EXIF (las fotos del móvil vienen giradas) y reduce el lado mayor a 1024 px.</summary>
    /// <exception cref="ModelValidationException">No es una imagen que se pueda leer.</exception>
    public static SKBitmap Load(Stream stream, int maxSide = MaxSide)
    {
        using var codec = SKCodec.Create(stream) ?? throw new ModelValidationException("No se pudo leer la imagen.");
        using var decoded = SKBitmap.Decode(codec) ?? throw new ModelValidationException("No se pudo leer la imagen.");
        using var oriented = Orient(decoded, codec.EncodedOrigin);

        var scale = Math.Min(1.0, (double)maxSide / Math.Max(oriented.Width, oriented.Height));
        var info = new SKImageInfo(Math.Max(1, (int)Math.Round(oriented.Width * scale)), Math.Max(1, (int)Math.Round(oriented.Height * scale)),
            SKColorType.Rgba8888, SKAlphaType.Premul);
        return oriented.Resize(info, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear))
            ?? throw new ModelValidationException("No se pudo reducir la imagen.");
    }

    /// <summary>Aplica la orientación EXIF: cada caso es una matriz que lleva el píxel (x, y) del archivo a la imagen derecha.</summary>
    private static SKBitmap Orient(SKBitmap source, SKEncodedOrigin origin)
    {
        float w = source.Width, h = source.Height;
        var swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        // (x', y') = (a·x + b·y + c, d·x + e·y + f)
        var (a, b, c, d, e, f) = origin switch
        {
            SKEncodedOrigin.TopRight => (-1f, 0f, w, 0f, 1f, 0f),       // espejo horizontal
            SKEncodedOrigin.BottomRight => (-1f, 0f, w, 0f, -1f, h),    // 180°
            SKEncodedOrigin.BottomLeft => (1f, 0f, 0f, 0f, -1f, h),     // espejo vertical
            SKEncodedOrigin.LeftTop => (0f, 1f, 0f, 1f, 0f, 0f),        // transpuesta
            SKEncodedOrigin.RightTop => (0f, -1f, h, 1f, 0f, 0f),       // 90° horario (retrato típico del móvil)
            SKEncodedOrigin.RightBottom => (0f, -1f, h, -1f, 0f, w),    // transversa
            SKEncodedOrigin.LeftBottom => (0f, 1f, 0f, -1f, 0f, w),     // 90° antihorario
            _ => (1f, 0f, 0f, 0f, 1f, 0f),
        };
        var result = new SKBitmap(swap ? source.Height : source.Width, swap ? source.Width : source.Height);
        using var canvas = new SKCanvas(result);
        canvas.SetMatrix(new SKMatrix(a, b, c, d, e, f, 0, 0, 1));
        canvas.DrawBitmap(source, 0, 0);
        return result;
    }
}
