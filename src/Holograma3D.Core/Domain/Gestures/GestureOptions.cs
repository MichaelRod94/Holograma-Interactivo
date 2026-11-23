namespace Holograma3D.Core.Domain.Gestures;

/// <summary>
/// Perillas de calibración del intérprete de gestos. La Api las carga de la sección "Gestures"
/// de appsettings para ajustarlas sin recompilar.
/// </summary>
public sealed class GestureOptions
{
    /// <summary>Confianza mínima de MediaPipe; por debajo, la pose cuenta como "otra".</summary>
    public float MinScore { get; init; } = 0.6f;

    /// <summary>Frames seguidos que debe durar un gesto candidato para activarse (histéresis).</summary>
    public int StableFrames { get; init; } = 3;

    /// <summary>El usuario se ve en espejo: se invierte X para que mover la mano a la derecha mueva el modelo a la derecha.</summary>
    public bool MirrorX { get; init; } = true;

    /// <summary>
    /// Categoría de MediaPipe que rota con una sola mano: moverla gira el modelo y girar la muñeca lo inclina.
    /// "Victory" (✌️) por defecto; también sirven "Pointing_Up", "Thumb_Up" o "ILoveYou". Vacía = solo con dos puños.
    /// </summary>
    public string RotateGesture { get; init; } = "Victory";

    public bool FilterEnabled { get; init; } = true;
    public float FilterMinCutoff { get; init; } = 1.0f;
    public float FilterBeta { get; init; } = 0.02f;
    public float FilterDCutoff { get; init; } = 1.0f;

    /// <summary>Unidades de mundo por unidad normalizada de imagen al arrastrar.</summary>
    public float GainXY { get; init; } = 2.5f;

    /// <summary>Ganancia de profundidad sobre ln(tamaño / tamaño del ancla).</summary>
    public float GainZ { get; init; } = 1.5f;

    /// <summary>Desplazamientos menores a esto (en coordenadas de imagen) se ignoran al arrastrar.</summary>
    public float DeadZone { get; init; } = 0.005f;

    public float ZoomExponent { get; init; } = 1.0f;

    /// <summary>Distancia mínima entre manos al anclar el zoom; por debajo, la razón es inestable.</summary>
    public float MinHandDistance { get; init; } = 0.05f;

    /// <summary>Radianes por unidad normalizada de desplazamiento al rotar.</summary>
    public float RotGain { get; init; } = 3.5f;

    public float MinScale { get; init; } = 0.2f;
    public float MaxScale { get; init; } = 5.0f;

    /// <summary>Límite por eje de la posición, en radios del modelo normalizado.</summary>
    public float MaxOffset { get; init; } = 1.5f;
}
