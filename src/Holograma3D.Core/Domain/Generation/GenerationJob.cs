namespace Holograma3D.Core.Domain.Generation;

public enum GenerationStatus { Queued, Running, Succeeded, Failed }

/// <summary>Un trabajo de IA generativa imagen → 3D. Succeeded y Failed son finales.</summary>
public sealed class GenerationJob(Guid id, string ownerId, string name, string provider, string providerTaskId, DateTimeOffset createdAt)
{
    public Guid Id { get; private set; } = id;
    public string OwnerId { get; private set; } = ownerId;
    public string Name { get; private set; } = name;
    public string Provider { get; private set; } = provider;
    public string ProviderTaskId { get; private set; } = providerTaskId;
    public GenerationStatus Status { get; private set; } = GenerationStatus.Queued;
    public int Progress { get; private set; }
    public string? Error { get; private set; }
    public Guid? ModelId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = createdAt;
    public DateTimeOffset UpdatedAt { get; private set; } = createdAt;

    public bool IsActive => Status is GenerationStatus.Queued or GenerationStatus.Running;

    public void MarkRunning(int progress, DateTimeOffset now)
    {
        EnsureActive();
        Status = GenerationStatus.Running;
        Progress = Math.Clamp(progress, 0, 99); // 100 solo cuando el modelo ya está en el catálogo
        UpdatedAt = now;
    }

    public void MarkSucceeded(Guid modelId, DateTimeOffset now)
    {
        EnsureActive();
        Status = GenerationStatus.Succeeded;
        Progress = 100;
        ModelId = modelId;
        UpdatedAt = now;
    }

    public void MarkFailed(string message, DateTimeOffset now)
    {
        EnsureActive();
        Status = GenerationStatus.Failed;
        Error = message.Length > 500 ? message[..500] : message;
        UpdatedAt = now;
    }

    private void EnsureActive()
    {
        if (!IsActive)
            throw new InvalidOperationException($"La generación {Id} ya terminó ({Status}).");
    }
}

public sealed record GenerationImage(string FileName, string ContentType, byte[] Data);

public enum ProviderTaskState { Pending, Running, Succeeded, Failed }

public sealed record ProviderTaskStatus(ProviderTaskState State, int Progress = 0, string? Error = null);

/// <summary>La IA generativa no está configurada (503).</summary>
public sealed class GenerationDisabledException()
    : Exception("La generación con IA no está configurada. Define GenerativeAi__Provider y GenerativeAi__ApiKey en tu .env.");

/// <summary>El usuario ya tiene el máximo de trabajos activos (429).</summary>
public sealed class GenerationLimitException(int max)
    : Exception($"Ya tienes {max} generaciones en curso; espera a que termine alguna.");

/// <summary>El proveedor rechazó o no respondió al crear la tarea (502).</summary>
public sealed class GenerationProviderException(string message, Exception? inner = null) : Exception(message, inner);
