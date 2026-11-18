using Holograma3D.Core.Domain.Gestures;

namespace Holograma3D.Tests.Gestures;

public class OneEuroFilterTests
{
    private static OneEuroFilter Default()
    {
        var o = new GestureOptions();
        return new OneEuroFilter(o.FilterMinCutoff, o.FilterBeta, o.FilterDCutoff);
    }

    [Fact]
    public void Converge_a_una_entrada_constante()
    {
        var filter = Default();
        filter.Filter(0f, 0);

        var output = 0f;
        for (var i = 1; i <= 120; i++)
            output = filter.Filter(1f, i * 33.3);

        Assert.Equal(1f, output, 3);
    }

    [Fact]
    public void Reduce_el_temblor_de_una_senal_quieta()
    {
        var filter = Default();
        var random = new Random(42);
        var raw = new List<float>();
        var filtered = new List<float>();

        for (var i = 0; i < 300; i++)
        {
            // Box-Muller: ruido gaussiano con σ = 0.02 alrededor de 0.5.
            var noise = MathF.Sqrt(-2f * MathF.Log(1f - random.NextSingle())) * MathF.Cos(2f * MathF.PI * random.NextSingle());
            var value = 0.5f + 0.02f * noise;
            raw.Add(value);
            filtered.Add(filter.Filter(value, i * 33.3));
        }

        Assert.True(StdDev(filtered.Skip(30)) < StdDev(raw.Skip(30)) / 2);
    }

    [Fact]
    public void Reset_toma_el_siguiente_valor_tal_cual()
    {
        var filter = Default();
        filter.Filter(0f, 0);
        filter.Filter(0f, 33);

        filter.Reset();

        Assert.Equal(5f, filter.Filter(5f, 66));
    }

    private static float StdDev(IEnumerable<float> values)
    {
        var list = values.ToList();
        var mean = list.Average();
        return MathF.Sqrt(list.Sum(v => (v - mean) * (v - mean)) / list.Count);
    }
}
