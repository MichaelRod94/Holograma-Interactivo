using Holograma3D.Core.Domain.Gestures;

namespace Holograma3D.Tests.Gestures;

/// <summary>Construye manos sintéticas como las enviaría MediaPipe.</summary>
internal static class Hands
{
    public static readonly GestureOptions NoFilter = new() { FilterEnabled = false };

    /// <summary>
    /// 63 floats cuyo centro de palma (promedio de 0, 5, 9, 13, 17) es (rawX, y) y cuya distancia 0→9 es <paramref name="size"/>.
    /// Las coordenadas son crudas de la cámara, sin espejo.
    /// </summary>
    public static float[] Landmarks(float rawX, float y, float size = 0.2f)
    {
        var lm = new float[HandObservation.LandmarkValues];
        for (var i = 0; i < 21; i++)
            Set(lm, i, rawX, y);
        Set(lm, 0, rawX, y + size / 2);   // muñeca
        Set(lm, 9, rawX, y - size / 2);   // nudillo medio
        Set(lm, 5, rawX - 0.05f, y);
        Set(lm, 13, rawX + 0.05f, y);
        return lm;
    }

    /// <summary>Mano en coordenadas de pantalla tal como las ve el usuario (x ya en espejo).</summary>
    public static HandObservation At(string gesture, float x, float y = 0.5f, float size = 0.2f, GestureOptions? options = null) =>
        HandObservation.FromMediaPipe(gesture, 0.9f, Landmarks(1f - x, y, size), options ?? NoFilter);

    public static HandObservation Fist(float x, float y = 0.5f, float size = 0.2f) => At("Closed_Fist", x, y, size);
    public static HandObservation Open(float x, float y = 0.5f, float size = 0.2f) => At("Open_Palm", x, y, size);

    private static void Set(float[] lm, int index, float x, float y)
    {
        lm[index * 3] = x;
        lm[index * 3 + 1] = y;
        lm[index * 3 + 2] = 0f;
    }
}
