using Holograma3D.Core.Domain.Generation;

namespace Holograma3D.Core.Ports;

public interface IGenerationJobStore
{
    Task AddAsync(GenerationJob job, CancellationToken ct);

    Task UpdateAsync(GenerationJob job, CancellationToken ct);

    Task<GenerationJob?> GetAsync(Guid id, string ownerId, CancellationToken ct);

    /// <summary>Del usuario, la más reciente primero.</summary>
    Task<IReadOnlyList<GenerationJob>> ListAsync(string ownerId, CancellationToken ct);

    /// <summary>Queued o Running, de todos los usuarios.</summary>
    Task<IReadOnlyList<GenerationJob>> ListActiveAsync(CancellationToken ct);

    Task<int> CountActiveAsync(string ownerId, CancellationToken ct);
}
