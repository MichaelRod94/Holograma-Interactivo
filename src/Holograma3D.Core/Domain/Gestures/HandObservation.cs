using System.Numerics;

namespace Holograma3D.Core.Domain.Gestures;

/// <summary>
/// Una mano reducida a lo que usa el intérprete: pose, centro de la palma, tamaño aparente y ángulo de la mano
/// (muñeca → nudillo medio, en radianes, en la imagen tal como la ve el usuario; crece al girar en sentido horario).
/// </summary>
public readonly record struct HandObservation(HandPose Pose, Vector2 Center, float Size, float Angle = 0f)
{
    public const int LandmarkValues = 21 * 3;

    // Muñeca y nudillos: su promedio no se mueve al cerrar o abrir los dedos.
    private static readonly int[] PalmLandmarks = [0, 5, 9, 13, 17];

    /// <param name="gesture">Categoría de MediaPipe ("Closed_Fist", "Open_Palm", "Victory", …).</param>
    /// <param name="lm">21 landmarks × (x, y, z) normalizados, sin espejo.</param>
    public static HandObservation FromMediaPipe(string gesture, float score, ReadOnlySpan<float> lm, GestureOptions o)
    {
        if (lm.Length != LandmarkValues)
            throw new ArgumentException($"Se esperan {LandmarkValues} valores (21 landmarks × x, y, z) y llegaron {lm.Length}.", nameof(lm));
        foreach (var v in lm)
            if (!float.IsFinite(v))
                throw new ArgumentException("Los landmarks deben ser números finitos.", nameof(lm));

        // !(score >= min) también descarta NaN.
        var pose = !(score >= o.MinScore) ? HandPose.Other
            : gesture == "Closed_Fist" ? HandPose.Fist
            : gesture == "Open_Palm" ? HandPose.Open
            : !string.IsNullOrEmpty(o.RotateGesture) && gesture == o.RotateGesture ? HandPose.RotateSign
            : HandPose.Other;

        var center = Vector2.Zero;
        foreach (var i in PalmLandmarks)
            center += Point(lm, i);
        center /= PalmLandmarks.Length;

        // Muñeca → nudillo medio: su largo crece al acercar la mano a la cámara y su dirección gira con la muñeca.
        var axis = Point(lm, 9) - Point(lm, 0);
        if (o.MirrorX)
        {
            center.X = 1f - center.X;
            axis.X = -axis.X;
        }
        return new HandObservation(pose, center, axis.Length(), MathF.Atan2(axis.Y, axis.X));
    }

    private static Vector2 Point(ReadOnlySpan<float> lm, int index) => new(lm[index * 3], lm[index * 3 + 1]);
}
