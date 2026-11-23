using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Holograma3D.Api.Contracts;
using Holograma3D.Tests.Gestures;
using Holograma3D.Tests.Infrastructure;
using Holograma3D.Tests.Models;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;

namespace Holograma3D.Tests.Sessions;

[Collection("db")]
public sealed class HologramHubTests(PostgresFixture db) : IDisposable
{
    private readonly ApiFactory _factory = new(db);

    public void Dispose() => _factory.Dispose();

    /// <summary>TestServer no ofrece WebSockets por handler: long polling.</summary>
    private HubConnection Hub(string? token = null) => new HubConnectionBuilder()
        .WithUrl(new Uri(_factory.Server.BaseAddress, "hubs/hologram"), o =>
        {
            o.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
            o.Transports = HttpTransportType.LongPolling;
            if (token is not null)
                o.AccessTokenProvider = () => Task.FromResult<string?>(token);
        })
        .Build();

    private async Task<(HttpClient Client, string Token)> UserAsync()
    {
        var client = _factory.CreateClient();
        var (_, token, _) = await AuthHelper.RegisterAndLoginAsync(client);
        return (client, token);
    }

    private static HandFrameDto Fist(double t, float rawX) => new(t, [new HandDto("Closed_Fist", 0.9f, Hands.Landmarks(rawX, 0.5f))]);

    [Fact]
    public async Task El_display_sin_cuenta_recibe_el_movimiento_del_control()
    {
        var (_, token) = await UserAsync();
        await using var control = Hub(token);
        await using var display = Hub();
        var displays = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var moved = new TaskCompletionSource<TransformDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        control.On<int>("DisplaysChanged", n => displays.TrySetResult(n));
        display.On<TransformDto>("TransformChanged", t =>
        {
            if (t.Px > 0)
                moved.TrySetResult(t);
        });
        await control.StartAsync();
        await display.StartAsync();

        var session = await control.InvokeAsync<SessionSnapshotDto>("CreateSession");
        var joined = await display.InvokeAsync<SessionSnapshotDto>("JoinSession", session.Code);
        Assert.Equal(session.Code, joined.Code);
        Assert.EndsWith($"/?s={session.Code}", session.DisplayUrl);
        Assert.Equal("idle", joined.Gesture.State);
        Assert.Equal(1, await displays.Task.WaitAsync(TimeSpan.FromSeconds(5)));

        // Puño quieto 3 frames (se activa GRAB) y luego a la derecha en pantalla: la cámara no viene en espejo.
        for (var i = 0; i < 6; i++)
        {
            await control.InvokeAsync("SendHandFrame", Fist(i * 33, i < 3 ? 0.5f : 0.5f - (i - 2) * 0.05f));
            await Task.Delay(20); // el hub descarta frames a menos de 15 ms
        }

        var transform = await moved.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(transform.Seq > 0);
        Assert.True(transform.SrcT > 0);
    }

    [Fact]
    public async Task Sin_token_no_se_crea_sesion_ni_se_entra_a_una_inexistente()
    {
        await using var anonymous = Hub();
        await anonymous.StartAsync();

        await Assert.ThrowsAsync<HubException>(() => anonymous.InvokeAsync<SessionSnapshotDto>("CreateSession"));
        var ex = await Assert.ThrowsAsync<HubException>(() => anonymous.InvokeAsync<SessionSnapshotDto>("JoinSession", "ZZZZZZ"));
        Assert.Contains("no existe", ex.Message);
    }

    [Fact]
    public async Task El_display_descarga_el_modelo_y_el_qr_con_el_codigo()
    {
        var (client, token) = await UserAsync();
        var bytes = ModelFiles.BinaryStl(5);
        var upload = await client.PostAsync("/api/models", new MultipartFormDataContent { { new ByteArrayContent(bytes), "file", "cubo.stl" } });
        var modelId = (await upload.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await using var control = Hub(token);
        await control.StartAsync();
        var code = (await control.InvokeAsync<SessionSnapshotDto>("CreateSession")).Code;
        var anonymous = _factory.CreateClient();

        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/sessions/{code}/model")).StatusCode);
        await control.InvokeAsync("SelectModel", (Guid?)modelId);

        var model = await anonymous.GetAsync($"/api/sessions/{code}/model?v={modelId}");
        Assert.Equal(HttpStatusCode.OK, model.StatusCode);
        Assert.Equal("model/stl", model.Content.Headers.ContentType?.MediaType);
        Assert.Equal(bytes, await model.Content.ReadAsByteArrayAsync());

        var qr = await anonymous.GetAsync($"/api/sessions/{code}/qr.svg");
        Assert.Equal(HttpStatusCode.OK, qr.StatusCode);
        Assert.Equal("image/svg+xml", qr.Content.Headers.ContentType?.MediaType);
        Assert.Contains("<svg", await qr.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync("/api/sessions/ZZZZZZ/qr.svg")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync("/api/sessions/ZZZZZZ/model")).StatusCode);
    }
}
