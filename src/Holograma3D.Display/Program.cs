using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

// El cliente toma de aquí la URL de la Api; vacía = mismo host, puerto 5080.
var config = $"window.APP_CONFIG = {{ apiBaseUrl: {JsonSerializer.Serialize(app.Configuration["ApiBaseUrl"] ?? "")} }};";
app.MapGet("/config.js", () => Results.Text(config, "text/javascript"));

app.Run();
