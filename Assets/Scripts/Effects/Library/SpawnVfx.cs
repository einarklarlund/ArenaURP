using UnityEngine;

/// <summary>
/// Spawn a cosmetic VFX prefab at the hit point, oriented to the surface normal. Authored like any other
/// reaction — drop it into a HitReaction's effect list. The effect owns the prefab, so any reaction (any
/// projectile, any ability) can reuse it.
///
/// This is a LOCAL spawn (it runs in the server-authoritative collision context). For host/editor play the
/// host sees it; to guarantee it on remote clients too, make <see cref="VfxPrefab"/> a networked object or
/// add a broadcast — intentionally left simple here.
/// </summary>
[CreateAssetMenu(menuName = "Arena/World Effects/Spawn VFX", fileName = "SpawnVfx")]
public sealed class SpawnVfx : WorldEffect
{
    [Tooltip("Cosmetic VFX prefab instantiated at the hit point (should self-despawn, e.g. a particle system).")]
    public GameObject VfxPrefab;

    public override void Apply(in EffectContext ctx)
    {
        if (VfxPrefab == null)
            return;
        Quaternion rot = ctx.Normal.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(ctx.Normal) : Quaternion.identity;
        Object.Instantiate(VfxPrefab, ctx.Point, rot);
    }
}
