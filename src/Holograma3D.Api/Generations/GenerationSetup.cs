using System.Security.Claims;
using Holograma3D.Api.Contracts;
using Holograma3D.Api.Endpoints;
using Holograma3D.Core.Application;
using Holograma3D.Core.Domain;
using Holograma3D.Core.Domain.Generation;
using Holograma3D.Core.Domain.Models;
using Holograma3D.Core.Ports;
using Holograma3D.Infrastructure.GenerativeAi;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Holograma3D.Api.Generations;

public static class GenerationSetup
{
    private const long MaxRequestBytes = 45L * 1024 * 1024;

    public static IServiceCollection AddGenerations(this IServiceCollection services, IConfiguration configuration)
    {
        // Una configuración inválida (p. ej. Meshy sin clave) impide arrancar, con el motivo en el error.
        services.AddOptions<GenerativeAiOptions>().Bind(configuration.GetSection(GenerativeAiOptions.Section)).ValidateOnStart();
        services.AddSingleton<IValidateOptions<GenerativeAiOptions>, GenerativeAiOptionsValidator>();
        services.AddHttpClient("genai", c => c.Timeout = TimeSpan.FromSeconds(100));
        services.AddHttpClient("genai-download", c => c.Timeout = TimeSpan.FromSeconds(100));

        static GenerativeAiOptions Options(IServiceProvider sp) => sp.GetRequiredService<IOptions<GenerativeAiOptions>>().Value;
        static HttpClient Client(IServiceProvider sp, string name, string baseUrl)
        {
            var client = sp.GetRequiredService<IHttpClientFactory>().CreateClient(name);
            client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
            return client;
        }

        switch (configuration[$"{GenerativeAiOptions.Section}:Provider"]?.Trim().ToLowerInvariant())
        {
            case "meshy":
                services.AddScoped<IImageTo3DGenerator>(sp =>
                {
                    var o = Options(sp);
                    return new MeshyImageTo3DGenerator(
                        Client(sp, "genai", string.IsNullOrWhiteSpace(o.BaseUrl) ? MeshyImageTo3DGenerator.DefaultBaseUrl : o.BaseUrl),
                        sp.GetRequiredService<IHttpClientFactory>().CreateClient("genai-download"),
                        o);
                });
                break;
            case "http":
                services.AddScoped<IImageTo3DGenerator>(sp => new HttpImageTo3DGenerator(Client(sp, "genai", Options(sp).BaseUrl!), Options(sp)));
                break;
            case "fake":
                services.AddSingleton<IImageTo3DGenerator>(sp => new FakeImageTo3DGenerator(Options(sp)));
                break;
            default:
                services.AddSingleton<IImageTo3DGenerator, DisabledImageTo3DGenerator>();
                break;
        }

        services.AddSingleton(sp => new GenerationSettings(
            TimeSpan.FromMinutes(Options(sp).TimeoutMinutes), Options(sp).MaxActiveJobsPerUser));
        services.AddScoped<GenerationService>();
        services.AddHostedService<GenerationPoller>();
        return services;
    }

    public static IEndpointRouteBuilder MapGenerationEndpoints(this IEndpointRouteBuilder app)
    {
        var generations = app.MapGroup("/api/generations").RequireAuthorization();

        generations.MapGet("/provider", (IImageTo3DGenerator generator) => generator.ToDto());

        generations.MapPost("/", async (HttpRequest request, ClaimsPrincipal user, GenerationService service, CancellationToken ct) =>
            {
                var form = await request.ReadFormAsync(ct);
                var images = new List<GenerationImage>();
                foreach (var file in form.Files.GetFiles("images"))
                {
                    if (file.Length > GenerationService.MaxImageBytes)
                        return Problem(StatusCodes.Status400BadRequest, "Fotos no válidas", $"La foto {file.FileName} supera 10 MB.");
                    using var copy = new MemoryStream();
                    await file.CopyToAsync(copy, ct);
                    var data = copy.ToArray();
                    // El tipo real sale de los bytes, no de lo que declare el navegador.
                    images.Add(new GenerationImage(file.FileName, ImageSignature.ContentType(data) ?? file.ContentType, data));
                }

                try
                {
                    var job = await service.SubmitAsync(ModelEndpoints.OwnerId(user), images, form["name"], ct);
                    return Results.Accepted($"/api/generations/{job.Id}", job.ToDto());
                }
                catch (ModelValidationException ex)
                {
                    return Problem(StatusCodes.Status400BadRequest, "Fotos no válidas", ex.Message);
                }
                catch (GenerationLimitException ex)
                {
                    return Problem(StatusCodes.Status429TooManyRequests, "Demasiadas generaciones en curso", ex.Message);
                }
                catch (GenerationProviderException ex)
                {
                    return Problem(StatusCodes.Status502BadGateway, "El proveedor de IA falló", ex.Message);
                }
                catch (GenerationDisabledException ex)
                {
                    return Problem(StatusCodes.Status503ServiceUnavailable, "IA generativa no configurada", ex.Message);
                }
            })
            .DisableAntiforgery() // la Api usa bearer, no cookies
            .WithMetadata(new RequestSizeLimitAttribute(MaxRequestBytes))
            .WithFormOptions(multipartBodyLengthLimit: MaxRequestBytes);

        generations.MapGet("/", async (ClaimsPrincipal user, GenerationService service, CancellationToken ct) =>
            (await service.ListAsync(ModelEndpoints.OwnerId(user), ct)).Select(j => j.ToDto()));

        generations.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal user, GenerationService service, CancellationToken ct) =>
            await service.GetAsync(id, ModelEndpoints.OwnerId(user), ct) is { } job ? Results.Ok(job.ToDto()) : Results.NotFound());

        return app;
    }

    private static IResult Problem(int status, string title, string detail) =>
        Results.Problem(title: title, detail: detail, statusCode: status);
}

public sealed class GenerativeAiOptionsValidator : IValidateOptions<GenerativeAiOptions>
{
    public ValidateOptionsResult Validate(string? name, GenerativeAiOptions options) =>
        options.Problem() is { } problem ? ValidateOptionsResult.Fail(problem) : ValidateOptionsResult.Success;
}
