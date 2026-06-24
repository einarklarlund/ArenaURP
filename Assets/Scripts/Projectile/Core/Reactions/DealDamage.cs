using System.Collections.Generic;

namespace Arena.Projectile
{
    /// <summary>
    /// Emits a damage effect against a hit player (ports ProjectileDamageableImpact). The death of this
    /// projectile (<see cref="DespawnAfter"/>) is set deterministically on every peer so prediction
    /// agrees; only the damage EFFECT is authoritative, and the shell gates it.
    /// </summary>
    [System.Serializable]
    public sealed class DealDamage : IHitReaction
    {
        public int Amount = 10;
        public bool DespawnAfter = true;

        public void Apply(in HitInfo hit, ref ProjectileState self, List<ProjectileEffect> effects)
        {
            if (hit.OtherPlayerId >= 0 && hit.OtherPlayerId != self.OwnerId)
            {
                effects.Add(ProjectileEffect.Damage(hit.OtherPlayerId, Amount, self.OwnerId, hit.Point, hit.Direction));
                if (DespawnAfter)
                    self.Health = 0; // death is intrinsic to Health (ProjectileState.Alive => Health > 0)
            }
        }
    }
}
