using Holograma3D.Infrastructure.Reconstruction;
using SkiaSharp;

namespace Holograma3D.Tests.Reconstruction;

public sealed class ThresholdSilhouetteSegmenterTests
{
    [Fact]
    public void Un_circulo_rojo_sobre_fondo_blanco_se_segmenta()
    {
        const int w = 400, h = 300, cx = 200, cy = 150, r = 80;
        var png = SyntheticPhotos.Png(w, h, canvas =>
            canvas.DrawCircle(cx, cy, r, new SKPaint { Color = SKColors.Red, IsAntialias = true }));

        var mask = ThresholdSilhouetteSegmenter.Segment(new MemoryStream(png));

        Assert.Equal((w, h), (mask.Width, mask.Height));
        int intersection = 0, union = 0;
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var truth = Math.Pow(x + 0.5 - cx, 2) + Math.Pow(y + 0.5 - cy, 2) <= r * r;
                intersection += truth && mask[x, y] ? 1 : 0;
                union += truth || mask[x, y] ? 1 : 0;
            }
        Assert.True((double)intersection / union > 0.95, $"IoU = {(double)intersection / union:F3}");
    }

    [Fact]
    public void Una_foto_sin_objeto_no_encuentra_nada()
    {
        var png = SyntheticPhotos.Png(200, 150, _ => { });

        Assert.Equal(0, ThresholdSilhouetteSegmenter.Segment(new MemoryStream(png)).Count);
    }
}
