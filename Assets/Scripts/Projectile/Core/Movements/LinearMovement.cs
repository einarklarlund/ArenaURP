using UnityEngine;

namespace Arena.Projectile
{
    /// <summary>Straight line with optional constant acceleration along the facing direction.</summary>
    public sealed class LinearMovement : IProjectileMovement
    {
        public Vector3 PositionAt(in ProjectileState s, in ProjectileType t, float e) =>
            s.OriginPos + s.OriginForward * (s.OriginSpeed * e + 0.5f * t.Acceleration * e * e);

        public Vector3 VelocityAt(in ProjectileState s, in ProjectileType t, float e) =>
            s.OriginForward * (s.OriginSpeed + t.Acceleration * e);
    }
}
