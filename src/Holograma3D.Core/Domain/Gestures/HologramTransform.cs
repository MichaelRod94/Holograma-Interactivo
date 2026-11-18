using System.Numerics;

namespace Holograma3D.Core.Domain.Gestures;

/// <summary>Posición, rotación y escala del modelo. Y arriba; unidades = radio del modelo normalizado.</summary>
public readonly record struct HologramTransform(Vector3 Position, Quaternion Rotation, float Scale)
{
    public static HologramTransform Identity { get; } = new(Vector3.Zero, Quaternion.Identity, 1f);

    public HologramTransform Clamped(GestureOptions o) => new(
        Vector3.Clamp(Position, new Vector3(-o.MaxOffset), new Vector3(o.MaxOffset)),
        Quaternion.Normalize(Rotation),
        Math.Clamp(Scale, o.MinScale, o.MaxScale));

    /// <summary>Igualdad con tolerancia por componente.</summary>
    public bool IsCloseTo(HologramTransform other, float tolerance)
    {
        var dp = Vector3.Abs(Position - other.Position);
        var dr = Rotation - other.Rotation;
        return MathF.Max(dp.X, MathF.Max(dp.Y, dp.Z)) <= tolerance
            && MathF.Abs(dr.X) <= tolerance && MathF.Abs(dr.Y) <= tolerance
            && MathF.Abs(dr.Z) <= tolerance && MathF.Abs(dr.W) <= tolerance
            && MathF.Abs(Scale - other.Scale) <= tolerance;
    }
}
