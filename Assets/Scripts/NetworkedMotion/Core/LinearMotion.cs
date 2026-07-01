using UnityEngine;

namespace Arena.NetworkedMotion
{
    /// <summary>Straight line with optional constant acceleration along the facing direction.</summary>
    [System.Serializable]
    public sealed class LinearMotion : IMotionStrategy
    {
        [SerializeField, Tooltip("Constant acceleration along facing (units/s²). 0 = constant speed.")]
        private float acceleration = 0f;

        public LinearMotion() { }
        public LinearMotion(float acceleration) => this.acceleration = acceleration;

        public Vector3 PositionAt(in NetworkedMotionState s, float e) =>
            s.OriginPos + s.OriginForward * (s.OriginSpeed * e + 0.5f * acceleration * e * e);

        public Vector3 VelocityAt(in NetworkedMotionState s, float e) =>
            s.OriginForward * (s.OriginSpeed + acceleration * e);

        public MotionSweep CollisionSweep(in NetworkedMotionState s, Vector3 prevPos, Vector3 newPos) =>
            new(prevPos, newPos);
    }
}
