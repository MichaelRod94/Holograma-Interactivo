using Holograma3D.Core.Domain.Models;

namespace Holograma3D.Core.Ports;

/// <summary>Metadatos y archivos de modelos. Toda operación filtra por dueño: lo ajeno no existe.</summary>
public interface IModelStore
{
    Task SaveAsync(Model3D meta, Stream content, CancellationToken ct);
    Task<IReadOnlyList<Model3D>> ListAsync(string ownerId, CancellationToken ct);
    Task<Model3D?> GetAsync(Guid id, string ownerId, CancellationToken ct);
    Task<Stream?> OpenReadAsync(Guid id, string ownerId, CancellationToken ct);
    Task<bool> DeleteAsync(Guid id, string ownerId, CancellationToken ct);
}
