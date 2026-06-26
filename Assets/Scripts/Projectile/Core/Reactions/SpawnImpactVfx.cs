using System.Collections.Generic;

namespace Arena.Projectile
{
    /// <summary>
    /// Emits a COSMETIC impact-VFX effect (ports impactVfx). The shell gates this on
    /// <see cref="EffectGate.IsCosmetic"/> so it can be predicted on the owning client.
    /// </summary>
    [System.Serializable]
    public sealed class SpawnImpactVfx : IHitReaction
    {
        public ProjectileType Type; // may be null; the shell tolerates a null vfx type

        public void Apply(in HitInfo hit, ref ProjectileState self, List<WorldEffect> effects)
        {
            effects.Add(WorldEffect.Vfx(Type, hit.Point, hit.Normal));
        }
    }
}
