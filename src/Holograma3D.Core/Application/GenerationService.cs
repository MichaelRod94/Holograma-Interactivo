using Holograma3D.Core.Domain.Generation;
using Holograma3D.Core.Domain.Models;
using Holograma3D.Core.Ports;

namespace Holograma3D.Core.Application;

public sealed record GenerationSettings(TimeSpan Timeout, int MaxActiveJobsPerUser);

/// <summary>Generación de modelos con IA a partir de fotos: crea las tareas y, en cada ciclo, sigue las activas.</summary>
public sealed class GenerationService(IImageTo3DGenerator generator, IGenerationJobStore jobs, ModelCatalog catalog,
    TimeProvider time, GenerationSettings settings)
{
    public const long MaxImageBytes = 10L * 1024 * 1024;
    private const string DefaultName = "Modelo IA";

    /// <exception cref="GenerationDisabledException">No hay proveedor configurado.</exception>
    /// <exception cref="ModelValidationException">Fotos de más, demasiado grandes o que no son imágenes.</exception>
    /// <exception cref="GenerationLimitException">El usuario ya tiene el máximo de trabajos activos.</exception>
    /// <exception cref="GenerationProviderException">El proveedor rechazó la tarea o no respondió.</exception>
    public async Task<GenerationJob> SubmitAsync(string ownerId, IReadOnlyList<GenerationImage> images, string? name, CancellationToken ct)
    {
        if (!generator.Enabled)
            throw new GenerationDisabledException();
        if (images.Count < 1 || images.Count > generator.MaxImages)
            throw new ModelValidationException($"Sube de 1 a {generator.MaxImages} fotos.");
        foreach (var image in images)
        {
            if (image.Data.Length > MaxImageBytes)
                throw new ModelValidationException($"La foto {image.FileName} supera 10 MB.");
            if (!IsImage(image.Data))
                throw new ModelValidationException($"La foto {image.FileName} no es JPEG, PNG ni WEBP.");
        }
        if (await jobs.CountActiveAsync(ownerId, ct) >= settings.MaxActiveJobsPerUser)
            throw new GenerationLimitException(settings.MaxActiveJobsPerUser);

        string taskId;
        try
        {
            taskId = await generator.SubmitAsync(images, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var message = ex.Message.Length > 200 ? ex.Message[..200] : ex.Message;
            throw new GenerationProviderException($"{generator.Name} no aceptó la solicitud: {message}", ex);
        }

        var job = new GenerationJob(Guid.NewGuid(), ownerId, CleanName(name), generator.Name, taskId, time.GetUtcNow());
        await jobs.AddAsync(job, ct);
        return job;
    }

    public Task<GenerationJob?> GetAsync(Guid id, string ownerId, CancellationToken ct) => jobs.GetAsync(id, ownerId, ct);

    public Task<IReadOnlyList<GenerationJob>> ListAsync(string ownerId, CancellationToken ct) => jobs.ListAsync(ownerId, ct);

    /// <summary>Un ciclo sobre los trabajos activos. Un error de uno no frena a los demás.</summary>
    /// <returns>
    /// Los trabajos que dieron error (red, 429, 5xx…): quedan como estaban y se reintentan en el siguiente ciclo;
    /// el tiempo límite acaba cerrando los que nunca se recuperan.
    /// </returns>
    public async Task<IReadOnlyList<(Guid JobId, Exception Error)>> PollOnceAsync(CancellationToken ct)
    {
        var errors = new List<(Guid, Exception)>();
        foreach (var job in await jobs.ListActiveAsync(ct))
        {
            try
            {
                await PollAsync(job, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                errors.Add((job.Id, ex));
            }
        }
        return errors;
    }

    private async Task PollAsync(GenerationJob job, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        if (job.Provider != generator.Name)
        {
            job.MarkFailed("El proveedor configurado cambió.", now);
            await jobs.UpdateAsync(job, ct);
            return;
        }
        if (now - job.CreatedAt > settings.Timeout)
        {
            job.MarkFailed("Tiempo agotado.", now);
            await jobs.UpdateAsync(job, ct);
            return;
        }

        var status = await generator.GetStatusAsync(job.ProviderTaskId, ct);
        switch (status.State)
        {
            case ProviderTaskState.Running when job.Status == GenerationStatus.Queued || status.Progress != job.Progress:
                job.MarkRunning(status.Progress, now);
                await jobs.UpdateAsync(job, ct);
                break;
            case ProviderTaskState.Failed:
                job.MarkFailed(status.Error ?? "El proveedor no pudo generar el modelo.", now);
                await jobs.UpdateAsync(job, ct);
                break;
            case ProviderTaskState.Succeeded:
                await SaveResultAsync(job, ct);
                break;
        }
    }

    /// <summary>Descarga a un temporal y lo guarda en el catálogo, que lo valida como cualquier subida.</summary>
    private async Task SaveResultAsync(GenerationJob job, CancellationToken ct)
    {
        await using var file = new FileStream(Path.GetTempFileName(), FileMode.Create, FileAccess.ReadWrite, FileShare.None, 81920,
            FileOptions.DeleteOnClose | FileOptions.Asynchronous);
        try
        {
            await using (var result = await generator.DownloadResultAsync(job.ProviderTaskId, ct))
            {
                // Se corta en el máximo + 1 byte: el catálogo rechaza lo que se pase con su propio mensaje.
                var buffer = new byte[81920];
                int read;
                while (file.Length <= ModelCatalog.MaxUploadBytes && (read = await result.ReadAsync(buffer, ct)) > 0)
                    await file.WriteAsync(buffer.AsMemory(0, read), ct);
            }
            file.Position = 0;
            var model = await catalog.UploadAsync(job.OwnerId, $"{job.Name}.{generator.OutputFormat}", job.Name, file, file.Length,
                ModelSource.Generated, ct);
            job.MarkSucceeded(model.Id, time.GetUtcNow());
        }
        catch (ModelValidationException ex)
        {
            job.MarkFailed($"El modelo generado no es válido: {ex.Message}", time.GetUtcNow());
        }
        await jobs.UpdateAsync(job, ct);
    }

    private static string CleanName(string? name)
    {
        var clean = (name ?? "").Trim();
        if (clean.Length == 0)
            return DefaultName;
        return clean.Length > ModelCatalog.MaxNameLength ? clean[..ModelCatalog.MaxNameLength] : clean;
    }

    public static bool IsImage(ReadOnlySpan<byte> data) => ImageType(data) is not null;

    /// <summary>Tipo real por firma de bytes: JPEG (FF D8 FF), PNG (89 50 4E 47) o WEBP ("RIFF"…"WEBP"); null si no es ninguno.</summary>
    public static string? ImageType(ReadOnlySpan<byte> data)
    {
        if (data is [0xFF, 0xD8, 0xFF, ..])
            return "image/jpeg";
        if (data is [0x89, 0x50, 0x4E, 0x47, ..])
            return "image/png";
        if (data.Length >= 12 && data[..4].SequenceEqual("RIFF"u8) && data[8..12].SequenceEqual("WEBP"u8))
            return "image/webp";
        return null;
    }
}
