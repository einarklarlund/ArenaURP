using Arena.NetworkedMotion;
using FishNet;
using FishNet.Managing.Timing;
using UnityEngine;

/// <summary>
/// Spawn <see cref="count"/> projectiles of <see cref="type"/>, fanned evenly across <see cref="spreadDegrees"/>
/// around the context's direction (0 = all straight ahead, 360 = even ring). The ONE spawn operation reused by
/// a projectile reaction (children at the impact point) AND an ability (a fan from the caster). It reads only
/// an origin + direction, so it is typed to <see cref="IAimContext"/> — a single <see cref="Apply(IAimContext)"/>
/// that is authorable on every collision context and on <see cref="AbilityAim"/>. Origin is the context's point.
/// </summary>
[System.Serializable]
public sealed class SpawnProjectiles : IEffect<IAimContext>
{
    [SerializeField, Tooltip("The projectile kind to spawn.")]
    private ProjectileType type;
    [SerializeField, Min(1), Tooltip("How many to spawn.")]
    private int count = 1;
    [SerializeField, Tooltip("0 = all along the context direction; 360 = even ring; in between = arc.")]
    private float spreadDegrees = 0f;

    public SpawnProjectiles() { }

    public SpawnProjectiles(ProjectileType type, int count = 1, float spreadDegrees = 0f)
    {
        this.type = type;
        this.count = count;
        this.spreadDegrees = spreadDegrees;
    }

    public void Apply(IAimContext ctx) => Spawn(OwnerId(ctx), ctx.Point, ctx.Direction);

    /// <summary>The owning player's id to attribute spawned projectiles to, or -1 when the owner is unresolved.</summary>
    private static int OwnerId(IEffectContext ctx) => ctx.OwnerPlayer != null ? ctx.OwnerPlayer.ObjectId : -1;

    private void Spawn(int ownerId, Vector3 origin, Vector3 direction)
    {
        var manager = InstanceFinder.NetworkManager;
        if (type == null || count <= 0 || manager == null)
            return;

        PreciseTick tick = InstanceFinder.TimeManager.GetPreciseTick(TickType.Tick);
        for (int i = 0; i < count; i++)
        {
            Vector3 dir = SpawnFanMath.Direction(direction, i, count, spreadDegrees, Vector3.up);
            ProjectileSpawner.Spawn(type, manager, ownerId, tick, origin, dir, asServer: true);
        }
    }
}
