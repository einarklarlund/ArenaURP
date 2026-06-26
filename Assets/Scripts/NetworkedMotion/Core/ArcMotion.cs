using UnityEngine;

namespace Arena.NetworkedMotion
{
    /// <summary>Ballistic arc: constant cruise speed along facing plus vertical gravity over time.</summary>
    public sealed class ArcMotion : IMotionStrategy
    {
        public Vector3 PositionAt(in NetworkedMotionState s, in MotionConfig c, float e) =>
            s.OriginPos + s.OriginForward * (s.OriginSpeed * e) + 0.5f * c.Gravity * e * e * Vector3.up;

        public Vector3 VelocityAt(in NetworkedMotionState s, in MotionConfig c, float e) =>
            s.OriginForward * s.OriginSpeed + c.Gravity * e * Vector3.up;
    }
}
