using System.Security.Claims;
using Holograma3D.Api.Contracts;
using Holograma3D.Core.Application;
using Holograma3D.Core.Domain.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Holograma3D.Api.Endpoints;

public static class ModelEndpoints
{
    // El multipart agrega unos KB al archivo; por encima de esto Kestrel responde 413 sin leer el cuerpo.
    private const long MaxRequestBytes = ModelCatalog.MaxUploadBytes + 1024 * 1024;

    public static IServiceCollection AddModels(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<ModelCatalog>();
        return services;
    }

    public static IEndpointRouteBuilder MapModelEndpoints(this IEndpointRouteBuilder app)
    {
        var models = app.MapGroup("/api/models").RequireAuthorization();

        models.MapGet("/", async (ClaimsPrincipal user, ModelCatalog catalog, CancellationToken ct) =>
            (await catalog.ListAsync(OwnerId(user), ct)).Select(m => m.ToDto()));

        models.MapPost("/", async (IFormFile file, [FromForm] string? name, ClaimsPrincipal user, ModelCatalog catalog, CancellationToken ct) =>
            {
                if (file.Length > ModelCatalog.MaxUploadBytes)
                    return Results.Problem(title: "Archivo demasiado grande", detail: "El máximo es 50 MB.", statusCode: StatusCodes.Status413PayloadTooLarge);

                await using var content = await OpenSeekableAsync(file, ct);
                try
                {
                    var model = await catalog.UploadAsync(OwnerId(user), file.FileName, name, content, file.Length, ModelSource.Upload, ct);
                    return Results.Created($"/api/models/{model.Id}", model.ToDto());
                }
                catch (ModelValidationException ex)
                {
                    return Results.Problem(title: "Archivo no válido", detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
                }
            })
            .DisableAntiforgery() // la Api usa bearer, no cookies
            .WithMetadata(new RequestSizeLimitAttribute(MaxRequestBytes))
            .WithFormOptions(multipartBodyLengthLimit: MaxRequestBytes);

        models.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal user, ModelCatalog catalog, CancellationToken ct) =>
            await catalog.GetAsync(id, OwnerId(user), ct) is { } m ? Results.Ok(m.ToDto()) : Results.NotFound());

        models.MapGet("/{id:guid}/file", async (Guid id, ClaimsPrincipal user, ModelCatalog catalog, CancellationToken ct) =>
        {
            var file = await catalog.OpenFileAsync(id, OwnerId(user), ct);
            if (file is null)
                return Results.NotFound();
            var (model, content) = file.Value;
            var (contentType, extension) = model.Format == ModelFormat.Stl ? ("model/stl", ".stl") : ("model/gltf-binary", ".glb");
            return Results.File(content, contentType, model.Name + extension, enableRangeProcessing: true);
        });

        models.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal user, ModelCatalog catalog, CancellationToken ct) =>
            await catalog.DeleteAsync(id, OwnerId(user), ct) ? Results.NoContent() : Results.NotFound());

        return app;
    }

    /// <summary>El dueño sale siempre del token, nunca de lo que mande el cliente.</summary>
    public static string OwnerId(ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new InvalidOperationException("Token sin NameIdentifier.");

    private static async Task<Stream> OpenSeekableAsync(IFormFile file, CancellationToken ct)
    {
        var stream = file.OpenReadStream();
        if (stream.CanSeek)
            return stream;

        // El inspector necesita Seek: copia a un temporal que se borra al cerrarlo.
        await using (stream)
        {
            var tmp = new FileStream(Path.GetTempFileName(), FileMode.Create, FileAccess.ReadWrite, FileShare.None, 81920,
                FileOptions.DeleteOnClose | FileOptions.Asynchronous);
            await stream.CopyToAsync(tmp, ct);
            tmp.Position = 0;
            return tmp;
        }
    }
}
