namespace Holograma3D.Infrastructure;

/// <summary>
/// Lee el archivo .env (CLAVE=valor por línea, comentarios con #, claves sin valor ignoradas; el mismo formato que run.ps1).
/// Busca .env en el directorio actual y hacia arriba: en desarrollo está en la raíz del repo; en un servidor, junto a la app publicada.
/// </summary>
public static class DotEnv
{
    public const string FileName = ".env";

    /// <summary>Ruta del .env más cercano, o null si no hay.</summary>
    public static string? Find(string? start = null)
    {
        for (var dir = new DirectoryInfo(start ?? Directory.GetCurrentDirectory()); dir is not null; dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, FileName);
            if (File.Exists(path))
                return path;
        }
        return null;
    }

    public static IReadOnlyDictionary<string, string> Read(string path)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            var eq = line.IndexOf('=');
            if (line.StartsWith('#') || eq <= 0)
                continue;
            var value = line[(eq + 1)..].Trim();
            if (value.Length > 0)
                values[line[..eq].Trim()] = value;
        }
        return values;
    }

    /// <summary>
    /// Pasa los valores del .env a variables de entorno del proceso, sin pisar las que ya existan:
    /// en un servidor, las variables reales mandan sobre el archivo.
    /// </summary>
    /// <returns>La ruta del .env cargado, o null.</returns>
    public static string? Load(string? start = null)
    {
        if (Find(start) is not { } path)
            return null;
        foreach (var (key, value) in Read(path))
            if (Environment.GetEnvironmentVariable(key) is null)
                Environment.SetEnvironmentVariable(key, value);
        return path;
    }
}
