using System.Numerics;
using Holograma3D.Core.Domain.Gestures;
using static Holograma3D.Tests.Gestures.Hands;

namespace Holograma3D.Tests.Gestures;

public class GestureInterpreterTests
{
    private readonly GestureInterpreter _sut = new(NoFilter);
    private double _t;

    private bool Feed(int frames, params HandObservation[] hands)
    {
        var changed = false;
        for (var i = 0; i < frames; i++)
            changed |= _sut.Apply(new HandFrame(_t += 33, hands));
        return changed;
    }

    [Fact]
    public void Histeresis_exige_tres_frames_seguidos()
    {
        Assert.False(Feed(2, Fist(0.5f)));
        Assert.Equal(GestureState.Idle, _sut.State);

        Assert.True(Feed(1, Fist(0.5f)));
        Assert.Equal(GestureState.Grab, _sut.State);
    }

    [Fact]
    public void Grab_mueve_a_la_derecha_y_abrir_la_mano_suelta_sin_mover()
    {
        Feed(3, Fist(0.5f));
        Feed(1, Fist(0.6f)); // a la derecha, como lo ve el usuario

        var moved = _sut.Transform.Position;
        Assert.True(moved.X > 0);

        Feed(3, Open(0.62f));
        Assert.Equal(GestureState.Idle, _sut.State);
        Assert.Equal(moved, _sut.Transform.Position);
    }

    [Fact]
    public void Grab_sube_cuando_la_mano_sube()
    {
        Feed(3, Fist(0.5f, y: 0.5f));
        Feed(1, Fist(0.5f, y: 0.4f)); // en la imagen, Y hacia abajo

        Assert.True(_sut.Transform.Position.Y > 0);
    }

    [Fact]
    public void Grab_acerca_el_modelo_cuando_la_mano_crece()
    {
        Feed(3, Fist(0.5f, size: 0.2f));
        Feed(1, Fist(0.5f, size: 0.3f));

        Assert.True(_sut.Transform.Position.Z > 0);
    }

    [Fact]
    public void Volver_a_agarrar_en_otro_lugar_no_produce_salto()
    {
        Feed(3, Fist(0.5f));
        Feed(1, Fist(0.6f));
        Feed(3, Open(0.6f));
        var released = _sut.Transform.Position;

        Feed(3, Fist(0.2f)); // la mano reaparece lejos

        Assert.Equal(GestureState.Grab, _sut.State);
        Assert.Equal(released, _sut.Transform.Position);

        Feed(1, Fist(0.3f));
        Assert.True(_sut.Transform.Position.X > released.X);
    }

    [Fact]
    public void Zoom_escala_con_la_distancia_entre_palmas()
    {
        Feed(3, Open(0.4f), Open(0.6f)); // distancia 0.2
        Assert.Equal(GestureState.Zoom, _sut.State);

        Feed(1, Open(0.3f), Open(0.7f)); // distancia 0.4

        Assert.Equal(2f, _sut.Transform.Scale, 3);
    }

    [Fact]
    public void Rotate_yaw_al_mover_los_punos_a_la_derecha()
    {
        Feed(3, Fist(0.4f), Fist(0.6f));
        Assert.Equal(GestureState.Rotate, _sut.State);

        Feed(1, Fist(0.5f), Fist(0.7f));

        Assert.True(Vector3.Transform(Vector3.UnitZ, _sut.Transform.Rotation).X > 0);
    }

    [Fact]
    public void Rotate_pitch_al_bajar_los_punos()
    {
        Feed(3, Fist(0.4f), Fist(0.6f));
        Feed(1, Fist(0.4f, y: 0.6f), Fist(0.6f, y: 0.6f));

        Assert.True(Vector3.Transform(Vector3.UnitZ, _sut.Transform.Rotation).Y < 0);
    }

    [Fact]
    public void Rotate_roll_al_girar_las_manos_en_sentido_horario()
    {
        Feed(3, Fist(0.4f), Fist(0.6f));
        Feed(1, Fist(0.4f, y: 0.45f), Fist(0.6f, y: 0.55f)); // izquierda sube, derecha baja

        var right = Vector3.Transform(Vector3.UnitX, _sut.Transform.Rotation);
        Assert.True(right.Y < 0);
        Assert.Equal(0f, Vector3.Transform(Vector3.UnitZ, _sut.Transform.Rotation).X, 4); // sin yaw
    }

