using Holograma3D.Core.Domain.Gestures;
using Holograma3D.Core.Domain.Models;

namespace Holograma3D.Core.Ports;

/// <summary>Avisa en tiempo real a los clientes de una sesión.</summary>
public interface IHologramBroadcaster
{
    /// <param name="srcT">t del HandFrame que la produjo; 0 si no vino de un frame.</param>
    Task TransformChanged(string code, HologramTransform transform, long seq, double srcT);

    Task GestureChanged(string code, GestureState state, int hands);

    Task ModelChanged(string code, Model3D? model);

    /// <summary>Solo al control de la sesión.</summary>
    Task DisplaysChanged(string code, int count);
}
