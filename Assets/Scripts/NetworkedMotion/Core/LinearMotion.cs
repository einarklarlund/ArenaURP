using UnityEngine;

namespace Arena.NetworkedMotion
{
    /// <summary>Straight line with optional constant acceleration along the facing direction.</summary>
    public sealed class LinearMotion : IMotionStrategy
    {
        public Vector3 PositionAt(in NetworkedMotionState s, in MotionConfig c, float e) =>
            s.OriginPos + s.OriginForward * (s.OriginSpeed * e + 0.5f * c.Acceleration * e * e);

        public Vector3 VelocityAt(in NetworkedMotionState s, in MotionConfig c, float e) =>
            s.OriginForward * (s.OriginSpeed + c.Acceleration * e);
    }
}
