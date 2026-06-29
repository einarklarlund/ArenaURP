using Arena.NetworkedMotion;
using FishNet;
using FishNet.Managing.Timing;
using UnityEngine;

/// <summary>
/// Spawn <see cref="_count"/> projectiles of <see cref="_type"/>, fanned evenly across
/// <see cref="_spreadDegrees"/> around the context direction (0 = all straight ahead, 360 = even ring). The
/// ONE spawn effect reused by a projectile reaction (children at the impact point) and an ability (a fan
/// from the caster). Needs a direction to fan around; origin is the context's point, or the invoker's
/// position as a fallback.
/// </summary>
public sealed class SpawnProjectiles : IEffect
{
    readonly ProjectileType _type;
    readonly int _count;
    readonly float _spreadDegrees;

    /// <param name="spreadDegrees">0 = all along the context direction; 360 = even ring; in between = arc.</param>
    public SpawnProjectiles(ProjectileType type, int count = 1, float spreadDegrees = 0f)
    {
        _type = type;
        _count = count;
        _spreadDegrees = spreadDegrees;
    }

    public void Apply(IEffectContext ctx)
    {
        var manager = InstanceFinder.NetworkManager;
        if (_type == null || _count <= 0 || manager == null)
            return;
        if (ctx is not IDirectedContext directed)
            return;
        Vector3 origin = ctx is IPointContext point
            ? point.Point
            : (ctx.Invoker != null ? ctx.Invoker.transform.position : Vector3.zero);

        PreciseTick tick = InstanceFinder.TimeManager.GetPreciseTick(TickType.Tick);
        for (int i = 0; i < _count; i++)
        {
            Vector3 dir = SpawnFanMath.Direction(directed.Direction, i, _count, _spreadDegrees, Vector3.up);
            ProjectileSpawner.Spawn(_type, manager, ctx.InvokerPlayerId, tick, origin, dir, asServer: true);
        }
    }
}
