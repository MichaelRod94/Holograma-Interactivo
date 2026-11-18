using System.Numerics;

namespace Holograma3D.Core.Domain.Gestures;

/// <summary>Una mano reducida a lo que usa el intérprete: pose, centro de la palma y tamaño aparente.</summary>
public readonly record struct HandObservation(HandPose Pose, Vector2 Center, float Size)
{
    public const int LandmarkValues = 21 * 3;

    // Muñeca y nudillos: su promedio no se mueve al cerrar o abrir los dedos.
    private static readonly int[] PalmLandmarks = [0, 5, 9, 13, 17];

    /// <param name="gesture">Categoría de MediaPipe ("Closed_Fist", "Open_Palm", …).</param>
    /// <param name="lm">21 landmarks × (x, y, z) normalizados, sin espejo.</param>
    public static HandObservation FromMediaPipe(string gesture, float score, ReadOnlySpan<float> lm, GestureOptions o)
    {
        if (lm.Length != LandmarkValues)
            throw new ArgumentException($"Se esperan {LandmarkValues} valores (21 landmarks × x, y, z) y llegaron {lm.Length}.", nameof(lm));
        foreach (var v in lm)
            if (!float.IsFinite(v))
                throw new ArgumentException("Los landmarks deben ser números finitos.", nameof(lm));

        // !(score >= min) también descarta NaN.
        var pose = !(score >= o.MinScore) ? HandPose.Other : gesture switch
        {
            "Closed_Fist" => HandPose.Fist,
            "Open_Palm" => HandPose.Open,
            _ => HandPose.Other,
        };

        var center = Vector2.Zero;
        foreach (var i in PalmLandmarks)
            center += Point(lm, i);
        center /= PalmLandmarks.Length;
        if (o.MirrorX)
            center.X = 1f - center.X;

        // Muñeca → nudillo medio: crece al acercar la mano a la cámara.
        var size = Vector2.Distance(Point(lm, 0), Point(lm, 9));
        return new HandObservation(pose, center, size);
    }

    private static Vector2 Point(ReadOnlySpan<float> lm, int index) => new(lm[index * 3], lm[index * 3 + 1]);
}
