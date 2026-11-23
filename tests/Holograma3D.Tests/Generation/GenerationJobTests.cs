using Holograma3D.Core.Domain.Generation;

namespace Holograma3D.Tests.Generation;

public sealed class GenerationJobTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);

    private static GenerationJob NewJob() => new(Guid.NewGuid(), "u", "Taza", "Fake", "task-1", T0);

    [Fact]
    public void Avanza_de_queued_a_running_y_succeeded()
    {
        var job = NewJob();
        Assert.Equal(GenerationStatus.Queued, job.Status);

        job.MarkRunning(40, T0.AddSeconds(5));
        Assert.Equal((GenerationStatus.Running, 40), (job.Status, job.Progress));

        var model = Guid.NewGuid();
        job.MarkSucceeded(model, T0.AddSeconds(10));
        Assert.Equal((GenerationStatus.Succeeded, 100, model), (job.Status, job.Progress, job.ModelId));
        Assert.Equal(T0.AddSeconds(10), job.UpdatedAt);
        Assert.False(job.IsActive);
    }

    [Fact]
    public void Succeeded_y_failed_son_finales()
    {
        var done = NewJob();
        done.MarkSucceeded(Guid.NewGuid(), T0);
        Assert.Throws<InvalidOperationException>(() => done.MarkRunning(50, T0));
        Assert.Throws<InvalidOperationException>(() => done.MarkFailed("x", T0));

        var failed = NewJob();
        failed.MarkFailed(new string('x', 900), T0);
        Assert.Equal(500, failed.Error!.Length);
        Assert.Throws<InvalidOperationException>(() => failed.MarkSucceeded(Guid.NewGuid(), T0));
    }
}
