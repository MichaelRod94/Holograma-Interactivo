using Holograma3D.Api.Auth;
using Holograma3D.Api.Endpoints;
using Holograma3D.Api.Generations;
using Holograma3D.Api.Realtime;
using Holograma3D.Infrastructure;
using Holograma3D.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

// La configuración viene del .env o de variables de entorno reales (que mandan); nada de user-secrets.
var envFile = DotEnv.Load();
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
builder.Services.AddInfrastructure(builder.Configuration, builder.Environment.ContentRootPath);
builder.Services.AddAuth(builder.Configuration);
builder.Services.AddModels();
builder.Services.AddRealtime(builder.Configuration);
builder.Services.AddGenerations(builder.Configuration);

var app = builder.Build();
app.Logger.LogInformation("Configuración: {EnvFile}", envFile ?? "sin .env, solo variables de entorno");

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
app.MapModelEndpoints();
app.MapRealtime();
app.MapGenerationEndpoints();

app.Run();

public partial class Program;
