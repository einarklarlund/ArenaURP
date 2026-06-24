using System.Collections.Generic;

namespace Arena.Projectile
{
    /// <summary>Emits an authoritative debuff effect against a hit player.</summary>
    [System.Serializable]
    public sealed class ApplyDebuff : IHitReaction
    {
        public DebuffType Debuff = DebuffType.Slow;

        public void Apply(in HitInfo hit, ref ProjectileState self, List<ProjectileEffect> effects)
        {
            if (hit.OtherPlayerId >= 0)
                effects.Add(ProjectileEffect.Debuff_(hit.OtherPlayerId, Debuff));
        }
    }
}
