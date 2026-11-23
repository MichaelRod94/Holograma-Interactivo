using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Holograma3D.Core.Domain.Generation;
using Holograma3D.Core.Ports;

namespace Holograma3D.Infrastructure.GenerativeAi;

/// <summary>
/// Meshy Multi-Image to 3D (https://docs.meshy.ai/en/api/multi-image-to-3d, revisada el 23-nov-2026).
/// La clave va solo en las llamadas a <paramref name="api"/>; la descarga usa <paramref name="download"/>,
/// sin Authorization, porque los archivos están en otro host (URLs presignadas).
/// </summary>
// ponytail: un 429 se reintenta en el siguiente ciclo (PollSeconds) sin leer Retry-After; basta con 1 consulta por trabajo cada 5 s.
public sealed class MeshyImageTo3DGenerator(HttpClient api, HttpClient download, GenerativeAiOptions options) : IImageTo3DGenerator
{
    public const string DefaultBaseUrl = "https://api.meshy.ai";
    private const string Tasks = "openapi/v1/multi-image-to-3d";

    public string Name => "Meshy";
    public bool Enabled => true;
    public int MaxImages => Math.Min(options.MaxImages, 4);
    public string OutputFormat => options.OutputFormat;

    public async Task<string> SubmitAsync(IReadOnlyList<GenerationImage> images, CancellationToken ct)
    {
        var body = new Dictionary<string, object>
        {
            ["image_urls"] = images.Select(i => $"data:{i.ContentType};base64,{Convert.ToBase64String(i.Data)}").ToArray(),
            ["ai_model"] = string.IsNullOrWhiteSpace(options.Model) ? "latest" : options.Model,
            ["should_texture"] = options.Texture,
            ["target_formats"] = new[] { options.OutputFormat },
            ["target_polycount"] = options.TargetPolycount,
        };
        using var response = await SendAsync(new HttpRequestMessage(HttpMethod.Post, Tasks) { Content = JsonContent.Create(body) }, ct);
        var created = await response.Content.ReadFromJsonAsync<Created>(ct);
        return created?.Result ?? throw new InvalidOperationException("Meshy no devolvió el id de la tarea.");
    }

    public async Task<ProviderTaskStatus> GetStatusAsync(string providerTaskId, CancellationToken ct)
    {
        var task = await GetTaskAsync(providerTaskId, ct);
        return task.Status switch
        {
            "IN_PROGRESS" => new ProviderTaskStatus(ProviderTaskState.Running, task.Progress),
            "SUCCEEDED" => new ProviderTaskStatus(ProviderTaskState.Succeeded, 100),
            "FAILED" => new ProviderTaskStatus(ProviderTaskState.Failed, task.Progress, task.TaskError?.Message ?? "Meshy no pudo generar el modelo."),
            "CANCELED" => new ProviderTaskStatus(ProviderTaskState.Failed, task.Progress, task.TaskError?.Message ?? "La tarea se canceló en Meshy."),
            _ => new ProviderTaskStatus(ProviderTaskState.Pending), // PENDING o un estado nuevo: se sigue esperando
        };
    }

    public async Task<Stream> DownloadResultAsync(string providerTaskId, CancellationToken ct)
    {
        var task = await GetTaskAsync(providerTaskId, ct);
        if (task.ModelUrls?.GetValueOrDefault(OutputFormat) is not { } url
            || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException($"Meshy no entregó una URL https para el formato {OutputFormat}.");

        var response = await download.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStreamAsync(ct);
    }

    private async Task<TaskDto> GetTaskAsync(string id, CancellationToken ct)
    {
        using var response = await SendAsync(new HttpRequestMessage(HttpMethod.Get, $"{Tasks}/{Uri.EscapeDataString(id)}"), ct);
        return await response.Content.ReadFromJsonAsync<TaskDto>(ct) ?? throw new InvalidOperationException("Respuesta vacía de Meshy.");
    }

    /// <summary>Agrega la clave y convierte un error HTTP en una excepción con el mensaje de Meshy (nunca con la clave).</summary>
    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
        var response = await api.SendAsync(request, ct);
        if (response.IsSuccessStatusCode)
            return response;

        using (response)
        {
            string? message = null;
            try
            {
                message = (await response.Content.ReadFromJsonAsync<ErrorDto>(ct))?.Message;
            }
            catch (JsonException) { /* cuerpo que no es JSON */ }
            throw new HttpRequestException($"Meshy respondió {(int)response.StatusCode}: {message ?? response.ReasonPhrase}", null, response.StatusCode);
        }
    }

    private sealed record Created(string? Result);

    private sealed record ErrorDto(string? Message);

    private sealed record TaskError(string? Message);

    private sealed record TaskDto(
        string? Status,
        int Progress,
        [property: JsonPropertyName("model_urls")] Dictionary<string, string?>? ModelUrls,
        [property: JsonPropertyName("task_error")] TaskError? TaskError);
}
