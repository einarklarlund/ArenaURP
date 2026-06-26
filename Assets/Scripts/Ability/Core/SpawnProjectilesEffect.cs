using System.Collections.Generic;
using Arena.Projectile;
using UnityEngine;

namespace Arena.Ability
{
    /// <summary>
    /// Active-ability effect: spawn <see cref="Count"/> projectiles of <see cref="Type"/> fanned evenly
    /// across <see cref="SpreadDegrees"/> around the caster's aim direction (in the horizontal plane).
    /// A full 360° spread produces an even ring; a smaller spread produces an arc centred on the aim.
    ///
    /// This reuses the projectile <see cref="WorldEffect.Spawn"/> vocabulary — the very same effect a
    /// projectile reaction emits — so the ability spawns ordinary networked, owned projectiles via the
    /// shared <see cref="ProjectileWorldProvider"/>. Pure and deterministic (no Random), so it is
    /// EditMode-testable and replay-safe.
    /// </summary>
    [System.Serializable]
    public sealed class SpawnProjectilesEffect : IAbilityEffect
    {
        public ProjectileType Type;
        public int Count = 8;
        public float SpreadDegrees = 360f;

        public void Collect(in AbilityContext ctx, List<WorldEffect> effects)
        {
            if (Type == null || Count <= 0)
                return;

            Vector3 baseDir = ctx.AimDirection.sqrMagnitude > 1e-4f
                ? ctx.AimDirection.normalized
                : Vector3.forward;

            bool fullRing = SpreadDegrees >= 359.999f;
            // Full ring: divide the circle into Count slices. Arc: place endpoints on the arc edges.
            float step = Count > 1 ? SpreadDegrees / (fullRing ? Count : Count - 1) : 0f;
            float start = fullRing ? 0f : -SpreadDegrees * 0.5f;

            for (int i = 0; i < Count; i++)
            {
                float angle = start + step * i;
                Vector3 dir = Quaternion.AngleAxis(angle, Vector3.up) * baseDir;
                effects.Add(WorldEffect.Spawn(Type, 1, ctx.Origin, dir, ctx.CasterPlayerId));
            }
        }
    }
}
