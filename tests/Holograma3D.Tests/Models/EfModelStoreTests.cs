using Holograma3D.Core.Domain.Models;
using Holograma3D.Infrastructure;
using Holograma3D.Infrastructure.Identity;
using Holograma3D.Infrastructure.Persistence;
using Holograma3D.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Holograma3D.Tests.Models;

[Collection("db")]
public sealed class EfModelStoreTests(PostgresFixture db) : IAsyncLifetime
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "holograma3d-tests", Guid.NewGuid().ToString("N"));
    private readonly string _a = Guid.NewGuid().ToString(), _b = Guid.NewGuid().ToString();
    private AppDbContext _context = null!;
    private EfModelStore _store = null!;

    public async Task InitializeAsync()
    {
        _context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(db.ConnectionString).Options);
        await _context.Database.MigrateAsync();
        _context.Users.AddRange(new AppUser { Id = _a, UserName = _a }, new AppUser { Id = _b, UserName = _b });
        await _context.SaveChangesAsync();
        _store = new EfModelStore(_context, _dir, NullLogger<EfModelStore>.Instance);
    }

    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public async Task Guarda_lista_lee_y_borra_solo_para_su_dueno()
    {
        var bytes = ModelFiles.BinaryStl(2);
        var model = new Model3D(Guid.NewGuid(), _a, "Cubo", ModelFormat.Stl, bytes.Length, 2, ModelSource.Upload, DateTimeOffset.UtcNow);

        await _store.SaveAsync(model, new MemoryStream(bytes), default);

        Assert.Equal([model.Id], (await _store.ListAsync(_a, default)).Select(m => m.Id));
        Assert.Empty(await _store.ListAsync(_b, default));
        Assert.Equal("Cubo", (await _store.GetAsync(model.Id, _a, default))?.Name);
        Assert.Null(await _store.GetAsync(model.Id, _b, default));
        Assert.Null(await _store.OpenReadAsync(model.Id, _b, default));

        await using (var stream = await _store.OpenReadAsync(model.Id, _a, default))
        {
            var copy = new MemoryStream();
            await stream!.CopyToAsync(copy);
            Assert.Equal(bytes, copy.ToArray());
        }

        Assert.False(await _store.DeleteAsync(model.Id, _b, default));
        Assert.True(await _store.DeleteAsync(model.Id, _a, default));
        Assert.Null(await _store.GetAsync(model.Id, _a, default));
        Assert.Empty(Directory.GetFiles(_dir));
    }
}
