namespace Holograma3D.Core.Domain.Models;

public enum ModelFormat { Stl, Glb }

public enum ModelSource { Upload, Generated, Reconstruction }

/// <summary>Metadatos de un modelo 3D de un usuario; el archivo vive aparte, detrás de IModelStore.</summary>
public sealed record Model3D(
    Guid Id,
    string OwnerId,
    string Name,
    ModelFormat Format,
    long SizeBytes,
    int? TriangleCount,
    ModelSource Source,
    DateTimeOffset CreatedAt);
