using UnityEngine;

/// <summary>
/// Spawn the invoking projectile's cosmetic impact VFX at the hit point, broadcast to every peer through
/// the projectile's <see cref="ImpactVfxBroadcaster"/>. No-op when there is no broadcaster (e.g. an ability
/// with no projectile instance).
/// </summary>
[CreateAssetMenu(menuName = "Arena/World Effects/Spawn Impact VFX", fileName = "SpawnImpactVfx")]
public sealed class SpawnImpactVfx : WorldEffect
{
    public override void Apply(in EffectContext ctx)
    {
        if (ctx.Invoker != null && ctx.Invoker.TryGetComponent<ImpactVfxBroadcaster>(out var broadcaster))
            broadcaster.Broadcast(ctx.Point, ctx.Normal);
    }
}
