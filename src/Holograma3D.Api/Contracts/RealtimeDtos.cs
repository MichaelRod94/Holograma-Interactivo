using Holograma3D.Core.Application;
using Holograma3D.Core.Domain.Gestures;

namespace Holograma3D.Api.Contracts;

/// <param name="Lm">21 landmarks × (x, y, z) normalizados, sin espejo.</param>
public sealed record HandDto(string Gesture, float Score, float[] Lm);

/// <param name="T">performance.now() del Control, en ms.</param>
public sealed record HandFrameDto(double T, HandDto[] Hands);

public sealed record TransformDto(float Px, float Py, float Pz, float Qx, float Qy, float Qz, float Qw, float Scale, long Seq, double SrcT);

public sealed record GestureDto(string State, int Hands);

/// <param name="DisplayUrl">La URL que codifica el QR; el Control la muestra para copiarla.</param>
public sealed record SessionSnapshotDto(string Code, ModelDto? Model, TransformDto Transform, GestureDto Gesture, int Displays, string DisplayUrl);

public static class RealtimeMapping
{
    public static TransformDto ToDto(this HologramTransform t, long seq, double srcT) => new(
        t.Position.X, t.Position.Y, t.Position.Z, t.Rotation.X, t.Rotation.Y, t.Rotation.Z, t.Rotation.W, t.Scale, seq, srcT);

    public static GestureDto ToDto(this GestureState state, int hands) => new(state.ToString().ToLowerInvariant(), hands);

    public static SessionSnapshotDto ToDto(this SessionSnapshot s, string displayUrl) =>
        new(s.Code, s.Model?.ToDto(), s.Transform.ToDto(s.Seq, 0), s.Gesture.ToDto(s.HandCount), s.Displays, displayUrl);

    /// <summary>Valida un frame que llega del navegador; null si no es válido.</summary>
    public static HandFrame? ToDomain(this HandFrameDto? frame, GestureOptions options)
    {
        if (frame?.Hands is not { Length: <= 2 } hands || !double.IsFinite(frame.T))
            return null;

        var observations = new HandObservation[hands.Length];
        for (var i = 0; i < hands.Length; i++)
        {
            if (hands[i] is not { Gesture: not null, Lm.Length: HandObservation.LandmarkValues } hand || !(hand.Score is >= 0 and <= 1))
                return null;
            foreach (var v in hand.Lm)
                if (!(v is >= -0.5f and <= 1.5f)) // también descarta NaN e infinitos
                    return null;
            observations[i] = HandObservation.FromMediaPipe(hand.Gesture, hand.Score, hand.Lm, options);
        }
        return new HandFrame(frame.T, observations);
    }
}
