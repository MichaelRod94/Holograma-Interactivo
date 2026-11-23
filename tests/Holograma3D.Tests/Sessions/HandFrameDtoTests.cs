using Holograma3D.Api.Contracts;
using Holograma3D.Core.Domain.Gestures;
using Holograma3D.Tests.Gestures;

namespace Holograma3D.Tests.Sessions;

public sealed class HandFrameDtoTests
{
    private static HandDto Fist(float[]? lm = null, float score = 0.9f) => new("Closed_Fist", score, lm ?? Hands.Landmarks(0.5f, 0.5f));

    private static float[] LandmarksWith(int index, float value)
    {
        var lm = Hands.Landmarks(0.5f, 0.5f);
        lm[index] = value;
        return lm;
    }

    [Fact]
    public void Un_frame_valido_se_convierte()
    {
        var frame = new HandFrameDto(16.5, [Fist()]).ToDomain(Hands.NoFilter);

        Assert.NotNull(frame);
        Assert.Equal(16.5, frame.TimestampMs);
        Assert.Equal(HandPose.Fist, Assert.Single(frame.Hands).Pose);
    }

    public static TheoryData<HandFrameDto?> Invalidos => new()
    {
        null,
        new HandFrameDto(0, null!),
        new HandFrameDto(double.NaN, [Fist()]),
        new HandFrameDto(0, [Fist(), Fist(), Fist()]),
        new HandFrameDto(0, [null!]),
        new HandFrameDto(0, [new HandDto(null!, 0.9f, Hands.Landmarks(0.5f, 0.5f))]),
        new HandFrameDto(0, [Fist(new float[62])]),
        new HandFrameDto(0, [Fist(LandmarksWith(7, 1.6f))]),
        new HandFrameDto(0, [Fist(LandmarksWith(7, float.NaN))]),
        new HandFrameDto(0, [Fist(score: 1.5f)]),
    };

    [Theory]
    [MemberData(nameof(Invalidos))]
    public void Un_frame_invalido_se_descarta(HandFrameDto? frame) => Assert.Null(frame.ToDomain(Hands.NoFilter));
}
