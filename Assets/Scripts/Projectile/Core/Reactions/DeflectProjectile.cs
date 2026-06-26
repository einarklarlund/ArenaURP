using System.Collections.Generic;
using UnityEngine;

namespace Arena.Projectile
{
    /// <summary>
    /// Redirects this projectile along the OTHER projectile's direction, preserving this one's speed
    /// (ports the old ProjectileBulletImpact). Falls back to the other's velocity direction.
    /// </summary>
    [System.Serializable]
    public sealed class DeflectProjectile : IHitReaction
    {
        public void Apply(in HitInfo hit, ref ProjectileState self, List<WorldEffect> effects)
        {
            Vector3 dir = hit.OtherForward.sqrMagnitude > 0.0001f ? hit.OtherForward : hit.OtherVelocity;
            if (dir.sqrMagnitude < 0.0001f)
                return;

            float speed = hit.IncomingVelocity.magnitude;
            if (speed < 0.0001f)
                speed = self.OriginSpeed;

            self = ProjectileState.BeginSegment(self, hit.Point, dir.normalized, speed);
        }
    }
}
