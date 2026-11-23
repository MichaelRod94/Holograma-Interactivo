using Holograma3D.Core.Domain.Generation;

namespace Holograma3D.Core.Ports;

/// <summary>Un modelo de IA generativa imagen → 3D (Meshy, un servidor propio o el falso de desarrollo).</summary>
public interface IImageTo3DGenerator
{
    /// <summary>Se guarda en cada trabajo: si cambia la configuración, los trabajos viejos se cierran.</summary>
    string Name { get; }

    bool Enabled { get; }

    int MaxImages { get; }

    /// <summary>"glb" o "stl".</summary>
    string OutputFormat { get; }

    /// <returns>Id de la tarea en el proveedor.</returns>
    Task<string> SubmitAsync(IReadOnlyList<GenerationImage> images, CancellationToken ct);

    Task<ProviderTaskStatus> GetStatusAsync(string providerTaskId, CancellationToken ct);

    /// <summary>El archivo resultante, en OutputFormat.</summary>
    Task<Stream> DownloadResultAsync(string providerTaskId, CancellationToken ct);
}
