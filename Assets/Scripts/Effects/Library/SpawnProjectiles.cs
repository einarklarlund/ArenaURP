using Arena.NetworkedMotion;
using FishNet;
using FishNet.Managing.Timing;
using UnityEngine;

/// <summary>
/// Spawn <see cref="Count"/> projectiles of <see cref="Type"/>, fanned evenly across
/// <see cref="SpreadDegrees"/> around the context direction (0 = all straight ahead, 360 = even ring).
/// This is the ONE spawn effect reused by both a projectile reaction (children at the impact point) and an
/// ability (a fan from the caster). The type builds + initializes each instance via its own Create; this
/// effect just sets the per-spawn payload and authoritatively spawns it.
/// </summary>
[CreateAssetMenu(menuName = "Arena/World Effects/Spawn Projectiles", fileName = "SpawnProjectiles")]
public sealed class SpawnProjectiles : WorldEffect
{
    public ProjectileType Type;
    [Min(1)] public int Count = 1;
    [Tooltip("0 = all along the context direction; 360 = even ring; in between = arc centred on it.")]
    public float SpreadDegrees = 0f;

    public override void Apply(in EffectContext ctx)
    {
        var manager = InstanceFinder.NetworkManager;
        if (Type == null || Count <= 0 || manager == null)
            return;

        PreciseTick tick = InstanceFinder.TimeManager.GetPreciseTick(TickType.Tick);
        for (int i = 0; i < Count; i++)
        {
            Vector3 dir = SpawnFanMath.Direction(ctx.Direction, i, Count, SpreadDegrees, Vector3.up);
            Quaternion rot = dir.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(dir) : Quaternion.identity;
            var nob = Type.Create(manager, ctx.Point, rot, asServer: true);
            if (nob.TryGetComponent<NetworkedMotion>(out var motion))
                motion.SetupSpawn(ctx.InvokerPlayerId, tick, ctx.Point, dir);
            manager.ServerManager.Spawn(nob);
        }
    }
}
