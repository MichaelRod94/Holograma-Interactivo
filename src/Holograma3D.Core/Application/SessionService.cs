using Holograma3D.Core.Domain.Gestures;
using Holograma3D.Core.Domain.Models;
using Holograma3D.Core.Domain.Sessions;
using Holograma3D.Core.Ports;

namespace Holograma3D.Core.Application;

public sealed record SessionSnapshot(string Code, Model3D? Model, HologramTransform Transform, long Seq,
    GestureState Gesture, int HandCount, int Displays);

/// <summary>
/// Casos de uso de las sesiones de holograma. Scoped (usa el catálogo, que usa la base);
/// el estado vive en <see cref="ISessionStore"/>, que es singleton.
/// </summary>
public sealed class SessionService(ISessionStore store, IHologramBroadcaster broadcaster, ModelCatalog catalog,
    TimeProvider time, GestureOptions gestures)
{
    public async Task<SessionSnapshot> CreateAsync(string ownerId, string connectionId, CancellationToken ct = default)
    {
        HologramSession session;
        do
            session = new HologramSession(SessionCode.New(), ownerId, new GestureInterpreter(gestures), time.GetUtcNow())
            {
                ControlConnectionId = connectionId,
            };
        while (!store.TryAdd(session));
        return await SnapshotAsync(session, ct);
    }

    /// <summary>Solo el dueño. Esta conexión pasa a ser la de control: manda la pestaña más reciente.</summary>
    public async Task<SessionSnapshot> ResumeAsync(string code, string ownerId, string connectionId, CancellationToken ct = default)
    {
        var s = Find(code);
        if (s.OwnerId != ownerId)
            throw SessionException.NotFound(s.Code); // la de otro usuario no se distingue de una inexistente
        lock (s.Gate)
        {
            s.ControlConnectionId = connectionId;
            Touch(s);
        }
        return await SnapshotAsync(s, ct);
    }

    public async Task<SessionSnapshot> JoinAsync(string code, CancellationToken ct = default)
    {
        var s = Find(code);
        int displays;
        lock (s.Gate)
        {
            displays = ++s.Displays;
            Touch(s);
        }
        await broadcaster.DisplaysChanged(s.Code, displays);
        return await SnapshotAsync(s, ct);
    }

    public async Task LeaveAsync(string code)
    {
        if (store.Get(SessionCode.Normalize(code)) is not { } s)
            return;
        int displays;
        lock (s.Gate)
        {
            displays = s.Displays = Math.Max(0, s.Displays - 1);
            Touch(s);
        }
        await broadcaster.DisplaysChanged(s.Code, displays);
    }

    /// <summary>Ignora en silencio los frames de otra conexión o de una sesión que ya no existe.</summary>
    public async Task ProcessFrameAsync(string code, string connectionId, HandFrame frame)
    {
        if (store.Get(SessionCode.Normalize(code)) is not { } s)
            return;

        bool changed, gestureChanged;
        HologramTransform transform;
        long seq;
        GestureState state;
        int hands;
        lock (s.Gate)
        {
            if (s.ControlConnectionId != connectionId)
                return;
            Touch(s);
            var (previousState, previousHands) = (s.Interpreter.State, s.Interpreter.HandCount);
            changed = s.Interpreter.Apply(frame);
            if (changed)
                s.Seq++;
            (transform, seq, state, hands) = (s.Interpreter.Transform, s.Seq, s.Interpreter.State, s.Interpreter.HandCount);
            gestureChanged = state != previousState || hands != previousHands;
        }

        if (changed)
            await broadcaster.TransformChanged(s.Code, transform, seq, frame.TimestampMs);
        if (gestureChanged)
            await broadcaster.GestureChanged(s.Code, state, hands);
    }

    /// <param name="modelId">Un modelo del dueño, o null para el modelo por defecto.</param>
    public async Task SelectModelAsync(string code, string ownerId, string connectionId, Guid? modelId, CancellationToken ct = default)
    {
        var s = FindControlled(code, ownerId, connectionId);
        var model = modelId is { } id
            ? await catalog.GetAsync(id, ownerId, ct) ?? throw new SessionException("El modelo no existe.")
            : null;
        lock (s.Gate)
            s.ModelId = model?.Id;
        await broadcaster.ModelChanged(s.Code, model);
        await ResetAsync(s);
    }

    public Task ResetAsync(string code, string ownerId, string connectionId) =>
        ResetAsync(FindControlled(code, ownerId, connectionId));

    /// <summary>Para el Display, que no tiene token: el archivo del modelo activo de la sesión.</summary>
    public async Task<(Model3D Model, Stream Content)?> OpenActiveModelAsync(string code, CancellationToken ct = default)
    {
        if (store.Get(SessionCode.Normalize(code)) is not { } s)
            return null;
        Guid? modelId;
        lock (s.Gate)
        {
            modelId = s.ModelId;
            Touch(s);
        }
        return modelId is { } id ? await catalog.OpenFileAsync(id, s.OwnerId, ct) : null;
    }

    private async Task ResetAsync(HologramSession s)
    {
        HologramTransform transform;
        long seq;
        lock (s.Gate)
        {
            s.Interpreter.Reset();
            seq = ++s.Seq;
            transform = s.Interpreter.Transform;
            Touch(s);
        }
        await broadcaster.TransformChanged(s.Code, transform, seq, 0);
        await broadcaster.GestureChanged(s.Code, GestureState.Idle, 0);
    }

    private HologramSession Find(string code)
    {
        var normalized = SessionCode.Normalize(code);
        return store.Get(normalized) ?? throw SessionException.NotFound(normalized);
    }

    private HologramSession FindControlled(string code, string ownerId, string connectionId)
    {
        var s = Find(code);
        if (s.OwnerId != ownerId)
            throw SessionException.NotFound(s.Code);
        lock (s.Gate)
            if (s.ControlConnectionId != connectionId)
                throw new SessionException($"Otra pestaña tomó el control de la sesión {s.Code}.");
        return s;
    }

    private void Touch(HologramSession s) => s.LastActivity = time.GetUtcNow();

    private async Task<SessionSnapshot> SnapshotAsync(HologramSession s, CancellationToken ct)
    {
        SessionSnapshot snapshot;
        Guid? modelId;
        lock (s.Gate)
        {
            modelId = s.ModelId;
            snapshot = new SessionSnapshot(s.Code, null, s.Interpreter.Transform, s.Seq, s.Interpreter.State,
                s.Interpreter.HandCount, s.Displays);
        }
        return modelId is { } id ? snapshot with { Model = await catalog.GetAsync(id, s.OwnerId, ct) } : snapshot;
    }
}
