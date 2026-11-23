using Holograma3D.Core.Domain;
using Holograma3D.Core.Domain.Models;
using Holograma3D.Core.Domain.Reconstruction;
using Holograma3D.Core.Ports;

namespace Holograma3D.Core.Application;

/// <summary>STL local a partir de 5 fotos: siluetas → volumen visual → Surface Nets → catálogo. Gratis y sin internet.</summary>
public sealed class ReconstructionService(ISilhouetteSegmenter segmenter, ModelCatalog catalog)
{
    public static readonly int[] Resolutions = [96, 128, 160];
    public const int DefaultResolution = 128;
    public const long MaxImageBytes = 10L * 1024 * 1024;
    private const double MinCoverage = 0.01;

    public static string Label(View view) => view switch
    {
        View.Front => "frontal",
        View.Back => "trasera",
        View.Left => "izquierda",
        View.Right => "derecha",
        _ => "superior",
    };

    /// <exception cref="ModelValidationException">Falta una foto, no es imagen, la resolución no vale o no se ve el objeto.</exception>
    public async Task<Model3D> ReconstructAsync(string ownerId, IReadOnlyDictionary<View, byte[]> views, string? name, int resolution,
        CancellationToken ct)
    {
        if (!Resolutions.Contains(resolution))
            throw new ModelValidationException("La resolución debe ser 96, 128 o 160.");
        foreach (var view in Enum.GetValues<View>())
        {
            if (!views.TryGetValue(view, out var data) || data.Length == 0)
                throw new ModelValidationException($"Falta la foto {Label(view)}.");
            if (data.Length > MaxImageBytes)
                throw new ModelValidationException($"La foto {Label(view)} supera 10 MB.");
            if (ImageSignature.ContentType(data) is null)
                throw new ModelValidationException($"La foto {Label(view)} no es JPEG, PNG ni WEBP.");
        }

        var masks = await Task.WhenAll(Enum.GetValues<View>().Select(async view =>
            (View: view, Mask: await segmenter.SegmentAsync(new MemoryStream(views[view]), ct))));
        foreach (var (view, mask) in masks)
            if (mask.Coverage < MinCoverage)
                throw new ModelValidationException($"No se detectó el objeto en la vista {Label(view)}.");

        var occupancy = VisualHull.Occupancy(masks.ToDictionary(m => m.View, m => m.Mask), resolution);
        var mesh = SurfaceNets.Extract(VisualHull.Smooth(occupancy));
        var stl = new MemoryStream(StlWriter.Write(mesh));

        var finalName = string.IsNullOrWhiteSpace(name) ? "Reconstrucción" : name;
        return await catalog.UploadAsync(ownerId, "reconstruccion.stl", finalName, stl, stl.Length, ModelSource.Reconstruction, ct);
    }
}
