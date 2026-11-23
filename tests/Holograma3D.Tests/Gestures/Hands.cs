using System.Numerics;
using Holograma3D.Core.Domain.Gestures;

namespace Holograma3D.Tests.Gestures;

/// <summary>Construye manos sintéticas como las enviaría MediaPipe.</summary>
internal static class Hands
{
    public static readonly GestureOptions NoFilter = new() { FilterEnabled = false };

    /// <summary>
    /// 63 floats cuyo centro de palma (promedio de 0, 5, 9, 13, 17) es (rawX, y) y cuyo eje muñeca (0) → nudillo medio (9)
    /// es <paramref name="axis"/> (por defecto, la mano derecha hacia arriba con largo <paramref name="size"/>).
    /// Las coordenadas son crudas de la cámara, sin espejo.
    /// </summary>
    public static float[] Landmarks(float rawX, float y, float size = 0.2f, Vector2? axis = null)
    {
        var a = axis ?? new Vector2(0, -size);
        var lm = new float[HandObservation.LandmarkValues];
        for (var i = 0; i < 21; i++)
            Set(lm, i, rawX, y);
        Set(lm, 0, rawX - a.X / 2, y - a.Y / 2); // muñeca
        Set(lm, 9, rawX + a.X / 2, y + a.Y / 2); // nudillo medio
        Set(lm, 5, rawX - 0.05f, y);
        Set(lm, 13, rawX + 0.05f, y);
        return lm;
    }

    /// <summary>
    /// Mano en coordenadas de pantalla tal como las ve el usuario (x ya en espejo).
    /// <paramref name="tiltDegrees"/> la gira en sentido horario, también como la ve el usuario.
    /// </summary>
    public static HandObservation At(string gesture, float x, float y = 0.5f, float size = 0.2f, GestureOptions? options = null,
        float tiltDegrees = 0)
    {
        var t = tiltDegrees * MathF.PI / 180;
        // En pantalla, el eje girado es (sin t, −cos t); en la cámara (sin espejo), la x va al revés.
        var rawAxis = new Vector2(-MathF.Sin(t), -MathF.Cos(t)) * size;
        return HandObservation.FromMediaPipe(gesture, 0.9f, Landmarks(1f - x, y, size, rawAxis), options ?? NoFilter);
    }

    public static HandObservation Fist(float x, float y = 0.5f, float size = 0.2f) => At("Closed_Fist", x, y, size);
    public static HandObservation Open(float x, float y = 0.5f, float size = 0.2f) => At("Open_Palm", x, y, size);

    /// <summary>La seña de rotar con una mano (✌️ por defecto).</summary>
    public static HandObservation Sign(float x, float y = 0.5f, float tiltDegrees = 0) => At("Victory", x, y, tiltDegrees: tiltDegrees);

    private static void Set(float[] lm, int index, float x, float y)
    {
        lm[index * 3] = x;
        lm[index * 3 + 1] = y;
        lm[index * 3 + 2] = 0f;
    }
}
