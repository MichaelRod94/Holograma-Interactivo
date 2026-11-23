using System.Text.Json;
using Holograma3D.Infrastructure;

// Igual que la Api: lee .env (las variables de entorno reales mandan).
DotEnv.Load();
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.UseDefaultFiles();
// no-cache: el móvil revalida (304 si no cambió) y no se queda con JS/CSS viejos tras actualizar.
app.UseStaticFiles(new StaticFileOptions { OnPrepareResponse = ctx => ctx.Context.Response.Headers.CacheControl = "no-cache" });

// El cliente toma de aquí la URL de la Api; vacía = mismo host, puerto 5080. Con un túnel HTTPS: Display__ApiBaseUrl.
var config = $"window.APP_CONFIG = {{ apiBaseUrl: {JsonSerializer.Serialize(app.Configuration["Display:ApiBaseUrl"] ?? "")} }};";
app.MapGet("/config.js", () => Results.Text(config, "text/javascript"));

app.Run();
