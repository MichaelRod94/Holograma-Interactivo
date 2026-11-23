using System.Security.Claims;
using System.Threading.RateLimiting;
using Holograma3D.Infrastructure.Identity;
using Holograma3D.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Identity;

namespace Holograma3D.Api.Auth;

public static class AuthSetup
{
    public static IServiceCollection AddAuth(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddIdentityApiEndpoints<AppUser>(o =>
            {
                o.Password.RequiredLength = 8;
                o.User.RequireUniqueEmail = true;
                o.SignIn.RequireConfirmedAccount = false;
                o.Lockout.MaxFailedAccessAttempts = 5;
                o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
            })
            .AddEntityFrameworkStores<AppDbContext>();

        // Data Protection cifra los tokens con claves guardadas en el perfil del usuario de Windows.
        // ponytail: en contenedores o con varias instancias, PersistKeysToDbContext para que los tokens sobrevivan reinicios.
        // Auth__AccessTokenMinutes acorta la vida del token para probar la renovación (por defecto 30).
        var accessTokenMinutes = configuration.GetValue("Auth:AccessTokenMinutes", 30);
        services.Configure<BearerTokenOptions>(IdentityConstants.BearerScheme, o =>
        {
            o.BearerTokenExpiration = TimeSpan.FromMinutes(accessTokenMinutes);
            o.RefreshTokenExpiration = TimeSpan.FromDays(7);
            o.Events.OnMessageReceived = ctx =>
            {
                // WebSockets no permite headers: el token llega por query, pero solo se acepta en /hubs.
                if (ctx.Request.Path.StartsWithSegments("/hubs") && ctx.Request.Query.TryGetValue("access_token", out var token))
                    ctx.Token = token;
                return Task.CompletedTask;
            };
        });
        services.AddAuthorization();

        var authPerMinute = configuration.GetValue("RateLimits:AuthPerMinute", 10);
        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.AddPolicy("auth", ctx => RateLimitPartition.GetFixedWindowLimiter(
                ctx.Connection.RemoteIpAddress?.ToString() ?? "",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = authPerMinute, Window = TimeSpan.FromMinutes(1) }));
        });
        return services;
    }

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/api/auth").RequireRateLimiting("auth");
        auth.MapIdentityApi<AppUser>();

        auth.MapGet("/me", async (ClaimsPrincipal principal, UserManager<AppUser> users) =>
                await users.GetUserAsync(principal) is { } user
                    ? Results.Ok(new { id = user.Id, email = user.Email })
                    : Results.Unauthorized())
            .RequireAuthorization();

        // Cambiar el security stamp invalida todos los refresh tokens del usuario; el access token vence solo en ≤ 30 min.
        auth.MapPost("/logout-all", async (ClaimsPrincipal principal, UserManager<AppUser> users) =>
            {
                if (await users.GetUserAsync(principal) is not { } user)
                    return Results.Unauthorized();
                await users.UpdateSecurityStampAsync(user);
                return Results.NoContent();
            })
            .RequireAuthorization();

        return app;
    }
}
