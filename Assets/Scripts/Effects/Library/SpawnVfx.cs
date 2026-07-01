using UnityEngine;

/// <summary>
/// Spawn a cosmetic VFX prefab at the hit point, oriented to the surface normal. It reads only point + normal,
/// so it is typed to <see cref="ISurfaceHitContext"/> — a single <see cref="Apply(ISurfaceHitContext)"/> that is
/// authorable on every collision context (surface/player/projectile), but not on an ability aim (no normal).
///
/// This is a LOCAL spawn (runs in the server-authoritative collision context). For host/editor play the host
/// sees it; to guarantee it on remote clients, make the prefab a networked object or add a broadcast.
/// </summary>
[System.Serializable]
public sealed class SpawnVfx : IEffect<ISurfaceHitContext>
{
    [SerializeField, Tooltip("The VFX prefab to instantiate at the impact point.")]
    private GameObject prefab;

    public SpawnVfx() { }

    public SpawnVfx(GameObject prefab) => this.prefab = prefab;

    public void Apply(ISurfaceHitContext ctx) => Spawn(ctx.Point, ctx.Normal);

    private void Spawn(Vector3 point, Vector3 normal)
    {
        if (prefab == null)
            return;
        Quaternion rot = normal.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(normal) : Quaternion.identity;
        Object.Instantiate(prefab, point, rot);
    }
}
