using Holograma3D.Core.Domain.Models;

namespace Holograma3D.Api.Contracts;

public sealed record ModelDto(
    Guid Id,
    string Name,
    string Format,
    long SizeBytes,
    int? TriangleCount,
    string Source,
    DateTimeOffset CreatedAt,
    string FileUrl);

public static class ModelMapping
{
    public static ModelDto ToDto(this Model3D m) => new(
        m.Id,
        m.Name,
        m.Format.ToString().ToLowerInvariant(),
        m.SizeBytes,
        m.TriangleCount,
        m.Source.ToString().ToLowerInvariant(),
        m.CreatedAt,
        $"/api/models/{m.Id}/file");
}
