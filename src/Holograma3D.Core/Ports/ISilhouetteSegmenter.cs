using Holograma3D.Core.Domain.Reconstruction;

namespace Holograma3D.Core.Ports;

/// <summary>Separa el objeto del fondo en una foto (umbral sobre fondo liso, o U²-Netp con ONNX).</summary>
public interface ISilhouetteSegmenter
{
    /// <summary>La silueta, del tamaño de la imagen ya orientada y reducida.</summary>
    Task<Mask> SegmentAsync(Stream image, CancellationToken ct);
}
