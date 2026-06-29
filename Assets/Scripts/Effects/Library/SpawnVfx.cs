using UnityEngine;

/// <summary>
/// Spawn a cosmetic VFX prefab at the hit point, oriented to the surface normal when there is one. The
/// effect owns the prefab (passed in code by whoever builds the reaction), so any reaction reuses it. Needs
/// at least a point; orients to the normal only on a surface context (otherwise spawns unoriented).
///
/// This is a LOCAL spawn (runs in the server-authoritative collision context). For host/editor play the host
/// sees it; to guarantee it on remote clients, make the prefab a networked object or add a broadcast.
/// </summary>
public sealed class SpawnVfx : IEffect
{
    readonly GameObject _prefab;

    public SpawnVfx(GameObject prefab) => _prefab = prefab;

    public void Apply(IEffectContext ctx)
    {
        if (_prefab == null || ctx is not IPointContext point)
            return;
        Quaternion rot = ctx is ISurfaceContext surface && surface.Normal.sqrMagnitude > 1e-4f
            ? Quaternion.LookRotation(surface.Normal)
            : Quaternion.identity;
        Object.Instantiate(_prefab, point.Point, rot);
    }
}
