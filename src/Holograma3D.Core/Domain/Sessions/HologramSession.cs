using System.Security.Cryptography;
using Holograma3D.Core.Domain.Gestures;

namespace Holograma3D.Core.Domain.Sessions;

/// <summary>Una sesión de holograma: un dueño, la conexión que controla, los displays y el estado de los gestos.</summary>
/// <remarks>Todo acceso a sus propiedades mutables va bajo <c>lock (Gate)</c>.</remarks>
public sealed class HologramSession(string code, string ownerId, GestureInterpreter interpreter, DateTimeOffset createdAt)
{
    public string Code { get; } = code;
    public string OwnerId { get; } = ownerId;
    public GestureInterpreter Interpreter { get; } = interpreter;
    public Lock Gate { get; } = new();

    public string? ControlConnectionId { get; set; }
    public Guid? ModelId { get; set; }
    public long Seq { get; set; }
    public int Displays { get; set; }
    public DateTimeOffset LastActivity { get; set; } = createdAt;
}

public static class SessionCode
{
    public const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
    public const int Length = 6;

    public static string New() => RandomNumberGenerator.GetString(Alphabet, Length);

    /// <summary>El código se escribe a mano a veces: sin espacios y en mayúsculas.</summary>
    public static string Normalize(string? code) => (code ?? "").Trim().ToUpperInvariant();
}

/// <summary>Error de negocio de una sesión; el hub lo devuelve al cliente con este mensaje.</summary>
public sealed class SessionException(string message) : Exception(message)
{
    public static SessionException NotFound(string code) => new($"La sesión {code} no existe o expiró.");
}
