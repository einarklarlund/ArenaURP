using UnityEngine;

/// <summary>
/// Spawn the invoking projectile's cosmetic impact VFX at the hit point, broadcast to every peer through
/// the projectile's <see cref="NetworkedMotion"/> (an ObserversRpc). No-op when there is no projectile
/// (e.g. an ability with no instance to broadcast through).
/// </summary>
[CreateAssetMenu(menuName = "Arena/World Effects/Spawn Impact VFX", fileName = "SpawnImpactVfx")]
public sealed class SpawnImpactVfx : WorldEffect
{
    public override void Apply(in EffectContext ctx)
    {
        if (ctx.Invoker != null && ctx.Invoker.TryGetComponent<NetworkedMotion>(out var motion))
            motion.BroadcastImpactVfx(ctx.Point, ctx.Normal);
    }
}
