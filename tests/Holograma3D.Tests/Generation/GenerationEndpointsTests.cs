using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Holograma3D.Tests.Infrastructure;

namespace Holograma3D.Tests.Generation;

[Collection("db")]
public sealed class GenerationEndpointsTests(PostgresFixture db)
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0];

    private static MultipartFormDataContent Photos(int count, byte[]? data = null)
    {
        var form = new MultipartFormDataContent { { new StringContent("Taza"), "name" } };
        for (var i = 0; i < count; i++)
            form.Add(new ByteArrayContent(data ?? Png), "images", $"foto{i}.png");
        return form;
    }

    private static async Task<HttpClient> UserAsync(ApiFactory factory)
    {
        var client = factory.CreateClient();
        await AuthHelper.RegisterAndLoginAsync(client);
        return client;
    }

    [Fact]
    public async Task Con_el_proveedor_falso_la_generacion_termina_en_el_catalogo()
    {
        using var factory = new ApiFactory(db, settings: new Dictionary<string, string>
        {
            ["GenerativeAi:Provider"] = "Fake",
            ["GenerativeAi:PollSeconds"] = "1",
        });
        var client = await UserAsync(factory);

        var provider = await client.GetFromJsonAsync<JsonElement>("/api/generations/provider");
        Assert.True(provider.GetProperty("enabled").GetBoolean());
        Assert.Equal("Fake", provider.GetProperty("provider").GetString());
        Assert.Equal("stl", provider.GetProperty("outputFormat").GetString());

        var created = await client.PostAsync("/api/generations", Photos(2));
        Assert.Equal(HttpStatusCode.Accepted, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();

        JsonElement job = default;
        for (var deadline = DateTime.UtcNow.AddSeconds(15); DateTime.UtcNow < deadline; await Task.Delay(300))
        {
            job = await client.GetFromJsonAsync<JsonElement>($"/api/generations/{id}");
            if (job.GetProperty("status").GetString() is "succeeded" or "failed")
                break;
        }
        Assert.Equal("succeeded", job.GetProperty("status").GetString());

        var model = await client.GetFromJsonAsync<JsonElement>($"/api/models/{job.GetProperty("modelId").GetString()}");
        Assert.Equal("generated", model.GetProperty("source").GetString());
        Assert.Equal("Taza", model.GetProperty("name").GetString());
        Assert.Equal(8, model.GetProperty("triangleCount").GetInt32()); // el octaedro

        var other = await UserAsync(factory);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/generations/{id}")).StatusCode);
        Assert.Empty(await other.GetFromJsonAsync<JsonElement[]>("/api/generations") ?? []);

        var notImage = await client.PostAsync("/api/generations", Photos(1, "hola"u8.ToArray()));
        Assert.Equal(HttpStatusCode.BadRequest, notImage.StatusCode);
    }

    [Fact]
    public async Task Sin_proveedor_responde_503_con_instrucciones()
    {
        using var factory = new ApiFactory(db);
        var client = await UserAsync(factory);

        var provider = await client.GetFromJsonAsync<JsonElement>("/api/generations/provider");
        Assert.False(provider.GetProperty("enabled").GetBoolean());

        var response = await client.PostAsync("/api/generations", Photos(1));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("GenerativeAi__Provider", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetString());
    }

    [Fact]
    public void Meshy_sin_clave_no_arranca()
    {
        using var factory = new ApiFactory(db, settings: new Dictionary<string, string> { ["GenerativeAi:Provider"] = "Meshy" });

        var ex = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("GenerativeAi__ApiKey", ex.ToString());
    }
}
