using UnityEngine;

namespace Arena.NetworkedMotion
{
    /// <summary>Ballistic arc: constant cruise speed along facing plus vertical gravity over time.</summary>
    [System.Serializable]
    public sealed class ArcMotion : IMotionStrategy
    {
        [SerializeField, Tooltip("Vertical gravity applied per second² (negative = down).")]
        private float gravity = -9.81f;

        public ArcMotion() { }
        public ArcMotion(float gravity) => this.gravity = gravity;

        public Vector3 PositionAt(in NetworkedMotionState s, float e) =>
            s.OriginPos + s.OriginForward * (s.OriginSpeed * e) + 0.5f * gravity * e * e * Vector3.up;

        public Vector3 VelocityAt(in NetworkedMotionState s, float e) =>
            s.OriginForward * s.OriginSpeed + gravity * e * Vector3.up;

        public MotionSweep CollisionSweep(in NetworkedMotionState s, Vector3 prevPos, Vector3 newPos) =>
            new(prevPos, newPos);
    }
}
