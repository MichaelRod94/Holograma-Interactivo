using System.Security.Claims;
using Holograma3D.Api.Contracts;
using Holograma3D.Core.Application;
using Holograma3D.Core.Domain.Models;
using Holograma3D.Core.Domain.Reconstruction;
using Holograma3D.Core.Ports;
using Holograma3D.Infrastructure.Reconstruction;
using Microsoft.AspNetCore.Mvc;

namespace Holograma3D.Api.Endpoints;

public static class ReconstructionEndpoints
{
    private const long MaxRequestBytes = 5 * ReconstructionService.MaxImageBytes + 1024 * 1024;

    /// <summary>
    /// U²-Netp si existe el archivo del modelo ("Reconstruction:ModelPath", por defecto models/u2netp.onnx junto a la Api);
    /// si no, el segmentador por umbral, que pide fondo liso.
    /// </summary>
    // ponytail: selección por existencia del archivo; reinicia la Api después de copiar el modelo.
    public static IServiceCollection AddReconstruction(this IServiceCollection services, IConfiguration configuration, string contentRoot)
    {
        var modelPath = Path.GetFullPath(configuration["Reconstruction:ModelPath"] ?? "models/u2netp.onnx", contentRoot);
        if (File.Exists(modelPath))
            services.AddSingleton<ISilhouetteSegmenter>(_ => new OnnxSilhouetteSegmenter(modelPath));
        else
            services.AddSingleton<ISilhouetteSegmenter, ThresholdSilhouetteSegmenter>();
        services.AddScoped<ReconstructionService>();
        return services;
    }

    public static WebApplication MapReconstructionEndpoints(this WebApplication app)
    {
        var segmenter = app.Services.GetRequiredService<ISilhouetteSegmenter>();
        if (segmenter is OnnxSilhouetteSegmenter)
            app.Logger.LogInformation("Siluetas de \"Crear desde 5 fotos\": U²-Netp (ONNX)");
        else
            app.Logger.LogWarning("Siluetas de \"Crear desde 5 fotos\": por umbral (no se encontró u2netp.onnx; ver README). Usa fondo liso.");

        app.MapPost("/api/reconstructions", async (HttpRequest request, ClaimsPrincipal user, ReconstructionService service, CancellationToken ct) =>
            {
                var form = await request.ReadFormAsync(ct);
                var views = new Dictionary<View, byte[]>();
                foreach (var view in Enum.GetValues<View>())
                {
                    if (form.Files.GetFile(view.ToString().ToLowerInvariant()) is not { } file)
                        continue;
                    if (file.Length > ReconstructionService.MaxImageBytes)
                        return Invalid($"La foto {ReconstructionService.Label(view)} supera 10 MB.");
                    using var copy = new MemoryStream();
                    await file.CopyToAsync(copy, ct);
                    views[view] = copy.ToArray();
                }
                var resolution = int.TryParse(form["resolution"], out var r) ? r : ReconstructionService.DefaultResolution;

                try
                {
                    var model = await service.ReconstructAsync(ModelEndpoints.OwnerId(user), views, form["name"], resolution, ct);
                    return Results.Created($"/api/models/{model.Id}", model.ToDto());
                }
                catch (ModelValidationException ex)
                {
                    return Invalid(ex.Message);
                }
            })
            .RequireAuthorization()
            .DisableAntiforgery() // la Api usa bearer, no cookies
            .WithMetadata(new RequestSizeLimitAttribute(MaxRequestBytes))
            .WithFormOptions(multipartBodyLengthLimit: MaxRequestBytes);
        return app;
    }

    private static IResult Invalid(string detail) =>
        Results.Problem(title: "Fotos no válidas", detail: detail, statusCode: StatusCodes.Status400BadRequest);
}
