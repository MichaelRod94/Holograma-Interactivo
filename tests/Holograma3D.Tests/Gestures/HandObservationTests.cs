using Holograma3D.Core.Domain.Gestures;

namespace Holograma3D.Tests.Gestures;

public class HandObservationTests
{
    [Theory]
    [InlineData("Closed_Fist", 0.9f, HandPose.Fist)]
    [InlineData("Open_Palm", 0.9f, HandPose.Open)]
    [InlineData("Victory", 0.9f, HandPose.Other)]
    [InlineData("None", 0.9f, HandPose.Other)]
    [InlineData("Closed_Fist", 0.5f, HandPose.Other)]
    [InlineData("Open_Palm", float.NaN, HandPose.Other)]
    public void Mapea_la_categoria_de_MediaPipe_con_umbral_de_confianza(string gesture, float score, HandPose expected)
    {
        var hand = HandObservation.FromMediaPipe(gesture, score, Hands.Landmarks(0.5f, 0.5f), Hands.NoFilter);

        Assert.Equal(expected, hand.Pose);
    }

    [Fact]
    public void Calcula_centro_de_palma_y_tamano()
    {
        var hand = HandObservation.FromMediaPipe("Open_Palm", 0.9f, Hands.Landmarks(0.3f, 0.4f, size: 0.25f),
            new GestureOptions { MirrorX = false });

        Assert.Equal(0.3f, hand.Center.X, 4);
        Assert.Equal(0.4f, hand.Center.Y, 4);
        Assert.Equal(0.25f, hand.Size, 4);
    }

    [Fact]
    public void MirrorX_invierte_la_x()
    {
        var hand = HandObservation.FromMediaPipe("Open_Palm", 0.9f, Hands.Landmarks(0.3f, 0.4f), new GestureOptions { MirrorX = true });

        Assert.Equal(0.7f, hand.Center.X, 4);
        Assert.Equal(0.4f, hand.Center.Y, 4);
    }

    [Fact]
    public void Rechaza_longitud_distinta_de_63()
    {
        Assert.Throws<ArgumentException>(() => HandObservation.FromMediaPipe("Open_Palm", 0.9f, new float[62], Hands.NoFilter));
    }

    [Fact]
    public void Rechaza_valores_no_finitos()
    {
        var lm = Hands.Landmarks(0.5f, 0.5f);
        lm[10] = float.PositiveInfinity;

        Assert.Throws<ArgumentException>(() => HandObservation.FromMediaPipe("Open_Palm", 0.9f, lm, Hands.NoFilter));
    }

    [Fact]
    public void HandFrame_ordena_dos_manos_de_izquierda_a_derecha()
    {
        var frame = new HandFrame(0, [Hands.Open(0.7f), Hands.Open(0.3f)]);

        Assert.True(frame.Hands[0].Center.X < frame.Hands[1].Center.X);
    }
}
