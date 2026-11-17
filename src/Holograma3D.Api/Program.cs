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

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseCors("web");

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.MapGet("/health", () => "ok");

app.Run();

public partial class Program;
