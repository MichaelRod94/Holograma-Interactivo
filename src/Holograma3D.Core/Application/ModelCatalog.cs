using Holograma3D.Core.Domain.Models;
using Holograma3D.Core.Ports;

namespace Holograma3D.Core.Application;

/// <summary>Catálogo de modelos de cada usuario.</summary>
public sealed class ModelCatalog(IModelStore store, TimeProvider time)
{
    public const long MaxUploadBytes = 50L * 1024 * 1024;
    public const int MaxNameLength = 80;

    /// <param name="content">Stream con Seek (lo exige el inspector).</param>
    /// <exception cref="ModelValidationException">Archivo demasiado grande o no válido.</exception>
    public async Task<Model3D> UploadAsync(string ownerId, string fileName, string? name, Stream content, long length,
        ModelSource source, CancellationToken ct)
    {
        if (length > MaxUploadBytes || content.Length > MaxUploadBytes)
            throw new ModelValidationException("El archivo supera el máximo de 50 MB.");

        var info = ModelFileInspector.Inspect(content, fileName);
        var finalName = (string.IsNullOrWhiteSpace(name) ? Path.GetFileNameWithoutExtension(fileName) : name).Trim();
        if (finalName.Length == 0)
            finalName = "Modelo";
        if (finalName.Length > MaxNameLength)
            finalName = finalName[..MaxNameLength];

        var model = new Model3D(Guid.NewGuid(), ownerId, finalName, info.Format, content.Length, info.TriangleCount, source, time.GetUtcNow());
        await store.SaveAsync(model, content, ct);
        return model;
    }

    public Task<IReadOnlyList<Model3D>> ListAsync(string ownerId, CancellationToken ct) => store.ListAsync(ownerId, ct);

    public Task<Model3D?> GetAsync(Guid id, string ownerId, CancellationToken ct) => store.GetAsync(id, ownerId, ct);

    public async Task<(Model3D Model, Stream Content)?> OpenFileAsync(Guid id, string ownerId, CancellationToken ct)
    {
        if (await store.GetAsync(id, ownerId, ct) is not { } model)
            return null;
        return await store.OpenReadAsync(id, ownerId, ct) is { } content ? (model, content) : null;
    }

    public Task<bool> DeleteAsync(Guid id, string ownerId, CancellationToken ct) => store.DeleteAsync(id, ownerId, ct);
}
