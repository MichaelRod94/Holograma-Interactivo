using SkiaSharp;

namespace Holograma3D.Tests.Reconstruction;

/// <summary>Fotos sintéticas: fondo blanco y lo que dibuje <paramref name="draw"/>.</summary>
internal static class SyntheticPhotos
{
    public static byte[] Png(int width, int height, Action<SKCanvas> draw)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.White);
            draw(canvas);
        }
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>Un rectángulo oscuro centrado, como una caja fotografiada sobre fondo blanco.</summary>
    public static byte[] Box(int width, int height) => Png(width + 120, height + 120, canvas =>
        canvas.DrawRect(60, 60, width, height, new SKPaint { Color = new SKColor(40, 60, 120) }));
}
