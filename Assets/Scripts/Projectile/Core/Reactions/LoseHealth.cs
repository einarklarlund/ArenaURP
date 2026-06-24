using System.Collections.Generic;

namespace Arena.Projectile
{
    /// <summary>
    /// Subtracts health (ports ProjectileSurfaceImpact / DevilDaggers self-damage). Death at zero is
    /// enforced centrally by <see cref="ProjectileStepProcessor"/> (Health &lt;= 0 -> not Alive), so this
    /// reaction only needs to drain. Deterministic — never gated.
    /// </summary>
    [System.Serializable]
    public sealed class LoseHealth : IHitReaction
    {
        public int Amount = 1;

        public void Apply(in HitInfo hit, ref ProjectileState self, List<ProjectileEffect> effects)
        {
            self.Health -= Amount;
        }
    }
}
