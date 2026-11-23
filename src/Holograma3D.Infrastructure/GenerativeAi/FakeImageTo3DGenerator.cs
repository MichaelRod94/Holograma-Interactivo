using System.Collections.Concurrent;
using System.Numerics;
using Holograma3D.Core.Domain.Generation;
using Holograma3D.Core.Ports;

namespace Holograma3D.Infrastructure.GenerativeAi;

/// <summary>
/// Sin red ni créditos, para desarrollar la UI y para los tests: progreso 33, 66 y luego un octaedro en STL.
/// Singleton: recuerda cuántas veces se consultó cada tarea.
/// </summary>
public sealed class FakeImageTo3DGenerator(GenerativeAiOptions options) : IImageTo3DGenerator
{
    private readonly ConcurrentDictionary<string, int> _polls = new();

    public string Name => "Fake";
    public bool Enabled => true;
    public int MaxImages => options.MaxImages;
    public string OutputFormat => "stl";

    public Task<string> SubmitAsync(IReadOnlyList<GenerationImage> images, CancellationToken ct) =>
        Task.FromResult(Guid.NewGuid().ToString("N"));

    public Task<ProviderTaskStatus> GetStatusAsync(string providerTaskId, CancellationToken ct) =>
        Task.FromResult(_polls.AddOrUpdate(providerTaskId, 1, (_, n) => n + 1) switch
        {
            1 => new ProviderTaskStatus(ProviderTaskState.Running, 33),
            2 => new ProviderTaskStatus(ProviderTaskState.Running, 66),
            _ => new ProviderTaskStatus(ProviderTaskState.Succeeded, 100),
        });

    public Task<Stream> DownloadResultAsync(string providerTaskId, CancellationToken ct)
    {
        _polls.TryRemove(providerTaskId, out _);
        return Task.FromResult<Stream>(new MemoryStream(Octahedron()));
    }

    /// <summary>STL binario: 8 caras triangulares con vértices en ±1 sobre cada eje.</summary>
    public static byte[] Octahedron()
    {
        Vector3[] v = [Vector3.UnitX, -Vector3.UnitX, Vector3.UnitY, -Vector3.UnitY, Vector3.UnitZ, -Vector3.UnitZ];
        (int, int, int)[] faces = [(0, 2, 4), (2, 1, 4), (1, 3, 4), (3, 0, 4), (2, 0, 5), (1, 2, 5), (3, 1, 5), (0, 3, 5)];
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(new byte[80]);
        writer.Write((uint)faces.Length);
        foreach (var (a, b, c) in faces)
        {
            var normal = Vector3.Normalize(Vector3.Cross(v[b] - v[a], v[c] - v[a]));
            foreach (var p in (Vector3[])[normal, v[a], v[b], v[c]])
            {
                writer.Write(p.X);
                writer.Write(p.Y);
                writer.Write(p.Z);
            }
            writer.Write((ushort)0);
        }
        writer.Flush();
        return stream.ToArray();
    }
}

/// <summary>Sin proveedor configurado: el endpoint responde 503 con instrucciones.</summary>
public sealed class DisabledImageTo3DGenerator : IImageTo3DGenerator
{
    public string Name => "Ninguno";
    public bool Enabled => false;
    public int MaxImages => 0;
    public string OutputFormat => "glb";

    public Task<string> SubmitAsync(IReadOnlyList<GenerationImage> images, CancellationToken ct) => throw new GenerationDisabledException();

    public Task<ProviderTaskStatus> GetStatusAsync(string providerTaskId, CancellationToken ct) => throw new GenerationDisabledException();

    public Task<Stream> DownloadResultAsync(string providerTaskId, CancellationToken ct) => throw new GenerationDisabledException();
}
