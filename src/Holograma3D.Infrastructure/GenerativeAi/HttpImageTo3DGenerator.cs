using System.Net.Http.Headers;
using System.Net.Http.Json;
using Holograma3D.Core.Domain.Generation;
using Holograma3D.Core.Ports;

namespace Holograma3D.Infrastructure.GenerativeAi;

/// <summary>
/// Servidor propio (por ejemplo, un wrapper de TripoSR o Hunyuan3D-2) con el contrato de docs/generative-http-contract.md.
/// <paramref name="http"/> trae BaseAddress = BaseUrl con "/" al final.
/// </summary>
public sealed class HttpImageTo3DGenerator(HttpClient http, GenerativeAiOptions options) : IImageTo3DGenerator
{
    public string Name => "Http";
    public bool Enabled => true;
    public int MaxImages => options.MaxImages;
    public string OutputFormat => options.OutputFormat;

    public async Task<string> SubmitAsync(IReadOnlyList<GenerationImage> images, CancellationToken ct)
    {
        using var form = new MultipartFormDataContent();
        foreach (var image in images)
        {
            var part = new ByteArrayContent(image.Data);
            part.Headers.ContentType = new MediaTypeHeaderValue(image.ContentType);
            form.Add(part, "images", image.FileName);
        }
        using var response = await SendAsync(new HttpRequestMessage(HttpMethod.Post, "jobs") { Content = form }, ct);
        return (await response.Content.ReadFromJsonAsync<Created>(ct))?.Id
            ?? throw new InvalidOperationException("El servidor no devolvió el id del trabajo.");
    }

    public async Task<ProviderTaskStatus> GetStatusAsync(string providerTaskId, CancellationToken ct)
    {
        using var response = await SendAsync(new HttpRequestMessage(HttpMethod.Get, $"jobs/{Uri.EscapeDataString(providerTaskId)}"), ct);
        var job = await response.Content.ReadFromJsonAsync<Job>(ct) ?? throw new InvalidOperationException("Respuesta vacía del servidor.");
        return job.Status switch
        {
            "running" => new ProviderTaskStatus(ProviderTaskState.Running, job.Progress),
            "succeeded" => new ProviderTaskStatus(ProviderTaskState.Succeeded, 100),
            "failed" => new ProviderTaskStatus(ProviderTaskState.Failed, job.Progress, job.Error ?? "El servidor no pudo generar el modelo."),
            _ => new ProviderTaskStatus(ProviderTaskState.Pending),
        };
    }

    public async Task<Stream> DownloadResultAsync(string providerTaskId, CancellationToken ct)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"jobs/{Uri.EscapeDataString(providerTaskId)}/result");
        var response = await SendAsync(request, ct, HttpCompletionOption.ResponseHeadersRead);
        return await response.Content.ReadAsStreamAsync(ct);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct,
        HttpCompletionOption completion = HttpCompletionOption.ResponseContentRead)
    {
        if (!string.IsNullOrWhiteSpace(options.ApiKey))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
        var response = await http.SendAsync(request, completion, ct);
        if (response.IsSuccessStatusCode)
            return response;
        response.Dispose();
        throw new HttpRequestException($"El servidor de IA respondió {(int)response.StatusCode} {response.ReasonPhrase}.", null, response.StatusCode);
    }

    private sealed record Created(string? Id);

    private sealed record Job(string? Status, int Progress, string? Error);
}
