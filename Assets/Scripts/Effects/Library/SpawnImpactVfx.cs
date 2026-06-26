using UnityEngine;

/// <summary>
/// Spawn a cosmetic impact VFX at the hit point. Broadcast to every peer through the invoking projectile's
/// <see cref="NetworkedMotion"/> (an ObserversRpc); falls back to a local spawn when there is no projectile
/// (e.g. an ability). The VFX prefab is resolved from <see cref="VfxType"/> via the provider's catalog;
/// leave it null to use the projectile's own type.
/// </summary>
[CreateAssetMenu(menuName = "Arena/World Effects/Spawn Impact VFX", fileName = "SpawnImpactVfx")]
public sealed class SpawnImpactVfx : WorldEffect
{
    [Tooltip("Which type's impact VFX to spawn; null = the invoking projectile's own type.")]
    public ProjectileType VfxType;

    public override void Apply(in EffectContext ctx)
    {
        if (ctx.Invoker != null && ctx.Invoker.TryGetComponent<NetworkedMotion>(out var motion))
            motion.BroadcastImpactVfx(VfxType, ctx.Point, ctx.Normal);
        else
            ProjectileWorldProvider.Instance?.SpawnVfx(VfxType, ctx.Point, ctx.Normal);
    }
}
