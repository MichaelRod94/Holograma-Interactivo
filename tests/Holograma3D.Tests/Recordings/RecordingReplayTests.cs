using System.Text.Json;
using Holograma3D.Api.Contracts;
using Holograma3D.Core.Domain.Gestures;
using Microsoft.Extensions.Configuration;

namespace Holograma3D.Tests.Recordings;

/// <summary>Se omite si falta la grabación (se hace con el botón "Grabar 30 s" del Control; ver README.md de esta carpeta).</summary>
public sealed class RequiresRecordingFactAttribute : FactAttribute
{
    public RequiresRecordingFactAttribute(string fileName)
    {
        if (RecordingReplayTests.Find(fileName) is null)
            Skip = $"Falta tests/Holograma3D.Tests/Recordings/{fileName} (grábala con el Control).";
    }
}

/// <summary>Reproduce grabaciones reales de manos en el GestureInterpreter, con la calibración de appsettings.json de la Api.</summary>
public sealed class RecordingReplayTests
{
    public const string StillOpenHands = "manos-abiertas-quietas.json";
    public const string GrabMoveRelease = "agarrar-mover-soltar.json";

    public static string? Find(string fileName) => RepoFile("tests", "Holograma3D.Tests", "Recordings", fileName);

    /// <summary>Un archivo del repositorio, buscando hacia arriba desde la carpeta de salida de los tests.</summary>
    private static string? RepoFile(params string[] parts)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var path = Path.Combine([dir.FullName, .. parts]);
            if (File.Exists(path))
                return path;
        }
        return null;
    }

    /// <summary>Los mismos valores con los que corre la Api, para que el test mida la calibración real.</summary>
    private static GestureOptions ApiGestureOptions()
    {
        var settings = RepoFile("src", "Holograma3D.Api", "appsettings.json")
            ?? throw new FileNotFoundException("No se encontró src/Holograma3D.Api/appsettings.json");
        return new ConfigurationBuilder().AddJsonFile(settings).Build()
            .GetSection("Gestures").Get<GestureOptions>() ?? new GestureOptions();
    }

    /// <summary>Los estados por los que pasa el intérprete (sin repetir consecutivos), empezando en Idle.</summary>
    private static List<GestureState> Replay(string fileName)
    {
        var frames = JsonSerializer.Deserialize<HandFrameDto[]>(File.ReadAllText(Find(fileName)!), JsonSerializerOptions.Web)!;
        var options = ApiGestureOptions();
        var interpreter = new GestureInterpreter(options);
        var states = new List<GestureState> { GestureState.Idle };
        foreach (var frame in frames)
        {
            if (frame.ToDomain(options) is not { } handFrame)
                continue; // como en el hub: un frame inválido se descarta
            interpreter.Apply(handFrame);
            if (interpreter.State != states[^1])
                states.Add(interpreter.State);
        }
        return states;
    }

    [RequiresRecordingFact(StillOpenHands)]
    public void Manos_abiertas_quietas_nunca_agarran()
    {
        var states = Replay(StillOpenHands);

        Assert.DoesNotContain(GestureState.Grab, states);
    }

    [RequiresRecordingFact(GrabMoveRelease)]
    public void Agarrar_mover_soltar_produce_un_solo_grab_y_termina_libre()
    {
        var states = Replay(GrabMoveRelease);

        Assert.Equal(1, states.Count(s => s == GestureState.Grab));
        Assert.Equal(GestureState.Idle, states[^1]);
    }
}
