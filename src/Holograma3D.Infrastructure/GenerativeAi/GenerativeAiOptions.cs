namespace Holograma3D.Infrastructure.GenerativeAi;

/// <summary>Sección "GenerativeAi" (en .env: GenerativeAi__Provider, GenerativeAi__ApiKey…).</summary>
public sealed class GenerativeAiOptions
{
    public const string Section = "GenerativeAi";

    /// <summary>Meshy | Http | Fake; vacío = generación deshabilitada.</summary>
    public string? Provider { get; set; }
    public string? BaseUrl { get; set; }

    /// <summary>Secreto: nunca va a logs ni a respuestas, y solo viaja al host de BaseUrl.</summary>
    public string? ApiKey { get; set; }
    public string? Model { get; set; }
    public string OutputFormat { get; set; } = "glb";
    public bool Texture { get; set; }
    public int MaxImages { get; set; } = 4;
    public int PollSeconds { get; set; } = 5;
    public int TimeoutMinutes { get; set; } = 15;
    public int TargetPolycount { get; set; } = 30000;
    public int MaxActiveJobsPerUser { get; set; } = 3;

    public string NormalizedProvider => (Provider ?? "").Trim().ToLowerInvariant();

    /// <summary>null si la configuración sirve; si no, qué corregir.</summary>
    public string? Problem()
    {
        var provider = NormalizedProvider;
        if (provider is not ("" or "meshy" or "http" or "fake"))
            return $"GenerativeAi__Provider debe ser Meshy, Http, Fake o vacío (llegó \"{Provider}\").";
        if (OutputFormat is not ("glb" or "stl"))
            return "GenerativeAi__OutputFormat debe ser glb o stl.";
        if (MaxImages < 1 || PollSeconds < 1 || TimeoutMinutes < 1 || MaxActiveJobsPerUser < 1)
            return "MaxImages, PollSeconds, TimeoutMinutes y MaxActiveJobsPerUser de GenerativeAi deben ser mayores que 0.";
        if (TargetPolycount is < 100 or > 300_000)
            return "GenerativeAi__TargetPolycount debe estar entre 100 y 300000.";
        if (!string.IsNullOrWhiteSpace(BaseUrl) && !IsHttpUrl(BaseUrl))
            return "GenerativeAi__BaseUrl debe ser una URL absoluta http o https.";
        return provider switch
        {
            "meshy" when string.IsNullOrWhiteSpace(ApiKey) => "Con GenerativeAi__Provider=Meshy falta GenerativeAi__ApiKey (ponla en el .env o como variable de entorno).",
            "meshy" when MaxImages > 4 => "Meshy acepta de 1 a 4 fotos: GenerativeAi__MaxImages debe estar entre 1 y 4.",
            "http" when string.IsNullOrWhiteSpace(BaseUrl) => "Con GenerativeAi__Provider=Http falta GenerativeAi__BaseUrl (la URL de tu servidor).",
            _ => null,
        };
    }

    private static bool IsHttpUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
