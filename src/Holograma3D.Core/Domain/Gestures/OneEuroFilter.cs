namespace Holograma3D.Core.Domain.Gestures;

/// <summary>
/// Filtro One Euro (Casiez, Roussel y Vogel, CHI 2012; https://gery.casiez.net/1euro/):
/// suaviza mucho cuando la señal está quieta (menos temblor) y poco cuando se mueve rápido (menos retraso).
/// </summary>
public sealed class OneEuroFilter(float minCutoff, float beta, float dCutoff)
{
    private bool _initialized;
    private float _x;
    private float _dx;
    private double _t;

    public float Filter(float value, double timestampMs)
    {
        if (!_initialized)
        {
            (_initialized, _x, _dx, _t) = (true, value, 0f, timestampMs);
            return value;
        }

        // ponytail: marcas de tiempo repetidas o hacia atrás se tratan como un cuadro a 30 fps.
        var dt = (float)((timestampMs - _t) / 1000.0);
        if (dt <= 0f)
            dt = 1f / 30f;
        _t = timestampMs;

        _dx = Smooth(_dx, (value - _x) / dt, Alpha(dCutoff, dt));
        _x = Smooth(_x, value, Alpha(minCutoff + beta * MathF.Abs(_dx), dt));
        return _x;
    }

    public void Reset() => _initialized = false;

    private static float Alpha(float cutoffHz, float dt) => 1f / (1f + 1f / (2f * MathF.PI * cutoffHz * dt));

    private static float Smooth(float previous, float value, float alpha) => previous + alpha * (value - previous);
}
