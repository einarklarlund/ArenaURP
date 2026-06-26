using UnityEngine;

namespace Arena.NetworkedMotion
{
    /// <summary>
    /// Stationary beam: zero velocity, fixed position. The hitscan is expressed by the shell casting along
    /// <see cref="NetworkedMotionState.OriginForward"/> over <see cref="MotionConfig.MaxRange"/> each tick.
    /// Facing is preserved even though speed is 0.
    /// </summary>
    public sealed class LaserMotion : IMotionStrategy
    {
        public Vector3 PositionAt(in NetworkedMotionState s, in MotionConfig c, float e) => s.OriginPos;

        public Vector3 VelocityAt(in NetworkedMotionState s, in MotionConfig c, float e) => Vector3.zero;
    }
}
