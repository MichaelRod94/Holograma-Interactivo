namespace Holograma3D.Core.Domain.Gestures;

public enum HandPose
{
    Other,
    Fist,
    Open,

    /// <summary>La seña de rotar con una sola mano (por defecto ✌️, "Victory"; ver <see cref="GestureOptions.RotateGesture"/>).</summary>
    RotateSign,
}
