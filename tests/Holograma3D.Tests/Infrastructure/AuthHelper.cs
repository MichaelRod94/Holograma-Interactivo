using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Holograma3D.Tests.Infrastructure;

public static class AuthHelper
{
    public const string Password = "Prueba-1234";

    public sealed record Tokens(string TokenType, string AccessToken, long ExpiresIn, string RefreshToken);

    /// <summary>Registra un usuario, inicia sesión y deja puesto el header Authorization en el cliente.</summary>
    public static async Task<(string Email, string AccessToken, string RefreshToken)> RegisterAndLoginAsync(HttpClient client, string? email = null)
    {
        email ??= $"u{Guid.NewGuid():N}@test.local";
        (await client.PostAsJsonAsync("/api/auth/register", new { email, password = Password })).EnsureSuccessStatusCode();

        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password = Password });
        login.EnsureSuccessStatusCode();
        var tokens = (await login.Content.ReadFromJsonAsync<Tokens>())!;

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        return (email, tokens.AccessToken, tokens.RefreshToken);
    }
}
