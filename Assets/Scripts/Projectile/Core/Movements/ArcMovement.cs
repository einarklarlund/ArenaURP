using UnityEngine;

namespace Arena.Projectile
{
    /// <summary>Ballistic arc: constant cruise speed along facing plus vertical gravity over time.</summary>
    public sealed class ArcMovement : IProjectileMovement
    {
        public Vector3 PositionAt(in ProjectileState s, in ProjectileType t, float e) =>
            s.OriginPos + s.OriginForward * (s.OriginSpeed * e) + 0.5f * t.Gravity * e * e * Vector3.up;

        public Vector3 VelocityAt(in ProjectileState s, in ProjectileType t, float e) =>
            s.OriginForward * s.OriginSpeed + t.Gravity * e * Vector3.up;
    }
}
