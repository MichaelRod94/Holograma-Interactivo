using Holograma3D.Core.Domain.Generation;
using Holograma3D.Core.Ports;
using Holograma3D.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Holograma3D.Infrastructure;

public sealed class EfGenerationJobStore(AppDbContext db) : IGenerationJobStore
{
    private IQueryable<GenerationJob> Active => db.Set<GenerationJob>()
        .Where(j => j.Status == GenerationStatus.Queued || j.Status == GenerationStatus.Running);

    public async Task AddAsync(GenerationJob job, CancellationToken ct)
    {
        db.Add(job);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(GenerationJob job, CancellationToken ct)
    {
        if (db.Entry(job).State == EntityState.Detached)
            db.Update(job);
        await db.SaveChangesAsync(ct);
    }

    public Task<GenerationJob?> GetAsync(Guid id, string ownerId, CancellationToken ct) =>
        db.Set<GenerationJob>().AsNoTracking().FirstOrDefaultAsync(j => j.Id == id && j.OwnerId == ownerId, ct);

    public async Task<IReadOnlyList<GenerationJob>> ListAsync(string ownerId, CancellationToken ct) =>
        await db.Set<GenerationJob>().AsNoTracking()
            .Where(j => j.OwnerId == ownerId)
            .OrderByDescending(j => j.CreatedAt)
            .ToListAsync(ct);

    // Con seguimiento: el ciclo de consulta los modifica y los guarda.
    public async Task<IReadOnlyList<GenerationJob>> ListActiveAsync(CancellationToken ct) => await Active.ToListAsync(ct);

    public Task<int> CountActiveAsync(string ownerId, CancellationToken ct) => Active.CountAsync(j => j.OwnerId == ownerId, ct);
}
