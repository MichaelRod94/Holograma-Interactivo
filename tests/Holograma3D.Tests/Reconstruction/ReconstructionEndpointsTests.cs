using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Holograma3D.Tests.Infrastructure;

namespace Holograma3D.Tests.Reconstruction;

[Collection("db")]
public sealed class ReconstructionEndpointsTests(PostgresFixture db) : IDisposable
{
    private readonly ApiFactory _factory = new(db);

    public void Dispose() => _factory.Dispose();

    /// <summary>Una caja de 200 × 150 × 100: frente y atrás muestran ancho × alto; los lados, fondo × alto; arriba, ancho × fondo.</summary>
    private static MultipartFormDataContent BoxPhotos(int resolution = 128)
    {
        var form = new MultipartFormDataContent
        {
            { new StringContent("Caja"), "name" },
            { new StringContent(resolution.ToString()), "resolution" },
        };
        foreach (var (field, (w, h)) in new Dictionary<string, (int, int)>
                 {
                     ["front"] = (200, 150), ["back"] = (200, 150), ["left"] = (100, 150), ["right"] = (100, 150), ["top"] = (200, 100),
                 })
            form.Add(new ByteArrayContent(SyntheticPhotos.Box(w, h)), field, $"{field}.png");
        return form;
    }

    [Fact]
    public async Task Sin_token_es_401()
    {
        var response = await _factory.CreateClient().PostAsync("/api/reconstructions", BoxPhotos());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Cinco_fotos_de_una_caja_dan_un_stl_en_el_catalogo()
    {
        var client = _factory.CreateClient();
        await AuthHelper.RegisterAndLoginAsync(client);

        var watch = Stopwatch.StartNew();
        var response = await client.PostAsync("/api/reconstructions", BoxPhotos());
        watch.Stop();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var model = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("reconstruction", model.GetProperty("source").GetString());
        Assert.Equal("stl", model.GetProperty("format").GetString());
        Assert.Equal("Caja", model.GetProperty("name").GetString());
        Assert.True(model.GetProperty("triangleCount").GetInt32() > 100);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), $"tardó {watch.Elapsed.TotalSeconds:F1} s");
    }

    [Fact]
    public async Task Falta_una_foto_o_no_se_ve_el_objeto_es_400()
    {
        var client = _factory.CreateClient();
        await AuthHelper.RegisterAndLoginAsync(client);

        var missing = new MultipartFormDataContent { { new ByteArrayContent(SyntheticPhotos.Box(200, 150)), "front", "front.png" } };
        var response = await client.PostAsync("/api/reconstructions", missing);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Falta la foto", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetString());

        // La foto superior no tiene objeto: solo fondo blanco.
        var empty = new MultipartFormDataContent();
        foreach (var field in new[] { "front", "back", "left", "right" })
            empty.Add(new ByteArrayContent(SyntheticPhotos.Box(200, 150)), field, $"{field}.png");
        empty.Add(new ByteArrayContent(SyntheticPhotos.Png(300, 200, _ => { })), "top", "top.png");
        response = await client.PostAsync("/api/reconstructions", empty);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("vista superior", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetString());
    }
}
