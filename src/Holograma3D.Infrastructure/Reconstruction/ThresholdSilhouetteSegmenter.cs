using Holograma3D.Core.Domain.Reconstruction;
using Holograma3D.Core.Ports;
using SkiaSharp;

namespace Holograma3D.Infrastructure.Reconstruction;

/// <summary>
/// Sin modelo de IA: sirve con fondo liso y contrastante. Fondo = mediana de un marco de 5 px; cada píxel se compara
/// por distancia de color con un umbral de Otsu; un cierre morfológico 3×3 tapa los huecos pequeños.
/// </summary>
public sealed class ThresholdSilhouetteSegmenter : ISilhouetteSegmenter
{
    private const int Border = 5;
    private const int Bins = 256;
    private const float MaxDistance = 441.68f; // √(3·255²)

    // ponytail: piso fijo para que una foto sin objeto (solo ruido de JPEG) no "encuentre" uno; un fondo con sombras fuertes necesita U²-Netp.
    private const float MinThreshold = 20f;

    public Task<Mask> SegmentAsync(Stream image, CancellationToken ct) => Task.Run(() => Segment(image), ct);

    public static Mask Segment(Stream image)
    {
        using var bitmap = ImageLoader.Load(image);
        int w = bitmap.Width, h = bitmap.Height;
        var pixels = bitmap.Pixels;

        var frame = new List<SKColor>();
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
                if (x < Border || y < Border || x >= w - Border || y >= h - Border)
                    frame.Add(pixels[y * w + x]);
        byte Median(Func<SKColor, byte> channel) => frame.Select(channel).Order().ElementAt(frame.Count / 2);
        var (r0, g0, b0) = (Median(c => c.Red), Median(c => c.Green), Median(c => c.Blue));

        var distance = new float[pixels.Length];
        var histogram = new int[Bins];
        for (var i = 0; i < pixels.Length; i++)
        {
            var c = pixels[i];
            float dr = c.Red - r0, dg = c.Green - g0, db = c.Blue - b0;
            distance[i] = MathF.Sqrt(dr * dr + dg * dg + db * db);
            histogram[Math.Min(Bins - 1, (int)(distance[i] / MaxDistance * Bins))]++;
        }
        var threshold = MathF.Max(MinThreshold, Otsu(histogram, pixels.Length) * MaxDistance / Bins);

        var mask = new Mask(w, h, distance.Select(d => d > threshold).ToArray());
        return Erode(Dilate(mask)); // cierre 3×3
    }

    /// <summary>El bin que maximiza la varianza entre clases.</summary>
    private static int Otsu(int[] histogram, int total)
    {
        double sum = 0;
        for (var i = 0; i < histogram.Length; i++)
            sum += i * histogram[i];
        double sumBackground = 0, best = -1;
        int weightBackground = 0, threshold = 0;
        for (var i = 0; i < histogram.Length; i++)
        {
            weightBackground += histogram[i];
            var weightObject = total - weightBackground;
            if (weightBackground == 0)
                continue;
            if (weightObject == 0)
                break;
            sumBackground += i * histogram[i];
            var meanBackground = sumBackground / weightBackground;
            var meanObject = (sum - sumBackground) / weightObject;
            var between = (double)weightBackground * weightObject * (meanBackground - meanObject) * (meanBackground - meanObject);
            if (between > best)
                (best, threshold) = (between, i);
        }
        return threshold + 1;
    }

    private static Mask Dilate(Mask m) => Mask.From(m.Width, m.Height, (x, y) => Neighborhood(m, x, y).Any(v => v));

    private static Mask Erode(Mask m) => Mask.From(m.Width, m.Height, (x, y) => Neighborhood(m, x, y).All(v => v));

    /// <summary>Los 3×3 alrededor de (x, y); fuera de la imagen cuenta como el propio píxel.</summary>
    private static IEnumerable<bool> Neighborhood(Mask m, int x, int y)
    {
        for (var dy = -1; dy <= 1; dy++)
            for (var dx = -1; dx <= 1; dx++)
            {
                int nx = x + dx, ny = y + dy;
                yield return nx >= 0 && ny >= 0 && nx < m.Width && ny < m.Height ? m[nx, ny] : m[x, y];
            }
    }
}
