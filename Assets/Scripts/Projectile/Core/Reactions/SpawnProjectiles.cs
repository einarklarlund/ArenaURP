using System.Collections.Generic;

namespace Arena.Projectile
{
    /// <summary>Emits a spawn effect for N child projectiles at the impact point (ports spawnOnDeath).</summary>
    [System.Serializable]
    public sealed class SpawnProjectiles : IHitReaction
    {
        public ProjectileType Type;
        public int Count = 1;

        public void Apply(in HitInfo hit, ref ProjectileState self, List<WorldEffect> effects)
        {
            if (Type == null || Count <= 0)
                return;
            effects.Add(WorldEffect.Spawn(Type, Count, hit.Point, self.OriginForward, self.OwnerId));
        }
    }
}
