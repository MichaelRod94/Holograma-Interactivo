using Holograma3D.Core.Domain.Models;
using Holograma3D.Core.Ports;
using Holograma3D.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Holograma3D.Infrastructure;

/// <summary>Fila en PostgreSQL y archivo en {modelsDir}/{id}.stl|.glb. Las rutas solo usan el Guid, nunca el nombre del usuario.</summary>
// ponytail: archivos en disco local; Blob Storage detrás del mismo puerto si se despliega en varios servidores.
// Al borrar un usuario, la cascada borra sus filas pero no sus archivos.
public sealed class EfModelStore(AppDbContext db, string modelsDir, ILogger<EfModelStore> log) : IModelStore
{
    public async Task SaveAsync(Model3D meta, Stream content, CancellationToken ct)
    {
        Directory.CreateDirectory(modelsDir);
        var path = PathOf(meta);
        var tmp = path + ".tmp";
        try
        {
            await using (var file = File.Create(tmp))
                await content.CopyToAsync(file, ct);
            File.Move(tmp, path, overwrite: true);

            db.Set<Model3D>().Add(meta);
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            File.Delete(tmp);
            File.Delete(path);
            throw;
        }
    }

    public async Task<IReadOnlyList<Model3D>> ListAsync(string ownerId, CancellationToken ct) =>
        await db.Set<Model3D>().AsNoTracking()
            .Where(m => m.OwnerId == ownerId)
            .OrderByDescending(m => m.CreatedAt)
            .ToListAsync(ct);

    public Task<Model3D?> GetAsync(Guid id, string ownerId, CancellationToken ct) =>
        db.Set<Model3D>().AsNoTracking().FirstOrDefaultAsync(m => m.Id == id && m.OwnerId == ownerId, ct);

    public async Task<Stream?> OpenReadAsync(Guid id, string ownerId, CancellationToken ct)
    {
        if (await GetAsync(id, ownerId, ct) is not { } meta)
            return null;
        var path = PathOf(meta);
        if (File.Exists(path))
            return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);

        log.LogWarning("Falta el archivo del modelo {ModelId} en {Path}", id, path);
        return null;
    }

    public async Task<bool> DeleteAsync(Guid id, string ownerId, CancellationToken ct)
    {
        var meta = await db.Set<Model3D>().FirstOrDefaultAsync(m => m.Id == id && m.OwnerId == ownerId, ct);
        if (meta is null)
            return false;

        db.Remove(meta);
        await db.SaveChangesAsync(ct);

        var path = PathOf(meta);
        if (File.Exists(path))
            File.Delete(path);
        else
            log.LogWarning("Se borró el modelo {ModelId}, pero su archivo no estaba en {Path}", id, path);
        return true;
    }

    private string PathOf(Model3D m) => Path.Combine(modelsDir, $"{m.Id}.{m.Format.ToString().ToLowerInvariant()}");
}
