namespace Holograma3D.Core.Domain.Gestures;

/// <summary>Las manos detectadas en un cuadro de cámara.</summary>
public sealed record HandFrame(double TimestampMs, IReadOnlyList<HandObservation> Hands)
{
    // Con dos manos, índice 0 = izquierda en pantalla. No se usa la lateralidad de MediaPipe: se invierte con el espejo.
    public IReadOnlyList<HandObservation> Hands { get; } =
        Hands.Count == 2 && Hands[0].Center.X > Hands[1].Center.X ? [Hands[1], Hands[0]] : Hands;
}
