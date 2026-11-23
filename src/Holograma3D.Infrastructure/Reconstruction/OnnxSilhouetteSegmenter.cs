using Holograma3D.Core.Domain.Reconstruction;
using Holograma3D.Core.Ports;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SkiaSharp;

namespace Holograma3D.Infrastructure.Reconstruction;

/// <summary>
/// U²-Netp (u2netp.onnx, Apache-2.0): detecta el objeto saliente aunque el fondo no sea liso.
/// Singleton: la sesión de ONNX Runtime admite llamadas concurrentes.
/// </summary>
public sealed class OnnxSilhouetteSegmenter(string modelPath) : ISilhouetteSegmenter, IDisposable
{
    private const int Size = 320;
    private static readonly float[] Mean = [0.485f, 0.456f, 0.406f];
    private static readonly float[] Std = [0.229f, 0.224f, 0.225f];

    private readonly InferenceSession _session = new(modelPath);

    public Task<Mask> SegmentAsync(Stream image, CancellationToken ct) => Task.Run(() => Segment(image), ct);

    private Mask Segment(Stream image)
    {
        using var bitmap = ImageLoader.Load(image);
        int w = bitmap.Width, h = bitmap.Height;

        // Pre-proceso: 320×320, RGB/255 normalizado con la media y la desviación de ImageNet, NCHW.
        using var input = bitmap.Resize(new SKImageInfo(Size, Size, SKColorType.Rgba8888, SKAlphaType.Premul),
            new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        var pixels = input.Pixels;
        var tensor = new DenseTensor<float>([1, 3, Size, Size]);
        for (var y = 0; y < Size; y++)
            for (var x = 0; x < Size; x++)
            {
                var c = pixels[y * Size + x];
                tensor[0, 0, y, x] = (c.Red / 255f - Mean[0]) / Std[0];
                tensor[0, 1, y, x] = (c.Green / 255f - Mean[1]) / Std[1];
                tensor[0, 2, y, x] = (c.Blue / 255f - Mean[2]) / Std[2];
            }

        using var results = _session.Run([NamedOnnxValue.CreateFromTensor(_session.InputMetadata.Keys.First(), tensor)]);
        var output = results.First().AsTensor<float>(); // 1×1×320×320

        // Post-proceso: min–max a 0..1, de vuelta al tamaño de la foto (vecino más cercano) y umbral 0.5.
        float min = float.MaxValue, max = float.MinValue;
        foreach (var v in output)
            (min, max) = (MathF.Min(min, v), MathF.Max(max, v));
        var range = MathF.Max(max - min, 1e-6f);
        return Mask.From(w, h, (x, y) => (output[0, 0, y * Size / h, x * Size / w] - min) / range > 0.5f);
    }

    public void Dispose() => _session.Dispose();
}
