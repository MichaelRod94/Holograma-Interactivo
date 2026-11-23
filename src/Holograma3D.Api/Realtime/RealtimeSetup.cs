using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.RateLimiting;
using Holograma3D.Api.Endpoints;
using Holograma3D.Core.Application;
using Holograma3D.Core.Domain.Gestures;
using Holograma3D.Core.Domain.Sessions;
using Holograma3D.Core.Ports;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection.Extensions;
using QRCoder;

namespace Holograma3D.Api.Realtime;

/// <summary>URL del Display para una sesión: la del QR y la que el Control muestra para copiar.</summary>
public sealed record DisplayLink(string BaseUrl)
{
    public string For(string code) => $"{BaseUrl}/?s={code}";
}

public static class RealtimeSetup
{
    public static IServiceCollection AddRealtime(this IServiceCollection services, IConfiguration configuration)
    {
        // Se lee una vez al arrancar; cada sesión crea su intérprete con estos valores.
        services.AddSingleton(configuration.GetSection("Gestures").Get<GestureOptions>() ?? new GestureOptions());
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<SessionService>();
        services.AddSingleton<IHologramBroadcaster, SignalRHologramBroadcaster>();
        services.AddHostedService<SessionCleanup>();
        services.AddSingleton(sp => new DisplayLink(
            DisplayUrl(configuration, sp.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(DisplayLink)))));
        services.AddSignalR(o =>
        {
            o.MaximumReceiveMessageSize = 64 * 1024;
            o.AddFilter<SessionErrorFilter>();
        });

        var anonPerMinute = configuration.GetValue("RateLimits:AnonPerMinute", 60);
        services.Configure<RateLimiterOptions>(o => o.AddPolicy("anon", ctx => RateLimitPartition.GetFixedWindowLimiter(
            ctx.Connection.RemoteIpAddress?.ToString() ?? "",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = anonPerMinute, Window = TimeSpan.FromMinutes(1) })));
        return services;
    }

    public static WebApplication MapRealtime(this WebApplication app)
    {
        app.MapHub<HologramHub>("/hubs/hologram");

        var link = app.Services.GetRequiredService<DisplayLink>();
        app.Logger.LogInformation("El QR de las sesiones apunta a {DisplayUrl}", link.BaseUrl);
        var sessions = app.MapGroup("/api/sessions").RequireRateLimiting("anon");

        sessions.MapGet("/{code}/qr.svg", (string code, ISessionStore store) =>
        {
            if (store.Get(SessionCode.Normalize(code)) is not { } session)
                return Results.NotFound();
            using var generator = new QRCodeGenerator();
            using var data = generator.CreateQrCode(link.For(session.Code), QRCodeGenerator.ECCLevel.M);
            return Results.Text(new SvgQRCode(data).GetGraphic(8), "image/svg+xml");
        });

        // El Display no tiene token: descarga el modelo activo con el código de la sesión.
        sessions.MapGet("/{code}/model", async (string code, SessionService service, CancellationToken ct) =>
            await service.OpenActiveModelAsync(code, ct) is { } file
                ? ModelEndpoints.FileResult(file.Model, file.Content)
                : Results.NotFound());

        return app;
    }

    /// <summary>"PublicDisplayUrl" o, si está vacía, http://{IP LAN}:5082.</summary>
    private static string DisplayUrl(IConfiguration configuration, ILogger log)
    {
        if (configuration["PublicDisplayUrl"] is { Length: > 0 } url)
            return url.TrimEnd('/');

        // La interfaz con puerta de enlace es la de la red local (descarta Hyper-V, WSL y similares).
        var ip = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .Select(n => n.GetIPProperties())
            .Where(p => p.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any)))
            .SelectMany(p => p.UnicastAddresses)
            .Select(a => a.Address)
            .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a));
        if (ip is null)
            log.LogWarning("No se detectó una IP LAN: el QR apunta a localhost. Define PublicDisplayUrl en .env.");
        return $"http://{ip?.ToString() ?? "localhost"}:5082";
    }
}
