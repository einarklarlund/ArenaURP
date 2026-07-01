using Arena.NetworkedMotion;
using UnityEngine;

/// <summary>
/// Reflect the invoking projectile's velocity across the surface normal and continue on a new segment. Reads
/// the owner's <see cref="NetworkedMotion"/> for the live velocity, reflects it with the pure
/// <see cref="RicochetMath"/>, and begins a new authoritative segment (which syncs to every peer). It reads
/// point + normal + the owner's motion, so it is typed to <see cref="ISurfaceHitContext"/> — a normal is always
/// present, and it is authorable on any collision (reflect off a wall, a player, or another projectile).
/// </summary>
[System.Serializable]
public sealed class Ricochet : IEffect<ISurfaceHitContext>
{
    public void Apply(ISurfaceHitContext ctx)
    {
        if (ctx.Owner == null || !ctx.Owner.TryGetComponent<NetworkedMotion>(out var motion))
            return;

        var state = motion.State;
        Vector3 vel = NetworkedMotionProcessor.VelocityAt(motion.Strategy, in state);

        if (RicochetMath.TryReflect(vel, ctx.Normal, out Vector3 dir, out float speed))
            motion.BeginSegment(ctx.Point, dir, speed);
    }
}
