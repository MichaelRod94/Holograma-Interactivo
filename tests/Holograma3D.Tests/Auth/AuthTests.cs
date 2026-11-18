using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Holograma3D.Infrastructure;
using Holograma3D.Tests.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Holograma3D.Tests.Auth;

[Collection("db")]
public sealed class AuthTests(PostgresFixture db) : IDisposable
{
    private readonly ApiFactory _factory = new(db);

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Register_login_y_me_devuelven_el_email()
    {
        var client = _factory.CreateClient();
        var (email, _, _) = await AuthHelper.RegisterAndLoginAsync(client);

        var me = await client.GetFromJsonAsync<JsonElement>("/api/auth/me");

        Assert.Equal(email, me.GetProperty("email").GetString());
        Assert.False(string.IsNullOrEmpty(me.GetProperty("id").GetString()));
    }

    [Fact]
    public async Task Me_sin_token_es_401()
    {
        var response = await _factory.CreateClient().GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Contrasena_debil_es_400_con_errores()
    {
        var response = await _factory.CreateClient().PostAsJsonAsync("/api/auth/register", new { email = "debil@test.local", password = "abc" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.NotEmpty(body.GetProperty("errors").EnumerateObject());
    }

    [Fact]
    public async Task Refresh_devuelve_tokens_nuevos_que_sirven()
    {
        var client = _factory.CreateClient();
        var (_, access, refresh) = await AuthHelper.RegisterAndLoginAsync(client);

        var response = await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = refresh });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var tokens = (await response.Content.ReadFromJsonAsync<AuthHelper.Tokens>())!;
        Assert.NotEqual(access, tokens.AccessToken);
        Assert.NotEqual(refresh, tokens.RefreshToken);

        var fresh = _factory.CreateClient();
        fresh.DefaultRequestHeaders.Authorization = new("Bearer", tokens.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await fresh.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Logout_all_invalida_el_refresh_anterior()
    {
        var client = _factory.CreateClient();
        var (_, _, refresh) = await AuthHelper.RegisterAndLoginAsync(client);

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/auth/logout-all", null)).StatusCode);

        var response = await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = refresh });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task El_cuarto_login_en_un_minuto_es_429()
    {
        using var factory = new ApiFactory(db, authPerMinute: 3);
        var client = factory.CreateClient();
        var login = new { email = "nadie@test.local", password = "Incorrecta-1" };

        for (var i = 0; i < 3; i++)
            Assert.NotEqual(HttpStatusCode.TooManyRequests, (await client.PostAsJsonAsync("/api/auth/login", login)).StatusCode);

        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.PostAsJsonAsync("/api/auth/login", login)).StatusCode);
    }

    [Fact]
    public async Task El_token_por_query_no_sirve_fuera_de_hubs()
    {
        var (_, access, _) = await AuthHelper.RegisterAndLoginAsync(_factory.CreateClient());

        var response = await _factory.CreateClient().GetAsync($"/api/auth/me?access_token={Uri.EscapeDataString(access)}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}

// Fuera de la colección "db": no necesita Docker.
public sealed class InfrastructureSetupTests
{
    [Fact]
    public void Sin_cadena_de_conexion_falla_con_instrucciones()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddInfrastructure(new ConfigurationBuilder().Build()));

        Assert.Contains("user-secrets", ex.Message);
    }
}