    [Fact]
    public void Rotate_con_una_mano_gira_al_moverla()
    {
        Feed(3, Sign(0.5f));
        Assert.Equal(GestureState.Rotate, _sut.State);

        Feed(1, Sign(0.6f));

        Assert.True(Vector3.Transform(Vector3.UnitZ, _sut.Transform.Rotation).X > 0);
    }

    [Fact]
    public void Rotate_con_una_mano_inclina_al_girar_la_muneca()
    {
        Feed(3, Sign(0.5f));

        Feed(1, Sign(0.5f, tiltDegrees: 30)); // muñeca girada 30° en sentido horario

        var right = Vector3.Transform(Vector3.UnitX, _sut.Transform.Rotation);
        Assert.Equal(-MathF.Sin(MathF.PI / 6), right.Y, 3);                            // inclinado 30° a la derecha
        Assert.Equal(0f, Vector3.Transform(Vector3.UnitZ, _sut.Transform.Rotation).X, 4); // sin yaw
    }

    [Fact]
    public void Pasar_de_una_mano_a_dos_punos_sigue_rotando_sin_saltar()
    {
        Feed(3, Sign(0.5f));
        Feed(1, Sign(0.6f));
        var rotated = _sut.Transform.Rotation;

        Feed(1, Fist(0.4f), Fist(0.6f));

        Assert.Equal(GestureState.Rotate, _sut.State);
        Assert.Equal(rotated, _sut.Transform.Rotation);

        Feed(1, Fist(0.5f), Fist(0.7f));
        Assert.NotEqual(rotated, _sut.Transform.Rotation);
    }

    [Fact]
    public void Con_filtro_girar_la_muneca_cruzando_180_grados_no_da_media_vuelta()
    {
        var sut = new GestureInterpreter(new GestureOptions());
        var t = 0d;
        // Mano apuntando a la izquierda: su ángulo pasa de −175° a +175° al girar 10° en sentido antihorario.
        for (var i = 0; i < 3; i++) sut.Apply(new HandFrame(t += 33, [Sign(0.5f, tiltDegrees: -85)]));
        float RollDegrees()
        {
            var right = Vector3.Transform(Vector3.UnitX, sut.Transform.Rotation);
            return MathF.Atan2(right.Y, right.X) * 180 / MathF.PI;
        }

        var maxRoll = 0f;
        for (var i = 0; i < 40; i++)
        {
            sut.Apply(new HandFrame(t += 33, [Sign(0.5f, tiltDegrees: -95)]));
            maxRoll = MathF.Max(maxRoll, MathF.Abs(RollDegrees()));
        }

        // Sin "desenrollar", el filtro pasaría de −175° a +175° por 0° y el modelo daría media vuelta en el camino.
        Assert.True(maxRoll <= 10.5f, $"roll máximo {maxRoll:F1}°");
        Assert.Equal(10f, RollDegrees(), 0); // termina inclinado 10° a la izquierda
    }

    [Fact]
    public void Puno_mas_palma_es_idle()
    {
        Feed(3, Fist(0.4f), Fist(0.6f));
        Feed(3, Fist(0.4f), Open(0.6f));

        Assert.Equal(GestureState.Idle, _sut.State);
    }

    [Fact]
    public void La_escala_no_supera_el_maximo()
    {
        Feed(3, Open(0.45f), Open(0.55f));
        Feed(1, Open(0.05f), Open(0.95f));

        Assert.Equal(NoFilter.MaxScale, _sut.Transform.Scale);
    }

    [Fact]
    public void Reset_vuelve_a_identidad_e_idle()
    {
        Feed(3, Fist(0.5f));
        Feed(1, Fist(0.7f));

        _sut.Reset();

        Assert.Equal(GestureState.Idle, _sut.State);
        Assert.Equal(HologramTransform.Identity, _sut.Transform);
    }

    [Fact]
    public void Sin_manos_tres_frames_suelta()
    {
        Feed(3, Fist(0.5f));

        Feed(3);

        Assert.Equal(GestureState.Idle, _sut.State);
        Assert.Equal(0, _sut.HandCount);
    }

    [Fact]
    public void Con_filtro_el_movimiento_sostenido_va_en_la_misma_direccion()
    {
        var sut = new GestureInterpreter(new GestureOptions());
        var t = 0d;
        for (var i = 0; i < 3; i++) sut.Apply(new HandFrame(t += 33, [Fist(0.5f)]));
        for (var i = 0; i < 30; i++) sut.Apply(new HandFrame(t += 33, [Fist(0.6f)]));

        Assert.Equal(0.1f * new GestureOptions().GainXY, sut.Transform.Position.X, 2);
    }
}
