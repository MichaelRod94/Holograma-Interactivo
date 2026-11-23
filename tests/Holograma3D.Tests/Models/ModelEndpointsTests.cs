using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Holograma3D.Tests.Infrastructure;

namespace Holograma3D.Tests.Models;

[Collection("db")]
public sealed class ModelEndpointsTests(PostgresFixture db) : IDisposable
{
    private readonly ApiFactory _factory = new(db);

    public void Dispose() => _factory.Dispose();

    private static MultipartFormDataContent Upload(byte[] bytes, string fileName, string? name = null)
    {
        var form = new MultipartFormDataContent { { new ByteArrayContent(bytes), "file", fileName } };
        if (name is not null)
            form.Add(new StringContent(name), "name");
        return form;
    }

    private async Task<(HttpClient Client, JsonElement Model, byte[] Bytes)> UserWithModelAsync()
    {
        var client = _factory.CreateClient();
        await AuthHelper.RegisterAndLoginAsync(client);
        var bytes = ModelFiles.BinaryStl(12);
        var response = await client.PostAsync("/api/models", Upload(bytes, "cubo.stl", "  Mi cubo  "));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (client, await response.Content.ReadFromJsonAsync<JsonElement>(), bytes);
    }

    [Fact]
    public async Task Subir_listar_descargar_y_borrar()
    {
        var (client, model, bytes) = await UserWithModelAsync();
        var id = model.GetProperty("id").GetString();

        Assert.Equal("Mi cubo", model.GetProperty("name").GetString());
        Assert.Equal("stl", model.GetProperty("format").GetString());
        Assert.Equal("upload", model.GetProperty("source").GetString());
        Assert.Equal(12, model.GetProperty("triangleCount").GetInt32());
        Assert.Equal($"/api/models/{id}/file", model.GetProperty("fileUrl").GetString());

        var list = await client.GetFromJsonAsync<JsonElement[]>("/api/models");
        Assert.Contains(list!, m => m.GetProperty("id").GetString() == id);

        var file = await client.GetAsync($"/api/models/{id}/file");
        Assert.Equal("model/stl", file.Content.Headers.ContentType?.MediaType);
        Assert.Equal(bytes, await file.Content.ReadAsByteArrayAsync());

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/models/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/models/{id}")).StatusCode);
    }

    [Fact]
    public async Task Otro_usuario_no_ve_ni_borra_el_modelo()
    {
        var (owner, model, _) = await UserWithModelAsync();
        var id = model.GetProperty("id").GetString();
        var other = _factory.CreateClient();
        await AuthHelper.RegisterAndLoginAsync(other);

        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/models/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/models/{id}/file")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"/api/models/{id}")).StatusCode);
        Assert.Empty((await other.GetFromJsonAsync<JsonElement[]>("/api/models"))!);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"/api/models/{id}")).StatusCode);
    }

    [Fact]
    public async Task Un_txt_es_400_con_problem_details()
    {
        var client = _factory.CreateClient();
        await AuthHelper.RegisterAndLoginAsync(client);

        var response = await client.PostAsync("/api/models", Upload("no soy un modelo"u8.ToArray(), "notas.txt"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Archivo no válido", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString());
    }

    [Fact]
    public async Task Sin_token_es_401()
    {
        var client = _factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/models")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/api/models", Upload(ModelFiles.Glb(), "a.glb"))).StatusCode);
    }
}
