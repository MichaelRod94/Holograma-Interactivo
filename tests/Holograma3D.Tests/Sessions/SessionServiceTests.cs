using Holograma3D.Api.Realtime;
using Holograma3D.Core.Application;
using Holograma3D.Core.Domain.Gestures;
using Holograma3D.Core.Domain.Models;
using Holograma3D.Core.Domain.Sessions;
using Holograma3D.Core.Ports;
using Holograma3D.Infrastructure;
using Holograma3D.Infrastructure.Identity;
using Holograma3D.Infrastructure.Persistence;
using Holograma3D.Tests.Gestures;
using Holograma3D.Tests.Infrastructure;
using Holograma3D.Tests.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Holograma3D.Tests.Sessions;

[Collection("db")]
public sealed class SessionServiceTests(PostgresFixture db) : IAsyncLifetime
{
    private readonly string _a = Guid.NewGuid().ToString(), _b = Guid.NewGuid().ToString();
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "holograma3d-tests", Guid.NewGuid().ToString("N"));
    private readonly InMemorySessionStore _store = new();
    private readonly RecordingBroadcaster _broadcast = new();
    private AppDbContext _context = null!;
    private ModelCatalog _catalog = null!;
    private SessionService _sessions = null!;

    public async Task InitializeAsync()
    {
        _context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(db.ConnectionString).Options);
        await _context.Database.MigrateAsync();
        _context.Users.AddRange(new AppUser { Id = _a, UserName = _a }, new AppUser { Id = _b, UserName = _b });
        await _context.SaveChangesAsync();
        _catalog = new ModelCatalog(new EfModelStore(_context, _dir, NullLogger<EfModelStore>.Instance), TimeProvider.System);
        _sessions = new SessionService(_store, _broadcast, _catalog, TimeProvider.System, Hands.NoFilter);
    }

    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private Task<Model3D> UploadAsync(string ownerId, byte[] bytes) =>
        _catalog.UploadAsync(ownerId, "cubo.stl", null, new MemoryStream(bytes), bytes.Length, ModelSource.Upload, default);

    private static HandFrame Fist(double t, float x) => new(t, [Hands.Fist(x)]);

    [Fact]
    public async Task Crear_unirse_y_salir_avisa_al_control()
    {
        var session = await _sessions.CreateAsync(_a, "control");
        Assert.Matches($"^[{SessionCode.Alphabet}]{{6}}$", session.Code);

        var joined = await _sessions.JoinAsync(session.Code.ToLowerInvariant()); // el código no distingue mayúsculas
        Assert.Equal(1, joined.Displays);
        await _sessions.LeaveAsync(session.Code);
        await _sessions.LeaveAsync(session.Code);

        Assert.Equal([1, 0, 0], _broadcast.Displays);
    }

    [Fact]
    public async Task Sesion_ajena_o_inexistente_lanza()
    {
        var session = await _sessions.CreateAsync(_a, "control");

        var ex = await Assert.ThrowsAsync<SessionException>(() => _sessions.ResumeAsync(session.Code, _b, "otra"));
        Assert.Contains("no existe", ex.Message);
        await Assert.ThrowsAsync<SessionException>(() => _sessions.JoinAsync("ZZZZZZ"));
    }

    [Fact]
    public async Task Puno_sostenido_y_movido_mueve_el_modelo_con_seq_creciente()
    {
        var session = await _sessions.CreateAsync(_a, "control");

        // Desde otra conexión no pasa nada.
        for (var i = 0; i < 4; i++)
            await _sessions.ProcessFrameAsync(session.Code, "intrusa", Fist(i * 33, 0.5f + i * 0.1f));
        Assert.Empty(_broadcast.Transforms);

        for (var i = 0; i < 3; i++)
            await _sessions.ProcessFrameAsync(session.Code, "control", Fist(i * 33, 0.5f));
        await _sessions.ProcessFrameAsync(session.Code, "control", Fist(100, 0.6f));

        Assert.Contains((GestureState.Grab, 1), _broadcast.Gestures);
        var seqs = _broadcast.Transforms.Select(t => t.Seq).ToArray();
        Assert.Equal(seqs.Order().Distinct(), seqs);
        Assert.True(_broadcast.Transforms[^1].Transform.Position.X > 0);
        Assert.Equal(100, _broadcast.Transforms[^1].SrcT);
    }

    [Fact]
    public async Task Retomar_pasa_el_control_a_la_conexion_nueva()
    {
        var session = await _sessions.CreateAsync(_a, "pestana-1");
        await _sessions.ResumeAsync(session.Code, _a, "pestana-2");

        for (var i = 0; i < 4; i++)
            await _sessions.ProcessFrameAsync(session.Code, "pestana-1", Fist(i * 33, 0.5f + i * 0.1f));
        Assert.Empty(_broadcast.Transforms);
        await Assert.ThrowsAsync<SessionException>(() => _sessions.ResetAsync(session.Code, _a, "pestana-1"));
    }

    [Fact]
    public async Task Solo_se_selecciona_un_modelo_propio_y_el_display_lo_descarga()
    {
        var bytes = ModelFiles.BinaryStl(3);
        var own = await UploadAsync(_a, bytes);
        var foreign = await UploadAsync(_b, ModelFiles.BinaryStl(1));
        var session = await _sessions.CreateAsync(_a, "control");

        await Assert.ThrowsAsync<SessionException>(() => _sessions.SelectModelAsync(session.Code, _a, "control", foreign.Id));
        Assert.Null(await _sessions.OpenActiveModelAsync(session.Code));

        await _sessions.SelectModelAsync(session.Code, _a, "control", own.Id);

        Assert.Equal([own.Id], _broadcast.Models.Select(m => m?.Id));
        Assert.Equal(0, _broadcast.Transforms[^1].SrcT);
        Assert.Equal(own.Id, (await _sessions.JoinAsync(session.Code)).Model?.Id);
        var file = await _sessions.OpenActiveModelAsync(session.Code);
        await using var content = file!.Value.Content;
        var copy = new MemoryStream();
        await content.CopyToAsync(copy);
        Assert.Equal(bytes, copy.ToArray());
    }

    [Fact]
    public async Task Las_sesiones_inactivas_se_borran()
    {
        var idle = await _sessions.CreateAsync(_a, "c1");
        var active = await _sessions.CreateAsync(_a, "c2");
        var now = DateTimeOffset.UtcNow;
        var session = _store.Get(idle.Code)!;
        lock (session.Gate)
            session.LastActivity = now - SessionCleanup.MaxIdle - TimeSpan.FromMinutes(1);

        SessionCleanup.RemoveIdle(_store, now - SessionCleanup.MaxIdle);

        Assert.Null(_store.Get(idle.Code));
        Assert.NotNull(_store.Get(active.Code));
    }

    private sealed class RecordingBroadcaster : IHologramBroadcaster
    {
        public List<(HologramTransform Transform, long Seq, double SrcT)> Transforms { get; } = [];
        public List<(GestureState State, int Hands)> Gestures { get; } = [];
        public List<Model3D?> Models { get; } = [];
        public List<int> Displays { get; } = [];

        public Task TransformChanged(string code, HologramTransform transform, long seq, double srcT)
        {
            Transforms.Add((transform, seq, srcT));
            return Task.CompletedTask;
        }

        public Task GestureChanged(string code, GestureState state, int hands)
        {
            Gestures.Add((state, hands));
            return Task.CompletedTask;
        }

        public Task ModelChanged(string code, Model3D? model)
        {
            Models.Add(model);
            return Task.CompletedTask;
        }

        public Task DisplaysChanged(string code, int count)
        {
            Displays.Add(count);
            return Task.CompletedTask;
        }
    }
}
