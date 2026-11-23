using Holograma3D.Core.Application;
using Holograma3D.Core.Domain.Generation;
using Holograma3D.Core.Domain.Models;
using Holograma3D.Core.Ports;
using Holograma3D.Tests.Models;

namespace Holograma3D.Tests.Generation;

public sealed class GenerationServiceTests
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4];

    private readonly ManualTime _time = new();
    private readonly ScriptedGenerator _generator = new();
    private readonly InMemoryJobStore _jobs = new();
    private readonly InMemoryModelStore _models = new();
    private readonly GenerationService _service;

    public GenerationServiceTests() =>
        _service = new GenerationService(_generator, _jobs, new ModelCatalog(_models, _time), _time,
            new GenerationSettings(TimeSpan.FromMinutes(15), MaxActiveJobsPerUser: 2));

    private static GenerationImage Photo(byte[]? data = null) => new("foto.png", "image/png", data ?? Png);

    private Task<GenerationJob> SubmitAsync(string owner = "u") => _service.SubmitAsync(owner, [Photo(), Photo()], "  Taza  ", default);

    [Fact]
    public async Task Crea_el_trabajo_en_queued()
    {
        var job = await SubmitAsync();

        Assert.Equal((GenerationStatus.Queued, "Taza", "Scripted"), (job.Status, job.Name, job.Provider));
        Assert.Equal(2, _generator.Submitted[0].Count);
        Assert.Same(job, Assert.Single(_jobs.All));
    }

    [Fact]
    public async Task Valida_las_fotos()
    {
        await Assert.ThrowsAsync<ModelValidationException>(() =>
            _service.SubmitAsync("u", [Photo("GIF89a....."u8.ToArray())], null, default));
        await Assert.ThrowsAsync<ModelValidationException>(() =>
            _service.SubmitAsync("u", [Photo(), Photo(), Photo(), Photo(), Photo()], null, default)); // MaxImages = 4
        await Assert.ThrowsAsync<ModelValidationException>(() => _service.SubmitAsync("u", [], null, default));
        await Assert.ThrowsAsync<ModelValidationException>(() =>
            _service.SubmitAsync("u", [Photo([.. Png, .. new byte[GenerationService.MaxImageBytes]])], null, default));
        Assert.Empty(_generator.Submitted);
    }

    [Fact]
    public async Task Respeta_el_limite_de_trabajos_activos_por_usuario()
    {
        await SubmitAsync();
        await SubmitAsync();

        await Assert.ThrowsAsync<GenerationLimitException>(() => SubmitAsync());
        await SubmitAsync("otro"); // el límite es por usuario
    }

    [Fact]
    public async Task Deshabilitado_o_proveedor_caido()
    {
        _generator.Enabled = false;
        await Assert.ThrowsAsync<GenerationDisabledException>(() => SubmitAsync());

        _generator.Enabled = true;
        _generator.SubmitError = new HttpRequestException("Meshy respondió 402: sin créditos");
        var ex = await Assert.ThrowsAsync<GenerationProviderException>(() => SubmitAsync());
        Assert.Contains("sin créditos", ex.Message);
        Assert.Empty(_jobs.All);
    }

    [Fact]
    public async Task Cuando_termina_guarda_el_modelo_en_el_catalogo()
    {
        var job = await SubmitAsync();

        _generator.Next = new ProviderTaskStatus(ProviderTaskState.Running, 45);
        await _service.PollOnceAsync(default);
        Assert.Equal((GenerationStatus.Running, 45), (job.Status, job.Progress));

        _generator.Next = new ProviderTaskStatus(ProviderTaskState.Succeeded);
        Assert.Empty(await _service.PollOnceAsync(default));

        Assert.Equal(GenerationStatus.Succeeded, job.Status);
        var model = Assert.Single(await _models.ListAsync("u", default));
        Assert.Equal((job.ModelId, ModelSource.Generated, "Taza", ModelFormat.Stl), (model.Id, model.Source, model.Name, model.Format));
    }

    [Fact]
    public async Task Fallo_del_proveedor_tiempo_agotado_y_proveedor_cambiado()
    {
        var failed = await SubmitAsync();
        _generator.Next = new ProviderTaskStatus(ProviderTaskState.Failed, Error: "La foto está borrosa");
        await _service.PollOnceAsync(default);
        Assert.Equal((GenerationStatus.Failed, "La foto está borrosa"), (failed.Status, failed.Error));

        var slow = await SubmitAsync();
        _generator.Next = new ProviderTaskStatus(ProviderTaskState.Running, 10);
        _time.Now += TimeSpan.FromMinutes(16);
        await _service.PollOnceAsync(default);
        Assert.Equal((GenerationStatus.Failed, "Tiempo agotado."), (slow.Status, slow.Error));

        var orphan = await SubmitAsync();
        _generator.Name = "OtroProveedor";
        await _service.PollOnceAsync(default);
        Assert.Equal((GenerationStatus.Failed, "El proveedor configurado cambió."), (orphan.Status, orphan.Error));
    }

    [Fact]
    public async Task Un_error_transitorio_deja_el_trabajo_para_el_siguiente_ciclo()
    {
        var job = await SubmitAsync();
        _generator.StatusError = new HttpRequestException("503");

        var errors = await _service.PollOnceAsync(default);

        Assert.Equal(job.Id, Assert.Single(errors).JobId);
        Assert.Equal(GenerationStatus.Queued, job.Status);
    }

    [Fact]
    public async Task Un_resultado_que_no_es_un_modelo_falla_con_motivo()
    {
        var job = await SubmitAsync();
        _generator.Result = "<html>no es un STL</html>"u8.ToArray();
        _generator.Next = new ProviderTaskStatus(ProviderTaskState.Succeeded);

        await _service.PollOnceAsync(default);

        Assert.Equal(GenerationStatus.Failed, job.Status);
        Assert.StartsWith("El modelo generado no es válido", job.Error);
        Assert.Empty(await _models.ListAsync("u", default));
    }

    // ---------- Dobles de prueba ----------

    private sealed class ManualTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class ScriptedGenerator : IImageTo3DGenerator
    {
        public string Name { get; set; } = "Scripted";
        public bool Enabled { get; set; } = true;
        public int MaxImages => 4;
        public string OutputFormat => "stl";
        public ProviderTaskStatus Next { get; set; } = new(ProviderTaskState.Pending);
        public Exception? SubmitError { get; set; }
        public Exception? StatusError { get; set; }
        public byte[] Result { get; set; } = ModelFiles.BinaryStl(8);
        public List<IReadOnlyList<GenerationImage>> Submitted { get; } = [];

        public Task<string> SubmitAsync(IReadOnlyList<GenerationImage> images, CancellationToken ct)
        {
            if (!Enabled)
                throw new GenerationDisabledException();
            if (SubmitError is not null)
                throw SubmitError;
            Submitted.Add(images);
            return Task.FromResult(Guid.NewGuid().ToString());
        }

        public Task<ProviderTaskStatus> GetStatusAsync(string providerTaskId, CancellationToken ct) =>
            StatusError is not null ? Task.FromException<ProviderTaskStatus>(StatusError) : Task.FromResult(Next);

        public Task<Stream> DownloadResultAsync(string providerTaskId, CancellationToken ct) => Task.FromResult<Stream>(new MemoryStream(Result));
    }

    private sealed class InMemoryJobStore : IGenerationJobStore
    {
        public List<GenerationJob> All { get; } = [];

        public Task AddAsync(GenerationJob job, CancellationToken ct)
        {
            All.Add(job);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(GenerationJob job, CancellationToken ct) => Task.CompletedTask; // mismas instancias

        public Task<GenerationJob?> GetAsync(Guid id, string ownerId, CancellationToken ct) =>
            Task.FromResult(All.FirstOrDefault(j => j.Id == id && j.OwnerId == ownerId));

        public Task<IReadOnlyList<GenerationJob>> ListAsync(string ownerId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<GenerationJob>>(All.Where(j => j.OwnerId == ownerId).ToList());

        public Task<IReadOnlyList<GenerationJob>> ListActiveAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<GenerationJob>>(All.Where(j => j.IsActive).ToList());

        public Task<int> CountActiveAsync(string ownerId, CancellationToken ct) =>
            Task.FromResult(All.Count(j => j.IsActive && j.OwnerId == ownerId));
    }

    private sealed class InMemoryModelStore : IModelStore
    {
        private readonly Dictionary<Guid, (Model3D Model, byte[] Data)> _models = [];

        public async Task SaveAsync(Model3D model, Stream content, CancellationToken ct)
        {
            var copy = new MemoryStream();
            await content.CopyToAsync(copy, ct);
            _models[model.Id] = (model, copy.ToArray());
        }

        public Task<IReadOnlyList<Model3D>> ListAsync(string ownerId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Model3D>>(_models.Values.Select(m => m.Model).Where(m => m.OwnerId == ownerId).ToList());

        public Task<Model3D?> GetAsync(Guid id, string ownerId, CancellationToken ct) =>
            Task.FromResult(_models.TryGetValue(id, out var m) && m.Model.OwnerId == ownerId ? m.Model : null);

        public Task<Stream?> OpenReadAsync(Guid id, string ownerId, CancellationToken ct) =>
            Task.FromResult<Stream?>(_models.TryGetValue(id, out var m) && m.Model.OwnerId == ownerId ? new MemoryStream(m.Data) : null);

        public Task<bool> DeleteAsync(Guid id, string ownerId, CancellationToken ct) =>
            Task.FromResult(_models.TryGetValue(id, out var m) && m.Model.OwnerId == ownerId && _models.Remove(id));
    }
}
