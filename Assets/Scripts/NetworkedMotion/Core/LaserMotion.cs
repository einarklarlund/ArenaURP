using UnityEngine;

namespace Arena.NetworkedMotion
{
    /// <summary>
    /// Stationary beam: zero velocity, fixed position; its collision sweep casts forward along facing over
    /// <see cref="maxRange"/> each tick (the hitscan). Facing is preserved even though speed is 0.
    /// </summary>
    [System.Serializable]
    public sealed class LaserMotion : IMotionStrategy
    {
        [SerializeField, Tooltip("Maximum beam length probed each tick.")]
        private float maxRange = 100f;

        public LaserMotion() { }
        public LaserMotion(float maxRange) => this.maxRange = maxRange;

        public Vector3 PositionAt(in NetworkedMotionState s, float e) => s.OriginPos;

        public Vector3 VelocityAt(in NetworkedMotionState s, float e) => Vector3.zero;

        public MotionSweep CollisionSweep(in NetworkedMotionState s, Vector3 prevPos, Vector3 newPos) =>
            new(newPos, newPos + s.OriginForward * maxRange);
    }
}
