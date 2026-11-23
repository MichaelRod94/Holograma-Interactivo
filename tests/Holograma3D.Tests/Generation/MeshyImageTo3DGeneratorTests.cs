using System.Net;
using System.Text;
using System.Text.Json;
using Holograma3D.Core.Domain.Generation;
using Holograma3D.Infrastructure.GenerativeAi;

namespace Holograma3D.Tests.Generation;

public sealed class MeshyImageTo3DGeneratorTests
{
    private const string Key = "msy-clave-de-prueba";
    private static readonly GenerativeAiOptions Options = new() { Provider = "Meshy", ApiKey = Key, OutputFormat = "glb" };

    private readonly FakeHandler _api = new(), _download = new();
    private readonly MeshyImageTo3DGenerator _meshy;

    public MeshyImageTo3DGeneratorTests() =>
        _meshy = new MeshyImageTo3DGenerator(
            new HttpClient(_api) { BaseAddress = new Uri(MeshyImageTo3DGenerator.DefaultBaseUrl + "/") },
            new HttpClient(_download),
            Options);

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task Crea_la_tarea_con_data_uris_sin_textura_y_con_la_clave()
    {
        _api.Respond = _ => Json("""{ "result": "tarea-1" }""");

        var id = await _meshy.SubmitAsync([new GenerationImage("a.png", "image/png", [1, 2, 3])], default);

        Assert.Equal("tarea-1", id);
        var (request, body) = Assert.Single(_api.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://api.meshy.ai/openapi/v1/multi-image-to-3d", request.RequestUri!.ToString());
        Assert.Equal($"Bearer {Key}", request.Headers.Authorization?.ToString());
        using var json = JsonDocument.Parse(body!);
        Assert.Equal("data:image/png;base64,AQID", json.RootElement.GetProperty("image_urls")[0].GetString());
        Assert.False(json.RootElement.GetProperty("should_texture").GetBoolean());
        Assert.Equal("glb", Assert.Single(json.RootElement.GetProperty("target_formats").EnumerateArray()).GetString());
        Assert.Equal("latest", json.RootElement.GetProperty("ai_model").GetString());
    }

    [Theory]
    [InlineData("PENDING", ProviderTaskState.Pending)]
    [InlineData("IN_PROGRESS", ProviderTaskState.Running)]
    [InlineData("SUCCEEDED", ProviderTaskState.Succeeded)]
    [InlineData("FAILED", ProviderTaskState.Failed)]
    [InlineData("CANCELED", ProviderTaskState.Failed)]
    public async Task Mapea_los_estados(string status, ProviderTaskState expected)
    {
        _api.Respond = _ => Json($$"""{ "status": "{{status}}", "progress": 40, "task_error": { "message": "sin malla" } }""");

        var result = await _meshy.GetStatusAsync("tarea-1", default);

        Assert.Equal(expected, result.State);
        if (expected == ProviderTaskState.Failed)
            Assert.Equal("sin malla", result.Error);
    }

    [Fact]
    public async Task La_descarga_va_a_otro_host_sin_la_clave()
    {
        _api.Respond = _ => Json("""{ "status": "SUCCEEDED", "progress": 100, "model_urls": { "glb": "https://assets.meshy.ai/m.glb?Expires=1" } }""");
        _download.Respond = _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([9, 8, 7]) };

        await using var stream = await _meshy.DownloadResultAsync("tarea-1", default);
        var copy = new MemoryStream();
        await stream.CopyToAsync(copy);

        Assert.Equal([9, 8, 7], copy.ToArray());
        var (request, _) = Assert.Single(_download.Requests);
        Assert.Equal("assets.meshy.ai", request.RequestUri!.Host);
        Assert.Null(request.Headers.Authorization);
    }

    [Fact]
    public async Task No_descarga_por_http_sin_cifrar()
    {
        _api.Respond = _ => Json("""{ "status": "SUCCEEDED", "model_urls": { "glb": "http://assets.meshy.ai/m.glb" } }""");

        await Assert.ThrowsAsync<InvalidOperationException>(() => _meshy.DownloadResultAsync("tarea-1", default));
        Assert.Empty(_download.Requests);
    }

    [Fact]
    public async Task Un_429_es_un_error_transitorio_sin_la_clave_en_el_mensaje()
    {
        _api.Respond = _ => Json("""{ "message": "Rate limit exceeded" }""", HttpStatusCode.TooManyRequests);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => _meshy.GetStatusAsync("tarea-1", default));

        Assert.Equal(HttpStatusCode.TooManyRequests, ex.StatusCode);
        Assert.Contains("Rate limit exceeded", ex.Message);
        Assert.DoesNotContain(Key, ex.Message);
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; } = _ => new HttpResponseMessage(HttpStatusCode.NotFound);
        public List<(HttpRequestMessage Request, string? Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add((request, request.Content is null ? null : await request.Content.ReadAsStringAsync(ct)));
            return Respond(request);
        }
    }
}
