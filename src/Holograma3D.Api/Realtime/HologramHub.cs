using System.Diagnostics;
using Holograma3D.Api.Contracts;
using Holograma3D.Core.Application;
using Holograma3D.Core.Domain.Gestures;
using Holograma3D.Core.Domain.Sessions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Holograma3D.Api.Realtime;

public interface IHologramClient
{
    Task TransformChanged(TransformDto transform);
    Task GestureChanged(GestureDto gesture);
    Task ModelChanged(ModelDto? model);
    Task DisplaysChanged(int count);
}

public static class HologramGroups
{
    /// <summary>Displays y control.</summary>
    public static string Session(string code) => $"s:{code}";

    /// <summary>Solo el control.</summary>
    public static string Control(string code) => $"c:{code}";
}

/// <summary>
/// Sin [Authorize] en la clase: el Display se conecta sin cuenta. Los métodos del Control lo llevan uno por uno.
/// La sesión de cada conexión vive en Context.Items ("code", "role").
/// </summary>
public sealed class HologramHub(SessionService sessions, GestureOptions gestures, DisplayLink link, ILogger<HologramHub> log)
    : Hub<IHologramClient>
{
    private const string ControlRole = "control", DisplayRole = "display";
    private const int MaxJoinFailures = 10;
    private static readonly TimeSpan MinFrameInterval = TimeSpan.FromMilliseconds(15);

    private string? Code => Context.Items["code"] as string;
    private string? Role => Context.Items["role"] as string;
    private string OwnerId => Context.UserIdentifier ?? throw new HubException("Inicia sesión.");

    [Authorize]
    public async Task<SessionSnapshotDto> CreateSession()
    {
        var snapshot = await sessions.CreateAsync(OwnerId, Context.ConnectionId, Context.ConnectionAborted);
        await EnterAsync(snapshot.Code, ControlRole);
        return snapshot.ToDto(link.For(snapshot.Code));
    }

    [Authorize]
    public async Task<SessionSnapshotDto> ResumeSession(string code)
    {
        var snapshot = await sessions.ResumeAsync(code, OwnerId, Context.ConnectionId, Context.ConnectionAborted);
        await EnterAsync(snapshot.Code, ControlRole);
        return snapshot.ToDto(link.For(snapshot.Code));
    }

    public async Task<SessionSnapshotDto> JoinSession(string code)
    {
        SessionSnapshot snapshot;
        try
        {
            snapshot = await sessions.JoinAsync(code, Context.ConnectionAborted);
        }
        catch (SessionException)
        {
            // Contra quien prueba códigos al azar.
            var failures = (Context.Items["joinFailures"] as int? ?? 0) + 1;
            Context.Items["joinFailures"] = failures;
            if (failures >= MaxJoinFailures)
                Context.Abort();
            throw;
        }
        await EnterAsync(snapshot.Code, DisplayRole);
        return snapshot.ToDto(link.For(snapshot.Code));
    }

    [Authorize]
    public async Task SendHandFrame(HandFrameDto frame)
    {
        if (Role != ControlRole || Code is not { } code)
            return;

        var now = Stopwatch.GetTimestamp();
        if (Context.Items["lastFrame"] is long last && Stopwatch.GetElapsedTime(last, now) < MinFrameInterval)
            return;
        Context.Items["lastFrame"] = now;

        // Un frame inválido se descarta sin error: llegan ~30 por segundo.
        if (frame.ToDomain(gestures) is not { } handFrame)
        {
            log.LogDebug("Frame inválido descartado en la sesión {Code}", code);
            return;
        }
        await sessions.ProcessFrameAsync(code, Context.ConnectionId, handFrame);
    }

    [Authorize]
    public Task SelectModel(Guid? modelId) =>
        sessions.SelectModelAsync(ControlledCode(), OwnerId, Context.ConnectionId, modelId, Context.ConnectionAborted);

    [Authorize]
    public Task ResetTransform() => sessions.ResetAsync(ControlledCode(), OwnerId, Context.ConnectionId);

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (Role == DisplayRole && Code is { } code)
            await sessions.LeaveAsync(code);
        await base.OnDisconnectedAsync(exception);
    }

    private string ControlledCode() =>
        Role == ControlRole && Code is { } code ? code : throw new HubException("Primero crea o retoma una sesión.");

    /// <summary>Una conexión está en una sola sesión: si ya estaba en otra, sale de ella.</summary>
    private async Task EnterAsync(string code, string role)
    {
        if (Code is { } previous)
        {
            if (Role == DisplayRole)
                await sessions.LeaveAsync(previous);
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, HologramGroups.Session(previous));
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, HologramGroups.Control(previous));
        }

        Context.Items["code"] = code;
        Context.Items["role"] = role;
        await Groups.AddToGroupAsync(Context.ConnectionId, HologramGroups.Session(code));
        if (role == ControlRole)
            await Groups.AddToGroupAsync(Context.ConnectionId, HologramGroups.Control(code));
    }
}

/// <summary>Los errores de negocio llegan al cliente con su mensaje; los demás, genéricos.</summary>
public sealed class SessionErrorFilter : IHubFilter
{
    public async ValueTask<object?> InvokeMethodAsync(HubInvocationContext context, Func<HubInvocationContext, ValueTask<object?>> next)
    {
        try
        {
            return await next(context);
        }
        catch (SessionException ex)
        {
            throw new HubException(ex.Message);
        }
    }
}
