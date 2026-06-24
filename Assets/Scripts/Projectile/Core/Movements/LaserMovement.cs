using UnityEngine;

namespace Arena.Projectile
{
    /// <summary>
    /// Stationary beam: zero velocity, fixed position. The hitscan is expressed by the shell casting
    /// along <see cref="ProjectileState.OriginForward"/> over <see cref="ProjectileType.MaxRange"/> each
    /// tick (the processor builds that cast for the Laser kind). Facing is preserved even though speed is 0.
    /// </summary>
    public sealed class LaserMovement : IProjectileMovement
    {
        public Vector3 PositionAt(in ProjectileState s, in ProjectileType t, float e) => s.OriginPos;

        public Vector3 VelocityAt(in ProjectileState s, in ProjectileType t, float e) => Vector3.zero;
    }
}
