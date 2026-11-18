using Holograma3D.Api.Auth;
using Holograma3D.Infrastructure;
using Holograma3D.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddCors(o => o.AddPolicy("web", p =>
{
    if (builder.Environment.IsDevelopment())
        // ponytail: abierto solo en Development, porque el móvil entra por IP LAN; fuera de ahí rige Cors:Origins.
        p.SetIsOriginAllowed(_ => true);
    else
        p.WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? []);

    p.AllowAnyHeader().AllowAnyMethod().AllowCredentials();
}));
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddAuth(builder.Configuration);

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseCors("web");
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    // ponytail: solo en Development; en producción, migration bundle o dotnet ef database update.
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
}

app.MapGet("/health", () => "ok");
app.MapAuthEndpoints();

app.Run();

public partial class Program;
