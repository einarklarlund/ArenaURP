using UnityEngine;

/// <summary>
/// Spawn a cosmetic VFX prefab at the hit point, oriented to the surface normal when there is one. The
/// operation owns the prefab (a serialized reference), so any collision reuses it. Authorable on all three
/// collision contexts (surface/player/projectile).
///
/// This is a LOCAL spawn (runs in the server-authoritative collision context). For host/editor play the host
/// sees it; to guarantee it on remote clients, make the prefab a networked object or add a broadcast.
/// </summary>
[System.Serializable]
public sealed class SpawnVfx :
    IEffect<ProjectileSurfaceCollision>,
    IEffect<ProjectilePlayerCollision>,
    IEffect<ProjectileProjectileCollision>
{
    [SerializeField, Tooltip("The VFX prefab to instantiate at the impact point.")]
    private GameObject prefab;

    public SpawnVfx() { }

    public SpawnVfx(GameObject prefab) => this.prefab = prefab;

    public void Apply(ProjectileSurfaceCollision ctx) => Spawn(ctx.Point, ctx.Normal);
    public void Apply(ProjectilePlayerCollision ctx) => Spawn(ctx.Point, ctx.Normal);
    public void Apply(ProjectileProjectileCollision ctx) => Spawn(ctx.Point, ctx.Normal);

    private void Spawn(Vector3 point, Vector3 normal)
    {
        if (prefab == null)
            return;
        Quaternion rot = normal.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(normal) : Quaternion.identity;
        Object.Instantiate(prefab, point, rot);
    }
}
