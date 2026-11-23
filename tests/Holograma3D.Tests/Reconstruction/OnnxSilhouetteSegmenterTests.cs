using Holograma3D.Core.Domain.Reconstruction;
using Holograma3D.Infrastructure.Reconstruction;
using SkiaSharp;
using Xunit.Abstractions;

namespace Holograma3D.Tests.Reconstruction;

/// <summary>Se omite si no está src/Holograma3D.Api/models/u2netp.onnx (no va en el repositorio; ver README).</summary>
public sealed class RequiresU2NetpFactAttribute : FactAttribute
{
    public static string? ModelPath { get; } = Find();

    public RequiresU2NetpFactAttribute()
    {
        if (ModelPath is null)
            Skip = "Falta src/Holograma3D.Api/models/u2netp.onnx (ver README).";
    }

    private static string? Find()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, "src", "Holograma3D.Api", "models", "u2netp.onnx");
            if (File.Exists(path))
                return path;
        }
        return null;
    }
}

public sealed class OnnxSilhouetteSegmenterTests(ITestOutputHelper output)
{
    private static double IoU(Mask mask, Func<int, int, bool> truth)
    {
        int intersection = 0, union = 0;
        for (var y = 0; y < mask.Height; y++)
            for (var x = 0; x < mask.Width; x++)
            {
                var t = truth(x, y);
                intersection += t && mask[x, y] ? 1 : 0;
                union += t || mask[x, y] ? 1 : 0;
            }
        return (double)intersection / union;
    }

    [RequiresU2NetpFact]
    public async Task Un_objeto_sobre_fondo_con_degradado_se_segmenta()
    {
        const int w = 480, h = 360;
        var bottle = new SKRect(190, 90, 290, 320);
        // Fondo con degradado y vetas: comprueba que el modelo carga, corre y aísla el objeto. (Con este contraste el umbral
        // también acierta; la ventaja de U²-Netp aparece con fotos reales: sombras, reflejos y fondos con objetos.)
        var png = SyntheticPhotos.Png(w, h, canvas =>
        {
            using var gradient = new SKPaint
            {
                Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(w, h),
                    [new SKColor(235, 225, 205), new SKColor(150, 140, 120)], SKShaderTileMode.Clamp),
            };
            canvas.DrawRect(0, 0, w, h, gradient);
            using var grain = new SKPaint { Color = new SKColor(120, 110, 95, 90), StrokeWidth = 2 };
            for (var x = -h; x < w; x += 14)
                canvas.DrawLine(x, 0, x + h, h, grain);
            using var body = new SKPaint { Color = new SKColor(30, 70, 150), IsAntialias = true };
            canvas.DrawRoundRect(bottle, 18, 18, body);
        });

        using var segmenter = new OnnxSilhouetteSegmenter(RequiresU2NetpFactAttribute.ModelPath!);
        var mask = await segmenter.SegmentAsync(new MemoryStream(png), default);

        Assert.Equal((w, h), (mask.Width, mask.Height));
        bool Truth(int x, int y) => bottle.Contains(x + 0.5f, y + 0.5f);
        var iou = IoU(mask, Truth);
        var thresholdIou = IoU(ThresholdSilhouetteSegmenter.Segment(new MemoryStream(png)), Truth);
        output.WriteLine($"IoU U²-Netp = {iou:F3}; IoU umbral = {thresholdIou:F3}");
        Assert.True(iou > 0.85, $"IoU = {iou:F3}");
    }
}
