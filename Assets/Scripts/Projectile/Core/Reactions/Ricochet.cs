using System.Collections.Generic;
using UnityEngine;

namespace Arena.Projectile
{
    /// <summary>Reflects the projectile's velocity across the surface normal and continues on a new segment.</summary>
    [System.Serializable]
    public sealed class Ricochet : IHitReaction
    {
        public void Apply(in HitInfo hit, ref ProjectileState self, List<ProjectileEffect> effects)
        {
            Vector3 reflected = Vector3.Reflect(hit.IncomingVelocity, hit.Normal);
            if (reflected.sqrMagnitude < 0.0001f)
                return;
            self = ProjectileState.BeginSegment(self, hit.Point, reflected.normalized, reflected.magnitude);
        }
    }
}
