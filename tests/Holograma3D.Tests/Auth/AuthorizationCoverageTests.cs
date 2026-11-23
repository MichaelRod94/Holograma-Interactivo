using System.Reflection;
using Holograma3D.Api.Realtime;
using Holograma3D.Tests.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Holograma3D.Tests.Auth;

/// <summary>Toda ruta nueva exige token salvo que esté en la lista de anónimos del contrato: así un olvido rompe un test.</summary>
[Collection("db")]
public sealed class AuthorizationCoverageTests(PostgresFixture db)
{
    private static readonly HashSet<string> AnonymousByContract =
    [
        "/health",
        // MapIdentityApi: registro, acceso y recuperación de cuenta.
        "/api/auth/register",
        "/api/auth/login",
        "/api/auth/refresh",
        "/api/auth/confirmEmail",
        "/api/auth/resendConfirmationEmail",
        "/api/auth/forgotPassword",
        "/api/auth/resetPassword",
        // El Display no tiene cuenta: entra con el código de la sesión.
        "/api/sessions/{code}/qr.svg",
        "/api/sessions/{code}/model",
        // El hub acepta conexiones anónimas (el Display); cada método del Control lleva [Authorize] (ver el otro test).
        "/hubs/hologram",
        "/hubs/hologram/negotiate",
        // Solo existe en Development (MapOpenApi).
        "/openapi/{documentName}.json",
    ];

    [Fact]
    public void Solo_las_rutas_del_contrato_son_anonimas()
    {
        using var factory = new ApiFactory(db);
        factory.CreateClient(); // arranca el host: las rutas se mapean al construir la app

        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToList();
        var anonymous = endpoints
            .Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is not null || !e.Metadata.GetOrderedMetadata<IAuthorizeData>().Any())
            .Select(e => e.RoutePattern.RawText ?? "")
            .Distinct()
            .Order()
            .ToList();

        var unexpected = anonymous.Except(AnonymousByContract).ToList();
        Assert.True(unexpected.Count == 0, $"Rutas sin token que no están en el contrato: {string.Join(", ", unexpected)}");
        // Y al revés: la lista no se queda con rutas que ya no existen.
        var stale = AnonymousByContract.Except(endpoints.Select(e => e.RoutePattern.RawText ?? "")).ToList();
        Assert.True(stale.Count == 0, $"La lista de anónimos tiene rutas que ya no existen: {string.Join(", ", stale)}");
        // Las protegidas siguen protegidas.
        Assert.DoesNotContain("/api/models/", anonymous);
        Assert.DoesNotContain("/api/generations/", anonymous);
        Assert.DoesNotContain("/api/reconstructions", anonymous);
    }

    [Fact]
    public void Los_metodos_del_hub_exigen_token_salvo_JoinSession()
    {
        var methods = typeof(HologramHub).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName && m.Name != nameof(HologramHub.OnDisconnectedAsync))
            .ToList();
        Assert.NotEmpty(methods);

        var withoutToken = methods.Where(m => m.GetCustomAttribute<AuthorizeAttribute>() is null).Select(m => m.Name).ToList();

        Assert.Equal([nameof(HologramHub.JoinSession)], withoutToken);
    }
}
