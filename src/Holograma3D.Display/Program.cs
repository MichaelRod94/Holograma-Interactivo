using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.UseDefaultFiles();
// no-cache: el móvil revalida (304 si no cambió) y no se queda con JS/CSS viejos tras actualizar.
app.UseStaticFiles(new StaticFileOptions { OnPrepareResponse = ctx => ctx.Context.Response.Headers.CacheControl = "no-cache" });

// El cliente toma de aquí la URL de la Api; vacía = mismo host, puerto 5080.
var config = $"window.APP_CONFIG = {{ apiBaseUrl: {JsonSerializer.Serialize(app.Configuration["ApiBaseUrl"] ?? "")} }};";
app.MapGet("/config.js", () => Results.Text(config, "text/javascript"));

app.Run();
