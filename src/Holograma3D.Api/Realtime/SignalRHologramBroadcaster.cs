using Holograma3D.Api.Contracts;
using Holograma3D.Core.Domain.Gestures;
using Holograma3D.Core.Domain.Models;
using Holograma3D.Core.Ports;
using Microsoft.AspNetCore.SignalR;

namespace Holograma3D.Api.Realtime;

public sealed class SignalRHologramBroadcaster(IHubContext<HologramHub, IHologramClient> hub) : IHologramBroadcaster
{
    public Task TransformChanged(string code, HologramTransform transform, long seq, double srcT) =>
        hub.Clients.Group(HologramGroups.Session(code)).TransformChanged(transform.ToDto(seq, srcT));

    public Task GestureChanged(string code, GestureState state, int hands) =>
        hub.Clients.Group(HologramGroups.Session(code)).GestureChanged(state.ToDto(hands));

    public Task ModelChanged(string code, Model3D? model) =>
        hub.Clients.Group(HologramGroups.Session(code)).ModelChanged(model?.ToDto());

    public Task DisplaysChanged(string code, int count) =>
        hub.Clients.Group(HologramGroups.Control(code)).DisplaysChanged(count);
}
