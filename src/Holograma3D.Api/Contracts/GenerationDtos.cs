using Holograma3D.Core.Domain.Generation;
using Holograma3D.Core.Ports;

namespace Holograma3D.Api.Contracts;

public sealed record GenerationDto(Guid Id, string Name, string Provider, string Status, int Progress, string? Error,
    Guid? ModelId, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

/// <summary>Lo que la UI necesita saber del proveedor: nunca la clave ni la BaseUrl.</summary>
public sealed record GenerationProviderDto(bool Enabled, string? Provider, int MaxImages, string OutputFormat);

public static class GenerationMapping
{
    public static GenerationDto ToDto(this GenerationJob j) =>
        new(j.Id, j.Name, j.Provider, j.Status.ToString().ToLowerInvariant(), j.Progress, j.Error, j.ModelId, j.CreatedAt, j.UpdatedAt);

    public static GenerationProviderDto ToDto(this IImageTo3DGenerator g) =>
        new(g.Enabled, g.Enabled ? g.Name : null, g.MaxImages, g.OutputFormat);
}
