using System.Numerics;

namespace Holograma3D.Core.Domain.Gestures;

/// <summary>
/// Convierte cuadros de manos en un estado de gesto y una transformación del holograma.
/// Una instancia por sesión; no es segura para hilos (quien la usa la protege con un lock).
/// </summary>
public sealed class GestureInterpreter
{
    private const float ChangeTolerance = 1e-5f;
    private const float MinSize = 1e-4f;

    private readonly GestureOptions _o;
    private readonly OneEuroFilter[,] _filters = new OneEuroFilter[2, 3]; // slot (izq/der) × (x, y, tamaño)

    private GestureState _candidate;
    private int _candidateFrames;

    // Ancla: pose de las manos y transformación al entrar al estado actual.
    private HologramTransform _t0;
    private Vector2 _c0, _c1;
    private float _s0, _d0, _a0;

    public GestureInterpreter(GestureOptions options)
    {
        _o = options;
        for (var slot = 0; slot < 2; slot++)
            for (var axis = 0; axis < 3; axis++)
                _filters[slot, axis] = new OneEuroFilter(options.FilterMinCutoff, options.FilterBeta, options.FilterDCutoff);
        Reset();
    }

    public GestureState State { get; private set; }
    public HologramTransform Transform { get; private set; }
    public int HandCount { get; private set; }

    /// <returns>true si cambió el estado o la transformación.</returns>
    public bool Apply(HandFrame frame)
    {
        var hands = Smooth(frame);
        var previousState = State;
        var previousTransform = Transform;
        HandCount = hands.Length;

        var candidate = Classify(hands);
        _candidateFrames = candidate == _candidate ? _candidateFrames + 1 : 1;
        _candidate = candidate;

        if (_candidate != State && _candidateFrames >= Math.Max(1, _o.StableFrames))
        {
            State = _candidate;
            Anchor(hands);
        }

        // Solo se mueve cuando las manos coinciden con el estado activo; durante una transición, se congela.
        if (State != GestureState.Idle && candidate == State)
            Transform = FromAnchor(hands).Clamped(_o);

        return State != previousState || !Transform.IsCloseTo(previousTransform, ChangeTolerance);
    }

    public void Reset()
    {
        State = GestureState.Idle;
        Transform = HologramTransform.Identity;
        HandCount = 0;
        _candidate = GestureState.Idle;
        _candidateFrames = 0;
        _t0 = Transform;
        foreach (var filter in _filters)
            filter.Reset();
    }

    private HandObservation[] Smooth(HandFrame frame)
    {
        var hands = frame.Hands.ToArray();
        if (hands.Length != HandCount)
            foreach (var filter in _filters)
                filter.Reset(); // cambió el número de manos: los slots ya no son las mismas manos

        if (!_o.FilterEnabled)
            return hands;

        for (var slot = 0; slot < Math.Min(hands.Length, 2); slot++)
        {
            var h = hands[slot];
            var t = frame.TimestampMs;
            hands[slot] = h with
            {
                Center = new Vector2(_filters[slot, 0].Filter(h.Center.X, t), _filters[slot, 1].Filter(h.Center.Y, t)),
                Size = _filters[slot, 2].Filter(h.Size, t),
            };
        }
        return hands;
    }

    private static GestureState Classify(HandObservation[] hands) => hands switch
    {
        [{ Pose: HandPose.Fist }] => GestureState.Grab,
        [{ Pose: HandPose.Open }, { Pose: HandPose.Open }] => GestureState.Zoom,
        [{ Pose: HandPose.Fist }, { Pose: HandPose.Fist }] => GestureState.Rotate,
        _ => GestureState.Idle,
    };

    private void Anchor(HandObservation[] hands)
    {
        _t0 = Transform;
        if (hands.Length == 0)
            return;

        _c0 = hands[0].Center;
        _s0 = hands[0].Size;
        if (hands.Length == 2)
        {
            _c1 = hands[1].Center;
            _d0 = Vector2.Distance(_c0, _c1);
            _a0 = Angle(_c0, _c1);
        }
    }

    private HologramTransform FromAnchor(HandObservation[] hands)
    {
        switch (State)
        {
            case GestureState.Grab:
            {
                var d = hands[0].Center - _c0;
                if (MathF.Abs(d.X) < _o.DeadZone) d.X = 0;
                if (MathF.Abs(d.Y) < _o.DeadZone) d.Y = 0;
                var size = hands[0].Size;
                var dz = _s0 > MinSize && size > MinSize ? _o.GainZ * MathF.Log(size / _s0) : 0f;
                // La imagen tiene Y hacia abajo; el mundo, Y hacia arriba.
                return _t0 with { Position = _t0.Position + new Vector3(d.X * _o.GainXY, -d.Y * _o.GainXY, dz) };
            }
            case GestureState.Zoom:
            {
                if (_d0 < _o.MinHandDistance)
                    return Transform;
                var distance = Vector2.Distance(hands[0].Center, hands[1].Center);
                return _t0 with { Scale = _t0.Scale * MathF.Pow(distance / _d0, _o.ZoomExponent) };
            }
            case GestureState.Rotate:
            {
                var delta = (hands[0].Center + hands[1].Center) / 2f - (_c0 + _c1) / 2f;
                var yaw = delta.X * _o.RotGain;   // manos a la derecha → el frente gira a la derecha
                var pitch = delta.Y * _o.RotGain; // manos abajo → el frente se inclina hacia abajo
                var roll = -MathF.IEEERemainder(Angle(hands[0].Center, hands[1].Center) - _a0, 2f * MathF.PI);
                var rotation =
                    Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw) *
                    Quaternion.CreateFromAxisAngle(Vector3.UnitX, pitch) *
                    Quaternion.CreateFromAxisAngle(Vector3.UnitZ, roll);
                // Delta en espacio mundo: con * de System.Numerics se aplica primero el factor de la derecha.
                return _t0 with { Rotation = Quaternion.Normalize(rotation * _t0.Rotation) };
            }
            default:
                return Transform;
        }
    }

    private static float Angle(Vector2 from, Vector2 to) => MathF.Atan2(to.Y - from.Y, to.X - from.X);
}
